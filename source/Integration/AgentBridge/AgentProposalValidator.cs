using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

internal enum AgentVerdictStatus
{
    Valid,
    Warning,
    Rejected
}

/// <summary>
/// One row of the review: what the operation would do to which channel, stated
/// in the words the panel uses, and whether it may be applied. A rejected row
/// keeps its current/proposed text so the user can see what was asked and why it
/// was refused; it can never be ticked.
/// </summary>
internal sealed record AgentOperationVerdict(
    string Id,
    string ChannelLabel,
    string Parameter,
    string Current,
    string Proposed,
    AgentVerdictStatus Status,
    string Message,
    string Reason,
    AgentOperation? Operation,
    AgentChannelSnapshot? Channel)
{
    public bool Applicable =>
        Status != AgentVerdictStatus.Rejected && Operation != null && Channel != null;
}

/// <summary>A reply judged against a session: the rows, plus reply-level warnings.</summary>
internal sealed record AgentProposalReview(
    AgentProposal Proposal,
    IReadOnlyList<AgentOperationVerdict> Verdicts,
    IReadOnlyList<string> Warnings)
{
    public bool HasApplicable => Verdicts.Any(verdict => verdict.Applicable);
}

/// <summary>
/// Decides, operation by operation, whether a proposal may be applied to the
/// session in front of it. Admissibility only — a valid proposal can still be a
/// worse tune, which is why the review never calls anything "verified". Every
/// trial edit is made on a copy of the channel's settings and judged by the same
/// <see cref="VirtualCrossoverChannelSettings.Validate"/> the session loader
/// runs, so the bridge cannot let in a value the file format would refuse.
/// </summary>
internal static class AgentProposalValidator
{
    // The channel block's own gain and delay fields — the range a human can dial,
    // narrower than what the FILE accepts (±60 dB, 1000 ms), because the block
    // would clamp a wider value on the first touch and the tune would silently
    // move. Mirrors VirtualCrossoverChannelControl's numericGain/numericDelay,
    // and AgentProposalValidatorTests pins the two together.
    public const double MinimumGainDb = -60;
    public const double MaximumGainDb = 20;
    public const double GainStepDb = 0.1;
    public const double MinimumDelayMs = 0;
    public const double MaximumDelayMs = 100;
    public const double DelayStepMs = 0.01;

    public const string DeviceLimitsUnknown =
        "Device limits unknown; only Virtual DSP limits were checked.";

    // Below this a net rise is bilinear warping and rounding, not a boost.
    private const double HeadroomToleranceDb = 0.05;

    public static AgentProposalReview Review(AgentProposal proposal, AgentSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(session);

        var warnings = new List<string>();
        if (proposal.PackageId != null && session.LastPackageId != null &&
            !string.Equals(proposal.PackageId, session.LastPackageId, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "The reply answers a different package than the one last copied from this " +
                "session (or the session was reopened since). The current values below decide.");
        }

        var verdicts = new List<AgentOperationVerdict>();
        foreach (AgentRejectedOperation rejected in proposal.Rejected)
        {
            verdicts.Add(new AgentOperationVerdict(
                rejected.Id ?? "?", string.Empty, rejected.Op ?? "?", string.Empty, string.Empty,
                AgentVerdictStatus.Rejected, rejected.Problem, string.Empty, null, null));
        }
        foreach (AgentOperation operation in proposal.Operations)
        {
            verdicts.Add(Judge(operation, session));
        }

        // Two applicable operations on one channel's same parameter cannot both be
        // meant; neither is picked over the other.
        foreach (IGrouping<(string, string), AgentOperationVerdict> group in verdicts
            .Where(verdict => verdict.Applicable)
            .GroupBy(verdict => (verdict.Channel!.Id, verdict.Parameter))
            .Where(group => group.Count() > 1))
        {
            foreach (AgentOperationVerdict verdict in group)
            {
                string others = string.Join(", ", group
                    .Where(other => !ReferenceEquals(other, verdict))
                    .Select(other => other.Id));
                verdicts[verdicts.IndexOf(verdict)] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = $"Conflicts with {others} on the same channel and parameter."
                };
            }
        }

