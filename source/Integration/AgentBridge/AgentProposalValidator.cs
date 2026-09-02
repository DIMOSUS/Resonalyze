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
        Status != AgentVerdictStatus.Rejected && Operation != null &&
        (Operation is not AgentChannelOperation || Channel != null);
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

    // The Auto delay dialog's own fields, and the panel's Target Level field,
    // restated here for the same reason and pinned to their controls by the same
    // test: an engine request outside them would be clamped on the first touch,
    // and the run would not be the one that was reviewed.
    public const double MinimumSceneOffsetMs = 0;
    public const double MaximumSceneOffsetMs = 5;
    public const double SceneOffsetStepMs = 0.01;
    public const double MinimumNearSideCutDb = 0;
    public const double MaximumNearSideCutDb = 6;
    public const double NearSideCutStepDb = 0.1;
    public const double MinimumRearFillOffsetMs = 0;
    public const double MaximumRearFillOffsetMs = 30;
    public const double RearFillOffsetStepMs = 0.1;
    public const double MinimumTargetLevelDb = -120;
    public const double MaximumTargetLevelDb = 60;
    public const double TargetLevelStepDb = 1;

    /// <summary>The two curves an auto-tune request may name as its source.</summary>
    public const string PointSource = "point";
    public const string SpatialAverageSource = "spatialAverage";

    /// <summary>How the review's Channel column names a row about the whole project.</summary>
    public const string AllChannels = "all";

    public const string DeviceLimitsUnknown =
        "Device limits unknown; only Virtual DSP limits were checked.";

    // Below this a net rise is bilinear warping and rounding, not a boost.
    private const double HeadroomToleranceDb = 0.05;

    /// <summary>
    /// The widest bell the review lets pass without comment inside a junction
    /// zone — an octave to each side of one of the channel's own active corners,
    /// the same span the panel's junction band covers. A narrower bell turns the
    /// channel's phase by tens of degrees right where the pair's sum is built on
    /// it, and the dip it aims at is, that close to a crossover, more often the
    /// pair's interference than the driver's own.
    /// </summary>
    public const double JunctionQLimit = 2;

    // The corner whose junction zone a too-narrow bell sits in, or null. Only
    // bells: shelves are wide by nature, and an all-pass at a junction is there
    // for the phase on purpose.
    private static double? JunctionCornerNear(VirtualCrossoverChannelSettings settings, PeqBand band)
    {
        if (band.Type != PeqBandType.Peaking || band.Q <= JunctionQLimit)
        {
            return null;
        }

        bool usesHigh = settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool usesLow = settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass;
        foreach (double cornerHz in new[]
        {
            usesHigh ? settings.HighPassEdge.FrequencyHz : double.NaN,
            usesLow ? settings.LowPassEdge.FrequencyHz : double.NaN
        })
        {
            if (double.IsFinite(cornerHz) &&
                band.FrequencyHz >= cornerHz / 2 && band.FrequencyHz <= cornerHz * 2)
            {
                return cornerHz;
            }
        }

        return null;
    }

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
            .Where(verdict => verdict.Applicable && verdict.Operation is AgentSettingsOperation)
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

        RejectRepeatedEngineRequests(verdicts);
        RejectDisagreeingTargetLevels(verdicts);
        RejectOverwrittenSettings(verdicts, session);

        // Some warnings are about the channel as it would END UP, not about one
        // operation: a bell that lands in a junction zone because the crossover
        // moved, or a bank proposed against a crossover another row moves. Every
        // channel's applicable rows are applied together to a copy and judged
        // there; the notes go on the rows that shape that state. LAST, after every
        // refusal above: a row this pass reads is a row that is going to be
        // applied, and a note read off one that has just been refused would
        // describe a state nothing will produce.
        AddFinalStateNotes(verdicts);
        return new AgentProposalReview(proposal, verdicts, warnings);
    }

    // The panel runs each engine once per import, and a second request for the
    // same one carries a second set of inputs. The first is kept rather than the
    // set silently deciding which won — the reply listed them in an order.
    private static void RejectRepeatedEngineRequests(List<AgentOperationVerdict> verdicts)
    {
        var first = new Dictionary<(string Op, string? ChannelId), string>();
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (!verdict.Applicable || verdict.Operation is null or AgentSettingsOperation)
            {
                continue;
            }

            (string, string?) key = (verdict.Operation.Op, ChannelIdOf(verdict.Operation));
            if (first.TryGetValue(key, out string? owner))
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = $"Already requested by {owner}; an engine runs once per import."
                };
            }
            else
            {
                first[key] = verdict.Id;
            }
        }
    }

    // The target level is one datum for the whole project, and an Auto-tune that
    // states one moves it. Two requests stating different levels would fit one
    // bank at a level the project no longer holds by the time the other has run,
    // so only the first stated level stands and the rest are refused naming it.
    private static void RejectDisagreeingTargetLevels(List<AgentOperationVerdict> verdicts)
    {
        (string Id, double LevelDb)? first = null;
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (!verdict.Applicable ||
                verdict.Operation is not AutoTunePeqOperation { TargetLevelDb: { } level })
            {
                continue;
            }

            if (first is not { } stated)
            {
                first = (verdict.Id, level);
            }
            else if (!stated.LevelDb.Equals(level))
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = $"The target level is one datum for the whole project; " +
                        $"{stated.Id} already states {Db(stated.LevelDb)}."
                };
            }
        }
    }

    // An engine and a hand-written value the engine is going to write over cannot
    // both be meant. The engine keeps its row — it is the one that computes the
    // number — and the hand-written row is refused, naming what would have erased
    // it. Only an engine this build can RUN erases anything: a request refused as
    // unavailable leaves the hand-written rows to do the work instead.
    private static void RejectOverwrittenSettings(
        List<AgentOperationVerdict> verdicts, AgentSessionSnapshot session)
    {
        foreach (AgentOperationVerdict engine in verdicts
            .Where(verdict => verdict.Applicable && verdict.Operation is not AgentSettingsOperation)
            .ToList())
        {
            for (int index = 0; index < verdicts.Count; index++)
            {
                AgentOperationVerdict verdict = verdicts[index];
                if (verdict.Applicable &&
                    verdict.Operation is AgentSettingsOperation written &&
                    Overwrites(engine.Operation!, written, session))
                {
                    verdicts[index] = verdict with
                    {
                        Status = AgentVerdictStatus.Rejected,
                        Message = $"Would be overwritten by {engine.Parameter} ({engine.Id})."
                    };
                }
            }
        }
    }

    /// <summary>
    /// Whether running <paramref name="engine"/> would write over what
    /// <paramref name="written"/> asks for. Read off what the panel's own buttons
    /// do: Auto delay writes delay and polarity, and gains when the balance is
    /// asked for; the crossover wizard writes both corners and the cut-only gain
    /// of every channel it proposes for; Auto-tune replaces one channel's bank.
    /// </summary>
    public static bool Overwrites(
        AgentOperation engine, AgentSettingsOperation written, AgentSessionSnapshot session) =>
        engine switch
        {
            RunAutoDelayOperation delay =>
                written is SetDelayOperation or SetPolarityOperation ||
                (written is SetGainOperation && (delay.AdjustGains ?? session.AutoDelay.AdjustGains)),
            RunAutoCrossoverOperation => written is SetCrossoverOperation or SetGainOperation,
            AutoTunePeqOperation tune =>
                written is ReplacePeqBankOperation &&
                string.Equals(tune.ChannelId, written.ChannelId, StringComparison.Ordinal),
            _ => false
        };

    private static string? ChannelIdOf(AgentOperation operation) =>
        operation is AgentChannelOperation channel ? channel.ChannelId : null;

    private static void AddFinalStateNotes(List<AgentOperationVerdict> verdicts)
    {
        foreach ((AgentChannelSnapshot channel, List<string> notes) in FinalStateNotes(verdicts))
        {
            foreach (AgentOperationVerdict verdict in verdicts
                .Where(verdict => verdict.Applicable && ReferenceEquals(verdict.Channel, channel))
                .Where(verdict => verdict.Operation is SetCrossoverOperation or ReplacePeqBankOperation)
                .ToList())
            {
                verdicts[verdicts.IndexOf(verdict)] = verdict with
                {
                    Status = AgentVerdictStatus.Warning,
                    Message = verdict.Status == AgentVerdictStatus.Valid
                        ? string.Join(" ", notes)
                        : verdict.Message + " " + string.Join(" ", notes)
                };
            }
        }
    }

    /// <summary>
    /// The warnings that are about a channel as it would END UP after the given
    /// rows — today the junction-zone check — per channel. The review runs it over
    /// every applicable row; the commit runs it again over the rows the user
    /// actually ticked, because unticking one can leave a state the review never
    /// showed (a crossover moved without the bank that would have gone with it).
    /// </summary>
    public static List<(AgentChannelSnapshot Channel, List<string> Notes)> FinalStateNotes(
        IEnumerable<AgentOperationVerdict> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var result = new List<(AgentChannelSnapshot, List<string>)>();
        foreach (IGrouping<AgentChannelSnapshot, AgentOperationVerdict> group in rows
            .Where(verdict => verdict.Applicable && verdict.Operation is AgentSettingsOperation)
            .GroupBy(verdict => verdict.Channel!))
        {
            VirtualCrossoverChannelSettings final = AgentOperations.CloneEditable(group.Key.Settings);
            try
            {
                foreach (AgentOperationVerdict verdict in group)
                {
                    AgentOperations.Apply((AgentSettingsOperation)verdict.Operation!, final);
                }
            }
            catch (InvalidDataException)
            {
                continue;
            }

            List<string> notes = JunctionQNotes(final);
            if (notes.Count > 0)
            {
                result.Add((group.Key, notes));
            }
        }

        return result;
    }

    // Every bell of the bank that is too narrow for the junction zone it sits
    // in, judged on the crossover the SAME settings hold.
    private static List<string> JunctionQNotes(VirtualCrossoverChannelSettings settings)
    {
        var notes = new List<string>();
        foreach (PeqBand band in settings.PeqBands)
        {
            if (JunctionCornerNear(settings, band) is { } cornerHz)
            {
                notes.Add(
                    $"Band at {Hz(band.FrequencyHz)} (Q {band.Q.ToString("0.#", CultureInfo.InvariantCulture)}) " +
                    $"sits in the junction zone around the {Hz(cornerHz)} crossover. Fine where it " +
                    "flattens a feature of the driver itself (one its spatial average shows too): " +
                    "a minimum-phase bell straightens that phase along with the magnitude. On a " +
                    "dip the average does not show it turns the pair's phase for nothing; keep Q " +
                    $"at or below {JunctionQLimit.ToString("0.#", CultureInfo.InvariantCulture)} there.");
            }
        }

        return notes;
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
            .Where(verdict => verdict.Applicable && verdict.Operation is AgentSettingsOperation)
            .GroupBy(verdict => verdict.Channel!))
        {
            VirtualCrossoverChannelSettings copy = AgentOperations.CloneEditable(group.Key.Settings);
            try
            {
                foreach (AgentOperationVerdict verdict in group)
                {
                    AgentOperations.Apply((AgentSettingsOperation)verdict.Operation!, copy);
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
        AgentChannelSnapshot? channel = null;
        if (operation is AgentChannelOperation addressed)
        {
            channel = session.Find(addressed.ChannelId);
            if (channel == null)
            {
                return Rejected(operation, null, string.Empty, string.Empty,
                    $"Unknown channel '{addressed.ChannelId}'; the package names the channels this session has.");
            }
        }

        if (operation is not AgentSettingsOperation edit)
        {
            return JudgeEngineRequest(operation, channel, session);
        }

        VirtualCrossoverChannelSettings settings = channel!.Settings;
        string current = Describe(edit, settings);

        string? stale = CheckExpected(edit, settings);
        if (stale != null)
        {
            return Rejected(operation, channel, current, string.Empty,
                "The value changed since the package was copied: " + stale);
        }

        var notes = new List<string>();
        VirtualCrossoverChannelSettings copy = AgentOperations.CloneEditable(settings);
        string? problem = CheckValue(edit, session, copy, notes);
        if (problem != null)
        {
            return Rejected(operation, channel, current, string.Empty, problem);
        }

        string proposed = Describe(edit, copy);
        if (IsNoChange(edit, settings, copy))
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
        new(operation.Id, LabelFor(operation, channel), operation.Parameter, current, proposed,
            AgentVerdictStatus.Rejected, message, operation.Reason, operation, channel);

    // A row that is not about one channel says which channels it is about rather
    // than leaving the column blank: the engines address the whole project.
    private static string LabelFor(AgentOperation operation, AgentChannelSnapshot? channel) =>
        channel?.Label ?? (operation is AgentChannelOperation ? string.Empty : AllChannels);

    /// <summary>
    /// An engine request judged on its INPUTS. There is no current value to
    /// compare against: what the engine writes is what the run decides, and the
    /// engine's own dialog is still the gate it goes through. What the review can
    /// state is where the run would start from, what it was asked for, and what
    /// it will write over — which is what the row carries.
    /// </summary>
    private static AgentOperationVerdict JudgeEngineRequest(
        AgentOperation operation, AgentChannelSnapshot? channel, AgentSessionSnapshot session)
    {
        string current = DescribeEngineStart(operation, channel, session);
        string proposed = DescribeEngineRequest(operation, session);
        string? problem = CheckEngineRequest(operation, channel, session);
        if (problem != null)
        {
            return Rejected(operation, channel, current, proposed, problem);
        }
        if (!AgentProtocol.Executes(operation.Op))
        {
            return Rejected(
                operation, channel, current, proposed, AgentProtocol.NotAvailable(operation.Op));
        }

        (AgentVerdictStatus status, string message) = EngineNote(operation, session);
        return new AgentOperationVerdict(
            operation.Id, LabelFor(operation, channel), operation.Parameter, current, proposed,
            status, message, operation.Reason, operation, channel);
    }

    // What the engine will write over, in the row's own words. A warning rather
    // than plain OK wherever the run reaches past the channels the reply names:
    // the reader is ticking a box that hands several channels to a search.
    private static (AgentVerdictStatus Status, string Message) EngineNote(
        AgentOperation operation, AgentSessionSnapshot session) => operation switch
    {
        RunAutoDelayOperation delay => (AgentVerdictStatus.Warning,
            "Auto delay runs with these inputs and rewrites the delay and polarity of every " +
            "channel it aligns" +
            ((delay.AdjustGains ?? session.AutoDelay.AdjustGains) ? ", and their gains" : string.Empty) +
            ". It runs without its dialog; its report goes into the import's summary."),
        RunAutoCrossoverOperation => (AgentVerdictStatus.Warning,
            "The Auto crossover wizard rewrites the crossover and the gain of every enabled " +
            "channel that has a measurement, and can reorder the chain. Its own dialog " +
            "confirms the proposal."),
        AutoTunePeqOperation => (AgentVerdictStatus.Warning,
            "Auto-tune replaces this channel's whole PEQ bank (all-pass bands kept). It runs " +
            "without the EQ Wizard, on the curve the wizard would have opened on, and skips " +
            "itself when the target level sits too far from that curve."),
        _ => (AgentVerdictStatus.Valid, "OK")
    };

    // Every input is held to the field a user would type it into, and an input
    // the reply leaves out is not judged at all — it is the panel's own answer,
    // which is admissible by construction.
    private static string? CheckEngineRequest(
        AgentOperation operation, AgentChannelSnapshot? channel, AgentSessionSnapshot session)
    {
        switch (operation)
        {
            case RunAutoDelayOperation delay:
                return Bounded(delay.SceneOffsetMs, MinimumSceneOffsetMs, MaximumSceneOffsetMs,
                        SceneOffsetStepMs, "The scene offset", "ms", 2)
                    ?? Bounded(delay.NearSideCutDb, MinimumNearSideCutDb, MaximumNearSideCutDb,
                        NearSideCutStepDb, "The near-side cut", "dB", 1)
                    ?? Bounded(delay.RearFillOffsetMs, MinimumRearFillOffsetMs,
                        MaximumRearFillOffsetMs, RearFillOffsetStepMs, "The rear fill offset", "ms", 1);

            case AutoTunePeqOperation tune:
                return CheckAutoTune(tune, channel!, session);

            case UseSpatialAverageOperation spatial:
                return CheckSpatialAverage(spatial, session);

            default:
                return null;
        }
    }

    private static string? CheckAutoTune(
        AutoTunePeqOperation tune, AgentChannelSnapshot channel, AgentSessionSnapshot session)
    {
        double nyquistHz = session.ProcessorSampleRateHz / 2.0;
        if (!channel.HasMeasurement)
        {
            return $"{channel.Label} has no measurement to fit a bank against.";
        }
        // The handoff a fit is built on is the side on screen's — its gate pin,
        // its render anchor, its hybrid datum — as the PEQ menu builds it.
        if (channel.Side != AgentChannelSide.Mono &&
            (channel.Side == AgentChannelSide.Right) != session.ActiveSideRight)
        {
            return $"{channel.Label} is on the side not on screen: switch the L/R " +
                "selector, copy a new package and import again.";
        }
        if (tune.Source != null &&
            tune.Source != PointSource && tune.Source != SpatialAverageSource)
        {
            return $"Unknown auto-tune source '{tune.Source}'; " +
                $"use '{PointSource}' or '{SpatialAverageSource}'.";
        }
        if (tune.Source == SpatialAverageSource && channel.SpatialAverageCaptures.Count == 0)
        {
            return $"{channel.Label} carries no spatial average to fit against.";
        }

        string? problem =
            Bounded(tune.TargetLevelDb, MinimumTargetLevelDb, MaximumTargetLevelDb,
                TargetLevelStepDb, "The target level", "dB", 0)
            ?? Edge(tune.MinHz, nyquistHz, "lower")
            ?? Edge(tune.MaxHz, nyquistHz, "upper");
        if (problem != null)
        {
            return problem;
        }

        // The window the run will use, with the wizard's own answer for an edge
        // the reply leaves out (the channel's passband, else the field's end) —
        // judged as a whole, since a stated lower edge above the passband's
        // upper is as inverted as two stated edges the wrong way round.
        (double MinHz, double MaxHz)? passband = VirtualDspEqHandoff.PassbandFor(channel.Settings);
        double minHz = tune.MinHz ?? passband?.MinHz ?? EqAutoTuneHeadless.WindowMinHz;
        double maxHz = tune.MaxHz ?? passband?.MaxHz ?? EqAutoTuneHeadless.WindowMaxHz;
        return EqAutoTuneHeadless.IsUsableWindow(minHz, maxHz)
            ? null
            : $"The auto-tune window would be {Hz(minHz)} to {Hz(maxHz)}: its lower edge " +
                "must sit below its upper edge.";
    }

    private static string? CheckSpatialAverage(
        UseSpatialAverageOperation spatial, AgentSessionSnapshot session)
    {
        if (!spatial.Hybrid)
        {
            return "The hybrid view is what makes a spatial average count, so a request " +
                "to leave it off has nothing to do.";
        }
        if (!AgentOperations.TryParseName(
                spatial.Mode, out VirtualCrossoverSpatialAverageMode mode) ||
            mode == VirtualCrossoverSpatialAverageMode.Off)
        {
            return $"Unknown spatial average mode '{spatial.Mode}'; use " +
                $"{VirtualCrossoverSpatialAverageMode.MovingMic} or " +
                $"{VirtualCrossoverSpatialAverageMode.MicArray}.";
        }
        if (!session.HasCapture(mode))
        {
            return $"No channel in this session carries a {mode} capture.";
        }

        return session.SpatialAverageMode == mode && session.HybridTicked ? "No change." : null;
    }

    // An optional engine input against the field a user would type it into.
    private static string? Bounded(
        double? value, double minimum, double maximum, double step,
        string name, string unit, int decimals)
    {
        if (value is not { } number)
        {
            return null;
        }
        if (!double.IsFinite(number) || number < minimum || number > maximum)
        {
            return $"{name} must be between {Fixed(minimum, decimals)} and " +
                $"{Fixed(maximum, decimals)} {unit}.";
        }

        return OnStep(number, step)
            ? null
            : $"{name} must be a multiple of {Fixed(step, decimals)} {unit}.";
    }

    // The From/To fields' own range, 20 Hz to 20 kHz, and the processor's Nyquist
    // where that is lower: what the review admits is what the run uses.
    private static string? Edge(double? value, double nyquistHz, string name) =>
        value is not { } frequency ||
        (double.IsFinite(frequency) &&
            frequency >= EqAutoTuneHeadless.WindowMinHz &&
            frequency <= EqAutoTuneHeadless.WindowMaxHz &&
            frequency < nyquistHz)
            ? null
            : $"The auto-tune window's {name} edge must sit between " +
                $"{Hz(EqAutoTuneHeadless.WindowMinHz)} and {Hz(EqAutoTuneHeadless.WindowMaxHz)}" +
                (nyquistHz < EqAutoTuneHeadless.WindowMaxHz
                    ? $", below the processor's Nyquist of {Hz(nyquistHz)}."
                    : ".");

    // Where the engine would start from, in the words its own dialog uses.
    private static string DescribeEngineStart(
        AgentOperation operation, AgentChannelSnapshot? channel, AgentSessionSnapshot session) =>
        operation switch
        {
            RunAutoDelayOperation => AutoDelayText(
                session.AutoDelay.SceneOffsetMs,
                session.AutoDelay.RightHandDrive,
                session.AutoDelay.AdjustGains,
                session.AutoDelay.NearSideCutDb,
                session.AutoDelay.RearFillOffsetMs),
            RunAutoCrossoverOperation => "the corners, slopes and gains as they stand",
            AutoTunePeqOperation => channel == null ? string.Empty : BankText(channel.Settings),
            UseSpatialAverageOperation => SpatialAverageText(
                session.SpatialAverageMode.ToString(), session.HybridTicked),
            _ => string.Empty
        };

    private static string DescribeEngineRequest(
        AgentOperation operation, AgentSessionSnapshot session) => operation switch
    {
        RunAutoDelayOperation delay => AutoDelayText(
            delay.SceneOffsetMs ?? session.AutoDelay.SceneOffsetMs,
            delay.RightHandDrive ?? session.AutoDelay.RightHandDrive,
            delay.AdjustGains ?? session.AutoDelay.AdjustGains,
            delay.NearSideCutDb ?? session.AutoDelay.NearSideCutDb,
            delay.RearFillOffsetMs ?? session.AutoDelay.RearFillOffsetMs),
        RunAutoCrossoverOperation => "the wizard's corners, slopes and cut-only gains",
        AutoTunePeqOperation tune => AutoTuneText(tune),
        UseSpatialAverageOperation spatial => SpatialAverageText(spatial.Mode, spatial.Hybrid),
        _ => string.Empty
    };

    // The near-side cut only where the gain balance is on: with it off the field
    // is an input to nothing, and printing it would read as a change.
    private static string AutoDelayText(
        double sceneOffsetMs, bool rightHandDrive, bool adjustGains,
        double nearSideCutDb, double rearFillOffsetMs) =>
        $"scene {Ms(sceneOffsetMs)} {(rightHandDrive ? "RHD" : "LHD")}, " +
        $"gains {(adjustGains ? "on" : "off")}" +
        (adjustGains ? $", near-side cut {Db(nearSideCutDb)}" : string.Empty) +
        $", rear fill {Ms(rearFillOffsetMs)}";

    // Only what the reply actually states: an input it leaves out is the wizard's
    // own answer, and printing that would read as a choice the reply made.
    private static string AutoTuneText(AutoTunePeqOperation tune)
    {
        var parts = new List<string>();
        if (tune.MinHz != null || tune.MaxHz != null)
        {
            parts.Add(
                (tune.MinHz is { } low ? Hz(low) : "the wizard's low edge") + " to " +
                (tune.MaxHz is { } high ? Hz(high) : "the wizard's high edge"));
        }
        if (tune.TargetLevelDb is { } level)
        {
            parts.Add("target " + Db(level));
        }
        if (tune.AllowShelves is { } shelves)
        {
            parts.Add(shelves ? "shelves allowed" : "no shelves");
        }
        if (tune.CutsOnly is { } cutsOnly)
        {
            parts.Add(cutsOnly ? "cuts only" : "cuts and boosts");
        }
        if (tune.Source is { } source)
        {
            parts.Add("from the " +
                (source == SpatialAverageSource ? "spatial average" : "point measurement"));
        }

        return parts.Count == 0
            ? "auto-tune with the wizard's own settings"
            : "auto-tune " + string.Join(", ", parts);
    }

    private static string SpatialAverageText(string mode, bool hybrid) =>
        $"{mode}, hybrid {(hybrid ? "on" : "off")}";

    // Exact comparison: the package prints every value in round-trip form, so an
    // assistant that copied it hands back the same double. A tolerance here would
    // be a tolerance for a reply that was reasoned about a different value.
    private static string? CheckExpected(
        AgentSettingsOperation operation, VirtualCrossoverChannelSettings settings)
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
        AgentSettingsOperation operation,
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
        AgentSettingsOperation operation,
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

    private static string Describe(
        AgentSettingsOperation operation, VirtualCrossoverChannelSettings settings) =>
        operation switch
        {
            SetGainOperation => Db(settings.GainDb),
            SetDelayOperation => Ms(settings.DelayMs),
            SetPolarityOperation => Polarity(settings.InvertPolarity),
            SetCrossoverOperation => Crossover(settings),
            _ => BankText(settings)
        };

    private static string BankText(VirtualCrossoverChannelSettings settings) =>
        $"{settings.PeqBands.Count} band{(settings.PeqBands.Count == 1 ? "" : "s")}, " +
        $"preamp {Db(settings.PeqPreampDb)}";

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

    private static string Fixed(double value, int decimals) =>
        (value + 0).ToString(
            "F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

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
    public static void Apply(AgentSettingsOperation operation, VirtualCrossoverChannelSettings target)
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
    public static bool TryParseName<TEnum>(string? name, out TEnum value) where TEnum : struct, Enum
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