        return new AgentProposalReview(proposal, verdicts, warnings);
    }

    /// <summary>
    /// The whole-set check a commit runs on the rows the user ticked: every ticked
    /// operation applied to a copy of its channel, and the copy validated as a
    /// whole. Null when the set is admissible; otherwise the first problem, which
    /// refuses the whole commit — never a partial one.
    /// </summary>
    public static string? CheckSelection(IEnumerable<AgentOperationVerdict> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        foreach (IGrouping<AgentChannelSnapshot, AgentOperationVerdict> group in selected
            .Where(verdict => verdict.Applicable)
            .GroupBy(verdict => verdict.Channel!))
        {
            VirtualCrossoverChannelSettings copy = AgentOperations.CloneEditable(group.Key.Settings);
            try
            {
                foreach (AgentOperationVerdict verdict in group)
                {
                    AgentOperations.Apply(verdict.Operation!, copy);
                }
                copy.Validate();
            }
            catch (InvalidDataException exception)
            {
                return $"{group.Key.Label}: {exception.Message}";
            }
        }

        return null;
    }

    private static AgentOperationVerdict Judge(AgentOperation operation, AgentSessionSnapshot session)
    {
        AgentChannelSnapshot? channel = session.Find(operation.ChannelId);
        if (channel == null)
        {
            return Rejected(operation, null, string.Empty, string.Empty,
                $"Unknown channel '{operation.ChannelId}'; the package names the channels this session has.");
        }

        VirtualCrossoverChannelSettings settings = channel.Settings;
        string current = Describe(operation, settings);

        string? stale = CheckExpected(operation, settings);
        if (stale != null)
        {
            return Rejected(operation, channel, current, string.Empty,
                "The value changed since the package was copied: " + stale);
        }

        var notes = new List<string>();
        VirtualCrossoverChannelSettings copy = AgentOperations.CloneEditable(settings);
        string? problem = CheckValue(operation, session, copy, notes);
        if (problem != null)
        {
            return Rejected(operation, channel, current, string.Empty, problem);
        }

        string proposed = Describe(operation, copy);
        if (IsNoChange(operation, settings, copy))
        {
            return Rejected(operation, channel, current, proposed, "No change.");
        }

        return new AgentOperationVerdict(
            operation.Id, channel.Label, operation.Parameter, current, proposed,
            notes.Count > 0 ? AgentVerdictStatus.Warning : AgentVerdictStatus.Valid,
            notes.Count > 0 ? string.Join(" ", notes) : "OK",
            operation.Reason, operation, channel);
    }

    private static AgentOperationVerdict Rejected(
        AgentOperation operation, AgentChannelSnapshot? channel, string current, string proposed,
        string message) =>
        new(operation.Id, channel?.Label ?? string.Empty, operation.Parameter, current, proposed,
            AgentVerdictStatus.Rejected, message, operation.Reason, operation, channel);

    // Exact comparison: the package prints every value in round-trip form, so an
    // assistant that copied it hands back the same double. A tolerance here would
    // be a tolerance for a reply that was reasoned about a different value.
    private static string? CheckExpected(AgentOperation operation, VirtualCrossoverChannelSettings settings)
    {
        switch (operation)
        {
            case SetGainOperation gain when gain.ExpectedCurrentDb != settings.GainDb:
                return $"gain is {Db(settings.GainDb)}, the reply expected {Db(gain.ExpectedCurrentDb)}.";
            case SetDelayOperation delay when delay.ExpectedCurrentMs != settings.DelayMs:
                return $"delay is {Ms(settings.DelayMs)}, the reply expected {Ms(delay.ExpectedCurrentMs)}.";
            case SetPolarityOperation polarity when polarity.ExpectedCurrentInverted != settings.InvertPolarity:
                return $"polarity is {Polarity(settings.InvertPolarity)}, the reply expected " +
                    $"{Polarity(polarity.ExpectedCurrentInverted)}.";
            case SetCrossoverOperation crossover when
                !AgentOperations.MatchesCrossover(crossover.ExpectedCurrent, settings, out string? mismatch):
                return mismatch;
            case ReplacePeqBankOperation peq when
                !string.Equals(peq.ExpectedCurrentHash,
                    AgentPeqHash.Compute(settings.PeqPreampDb, settings.PeqBands),
                    StringComparison.OrdinalIgnoreCase):
                return "the PEQ bank is not the one the reply describes.";
            default:
                return null;
        }
    }

    // Applies the operation to the copy and says what is wrong with the value, if
    // anything; notes collect the warnings that do not refuse it.
    private static string? CheckValue(
        AgentOperation operation,
        AgentSessionSnapshot session,
        VirtualCrossoverChannelSettings copy,
        List<string> notes)
    {
        double nyquistHz = session.ProcessorSampleRateHz / 2.0;
        switch (operation)
        {
            case SetGainOperation gain:
                if (!double.IsFinite(gain.ProposedDb) ||
                    gain.ProposedDb < MinimumGainDb || gain.ProposedDb > MaximumGainDb)
                {
                    return $"Gain must be between {Db(MinimumGainDb)} and {Db(MaximumGainDb)}.";
                }
                if (!OnStep(gain.ProposedDb, GainStepDb))
                {
                    return $"Gain must be a multiple of {GainStepDb.ToString("0.0", CultureInfo.InvariantCulture)} dB.";
                }
                break;

            case SetDelayOperation delay:
                if (!double.IsFinite(delay.ProposedMs) ||
                    delay.ProposedMs < MinimumDelayMs || delay.ProposedMs > MaximumDelayMs)
                {
                    return $"Delay must be between {Ms(MinimumDelayMs)} and {Ms(MaximumDelayMs)}.";
                }
                if (!OnStep(delay.ProposedMs, DelayStepMs))
                {
                    return $"Delay must be a multiple of {DelayStepMs.ToString("0.00", CultureInfo.InvariantCulture)} ms.";
                }
                if (delay.ProposedMs > session.MaxDelayMs)
                {
                    notes.Add($"Above the processor's delay ceiling of {Ms(session.MaxDelayMs)}.");
                }
                break;

            case SetCrossoverOperation crossover:
                if (!AgentOperations.TryMapCrossover(
                    crossover.Proposed, copy, out CrossoverKind kind,
                    out CrossoverEdge highPass, out CrossoverEdge lowPass, out string? problem))
                {
                    return problem;
                }
                if ((kind is CrossoverKind.HighPass or CrossoverKind.BandPass && highPass.FrequencyHz >= nyquistHz) ||
                    (kind is CrossoverKind.LowPass or CrossoverKind.BandPass && lowPass.FrequencyHz >= nyquistHz))
                {
                    return $"A crossover corner must sit below the processor's Nyquist of {Hz(nyquistHz)}.";
                }
                notes.Add(DeviceLimitsUnknown);
                break;

            case ReplacePeqBankOperation peq:
                if (!AgentOperations.TryMapBank(peq.Proposed, out _, out List<PeqBand> bands, out problem))
                {
                    return problem;
                }
                if (bands.Any(band => band.FrequencyHz >= nyquistHz))
                {
                    return $"Every PEQ band must sit below the processor's Nyquist of {Hz(nyquistHz)}.";
                }
                // Headroom is judged on the NET response, never on a band's sign: a
                // boost inside a wider cut, or under a negative preamp, asks the
                // device for nothing; a net rise above unity is where a full-scale
                // signal clips. A warning, not a refusal — the user may know the
                // source never reaches full scale.
                (double peakDb, double peakHz) = AgentPeqHeadroom.Peak(
                    peq.Proposed.PreampDb, bands, session.ProcessorSampleRateHz);
                if (peakDb > HeadroomToleranceDb)
                {
                    notes.Add(
                        $"The bank's net response rises to +{Db(peakDb)} at " +
                        $"{Hz(AgentCurveSampling.Frequency(peakHz))}: " +
                        $"trim the boost, or lower the preamp by {Db(peakDb)}.");
                }
                notes.Add(DeviceLimitsUnknown);
                break;
        }

        try
        {
            AgentOperations.Apply(operation, copy);
            copy.Validate();
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
        }

        return null;
    }

    private static bool IsNoChange(
        AgentOperation operation,
        VirtualCrossoverChannelSettings before,
        VirtualCrossoverChannelSettings after) => operation switch
    {
        SetGainOperation => before.GainDb == after.GainDb,
        SetDelayOperation => before.DelayMs == after.DelayMs,
        SetPolarityOperation => before.InvertPolarity == after.InvertPolarity,
        SetCrossoverOperation => before.CrossoverKind == after.CrossoverKind &&
            before.HighPassEdge == after.HighPassEdge && before.LowPassEdge == after.LowPassEdge,
        _ => before.PeqPreampDb == after.PeqPreampDb && before.PeqBands.SequenceEqual(after.PeqBands)
    };

    private static bool OnStep(double value, double step)
    {
        double steps = value / step;
        return Math.Abs(steps - Math.Round(steps)) < 1e-6;
    }

    private static string Describe(AgentOperation operation, VirtualCrossoverChannelSettings settings) =>
        operation switch
        {
            SetGainOperation => Db(settings.GainDb),
            SetDelayOperation => Ms(settings.DelayMs),
            SetPolarityOperation => Polarity(settings.InvertPolarity),
            SetCrossoverOperation => Crossover(settings),
            _ => $"{settings.PeqBands.Count} band{(settings.PeqBands.Count == 1 ? "" : "s")}, " +
                $"preamp {Db(settings.PeqPreampDb)}"
        };

    // A crossover in a table cell: the tuning sheet's full wording does not fit
    // one, so the family is abbreviated the way the channel block's own combo
    // does and the edge is named by its role.
    private static string Crossover(VirtualCrossoverChannelSettings settings) =>
        settings.CrossoverKind switch
        {
            CrossoverKind.LowPass => "LP " + Edge(settings.LowPassEdge),
            CrossoverKind.HighPass => "HP " + Edge(settings.HighPassEdge),
            CrossoverKind.BandPass => "HP " + Edge(settings.HighPassEdge) + " + LP " + Edge(settings.LowPassEdge),
            _ => VirtualCrossoverSheet.OffText
        };

    private static string Edge(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "LR",
            CrossoverFilterFamily.Butterworth => "BW",
            CrossoverFilterFamily.Bessel => "Bessel",
            _ => "Cheb"
        };
        string ripple = edge.Family == CrossoverFilterFamily.Chebyshev
            ? $" {edge.RippleDb.ToString("0.#", CultureInfo.InvariantCulture)} dB"
            : string.Empty;
        return $"{family}{edge.SlopeDbPerOctave} {Hz(edge.FrequencyHz)}{ripple}";
    }

    // "+ 0" folds a negative zero, which would otherwise print as "-0.0".
    private static string Db(double value) =>
        (value + 0).ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    private static string Ms(double value) =>
        (value + 0).ToString("0.00", CultureInfo.InvariantCulture) + " ms";

    private static string Hz(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";

    private static string Polarity(bool inverted) => inverted ? "Inverted" : "Normal";
}

/// <summary>
/// The one place an operation touches channel settings: the same code path judges
/// a copy in the review and writes the live object at commit, so what was
/// reviewed is what is applied.
/// </summary>
internal static class AgentOperations
{
    /// <summary>
    /// A copy holding the editable chain only — no source, history or path — for
    /// the review to try edits on and validate.
    /// </summary>
    public static VirtualCrossoverChannelSettings CloneEditable(VirtualCrossoverChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new VirtualCrossoverChannelSettings
        {
            DisplayName = settings.DisplayName,
            GainDb = settings.GainDb,
            DelayMs = settings.DelayMs,
            InvertPolarity = settings.InvertPolarity,
            CrossoverKind = settings.CrossoverKind,
            LowPassEdge = settings.LowPassEdge,
            HighPassEdge = settings.HighPassEdge,
            PeqPreampDb = settings.PeqPreampDb,
            PeqBands = new List<PeqBand>(settings.PeqBands),
            PeqSourceName = settings.PeqSourceName
        };
    }

    /// <summary>Writes the operation into the settings; the review has already passed it.</summary>
    /// <exception cref="InvalidDataException">The operation's value cannot be mapped.</exception>
    public static void Apply(AgentOperation operation, VirtualCrossoverChannelSettings target)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(target);

        switch (operation)
        {
            case SetGainOperation gain:
                target.GainDb = gain.ProposedDb;
                break;
            case SetDelayOperation delay:
                target.DelayMs = delay.ProposedMs;
                break;
            case SetPolarityOperation polarity:
                target.InvertPolarity = polarity.ProposedInverted;
                break;
            case SetCrossoverOperation crossover:
                if (!TryMapCrossover(crossover.Proposed, target, out CrossoverKind kind,
                    out CrossoverEdge highPass, out CrossoverEdge lowPass, out string? problem))
                {
                    throw new InvalidDataException(problem);
                }
                target.CrossoverKind = kind;
                target.HighPassEdge = highPass;
                target.LowPassEdge = lowPass;
                break;
            case ReplacePeqBankOperation peq:
                if (!TryMapBank(peq.Proposed, out double preampDb, out List<PeqBand> bands, out problem))
                {
                    throw new InvalidDataException(problem);
                }
                target.PeqPreampDb = preampDb;
                target.PeqBands = bands;
                break;
            default:
                throw new InvalidDataException("Unsupported operation.");
        }
    }

    /// <summary>
    /// The crossover a reply states, resolved against the channel's stored edges:
    /// an edge the reply omits keeps the stored one (the kind may not use it, but
    /// it round-trips like the project's own), and a ripple it omits keeps the
    /// stored ripple. The edges the kind USES must be stated.
    /// </summary>
    public static bool TryMapCrossover(
        AgentCrossover crossover,
        VirtualCrossoverChannelSettings current,
        out CrossoverKind kind,
        out CrossoverEdge highPass,
        out CrossoverEdge lowPass,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(crossover);
        ArgumentNullException.ThrowIfNull(current);

        highPass = current.HighPassEdge;
        lowPass = current.LowPassEdge;
        kind = CrossoverKind.Off;
        if (!TryParseName(crossover.Kind, out kind))
        {
            problem = $"Unknown crossover kind '{crossover.Kind}'; " +
                $"use one of {Names<CrossoverKind>()}.";
            return false;
        }

        bool usesHigh = kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool usesLow = kind is CrossoverKind.LowPass or CrossoverKind.BandPass;
        if (usesHigh && crossover.HighPass == null)
        {
            problem = $"A {kind} crossover needs its highPass edge.";
            return false;
        }
        if (usesLow && crossover.LowPass == null)
        {
            problem = $"A {kind} crossover needs its lowPass edge.";
            return false;
        }

        if (crossover.HighPass != null && !TryMapEdge(crossover.HighPass, current.HighPassEdge, out highPass, out problem))
        {
            return false;
        }
        if (crossover.LowPass != null && !TryMapEdge(crossover.LowPass, current.LowPassEdge, out lowPass, out problem))
        {
            return false;
        }

        problem = null;
        return true;
    }

    private static bool TryMapEdge(
        AgentCrossoverEdge edge, CrossoverEdge stored, out CrossoverEdge mapped, out string? problem)
    {
        mapped = stored;
        if (!TryParseName(edge.Family, out CrossoverFilterFamily family))
        {
            problem = $"Unknown crossover family '{edge.Family}'; " +
                $"use one of {Names<CrossoverFilterFamily>()}.";
            return false;
        }
        if (!CrossoverFilter.SupportedSlopes(family).Contains(edge.SlopeDbPerOctave))
        {
            problem = $"{family} offers slopes of " +
                $"{string.Join(", ", CrossoverFilter.SupportedSlopes(family))} dB/oct, " +
                $"not {edge.SlopeDbPerOctave}.";
            return false;
        }

        mapped = new CrossoverEdge(
            family, edge.FrequencyHz, edge.SlopeDbPerOctave, edge.RippleDb ?? stored.RippleDb);
        problem = null;
        return true;
    }

    /// <summary>
    /// Whether the crossover the reply believes is current IS current: same kind,
    /// and the edges that kind uses equal in family, corner and slope (and ripple
    /// where the reply states one). Edges the kind ignores are not compared.
    /// </summary>
    public static bool MatchesCrossover(
        AgentCrossover expected, VirtualCrossoverChannelSettings settings, out string? mismatch)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryParseName(expected.Kind, out CrossoverKind kind))
        {
            mismatch = $"the reply's expected crossover kind '{expected.Kind}' is not a kind.";
            return false;
        }
        if (kind != settings.CrossoverKind)
        {
            mismatch = $"the crossover is {settings.CrossoverKind}, the reply expected {kind}.";
            return false;
        }

        if (kind is CrossoverKind.HighPass or CrossoverKind.BandPass &&
            !EdgeMatches(expected.HighPass, settings.HighPassEdge, "high-pass", out mismatch))
        {
            return false;
        }
        if (kind is CrossoverKind.LowPass or CrossoverKind.BandPass &&
            !EdgeMatches(expected.LowPass, settings.LowPassEdge, "low-pass", out mismatch))
        {
            return false;
        }

        mismatch = null;
        return true;
    }

    private static bool EdgeMatches(
        AgentCrossoverEdge? expected, CrossoverEdge stored, string name, out string? mismatch)
    {
        if (expected == null)
        {
            mismatch = $"the reply states no expected {name} edge.";
            return false;
        }
        if (!TryParseName(expected.Family, out CrossoverFilterFamily family) ||
            family != stored.Family ||
            expected.FrequencyHz != stored.FrequencyHz ||
            expected.SlopeDbPerOctave != stored.SlopeDbPerOctave ||
            (expected.RippleDb is { } ripple && ripple != stored.RippleDb))
        {
            mismatch = $"the {name} edge is not the one the reply expected.";
            return false;
        }

        mismatch = null;
        return true;
    }

    /// <summary>The bank a reply states, as <see cref="PeqBand"/>s; Q is taken as RBJ, untouched.</summary>
    public static bool TryMapBank(
        AgentPeqBank bank, out double preampDb, out List<PeqBand> bands, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(bank);

        preampDb = bank.PreampDb;
        bands = new List<PeqBand>(bank.Bands.Count);
        if (bank.Bands.Count > EqualizationCurve.MaxBandCount)
        {
            problem = $"A PEQ bank holds at most {EqualizationCurve.MaxBandCount} bands.";
            return false;
        }

        foreach (AgentPeqBand band in bank.Bands)
        {
            if (!TryParseName(band.Type, out PeqBandType type))
            {
                problem = $"Unknown PEQ band type '{band.Type}'; use one of {Names<PeqBandType>()}.";
                return false;
            }

            bands.Add(new PeqBand(band.FrequencyHz, band.Q, band.GainDb, type));
        }

        problem = null;
        return true;
    }

    // The enum names exactly as the package prints them: no case games, and no
    // numeric strings, which Enum.TryParse would otherwise accept.
    private static bool TryParseName<TEnum>(string? name, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return !string.IsNullOrEmpty(name) &&
            char.IsLetter(name[0]) &&
            Enum.TryParse(name, ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }

    private static string Names<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>());
}
