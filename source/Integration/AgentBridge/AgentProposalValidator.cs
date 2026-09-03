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
    /// <summary>
    /// Whether the review offers the row ticked. An applicable row is, unless the
    /// session can no longer vouch for the package it was written against: then
    /// it is offered unticked, for the user to tick knowingly.
    /// </summary>
    public bool Ticked { get; init; } = true;

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
        string? stale = StaleSessionReason(proposal, session);
        if (stale != null)
        {
            warnings.Add(stale);
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

        RejectEngineRequestsOnAStaleSession(verdicts, stale);
        RejectRepeatedEngineRequests(verdicts);
        RejectProbesOverTheVariantBudget(verdicts);
        RejectDisagreeingTargetLevels(verdicts);
        RejectJunctionTunesUnderTheWizard(verdicts);
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
        // After every note: the stale mark is the last word on a row, and a note
        // added after it would read as if it were part of the same sentence.
        MarkSettingsRowsOnAStaleSession(verdicts, stale);
        return new AgentProposalReview(proposal, verdicts, warnings);
    }

    // Whether the session can vouch for the package the reply answers, and if
    // not, why: the reply names none while asking for an engine, no package was
    // copied since the session opened (or the reply's came from elsewhere), it
    // is another package than the one last copied, or the session changed since
    // that copy — its fingerprint (AgentSessionFingerprint) no longer matches,
    // whichever way it changed. A reply of settings rows alone that names no
    // package is taken at its word: each row carries its own expected current
    // value. Under any of the four the settings rows stay, judged on those
    // values but offered unticked (MarkSettingsRowsOnAStaleSession), and the
    // engine requests are refused, since an engine reads the session as it is
    // now, which the assistant has not seen.
    private static string? StaleSessionReason(AgentProposal proposal, AgentSessionSnapshot session)
    {
        const string Decide =
            " The settings rows are judged on the current values below and left " +
            "unticked; the engine requests are refused, since they would run on a " +
            "session the assistant has not seen — copy a new package and ask again.";
        if (proposal.PackageId == null)
        {
            return proposal.Operations.Any(operation => operation is not AgentSettingsOperation)
                ? "The reply names no package, so nothing says which session its engine " +
                    "requests were written for." + Decide
                : null;
        }

        if (session.LastPackageId == null)
        {
            return "The reply names a package this session has not copied since it opened, " +
                "or one that came from elsewhere." + Decide;
        }
        if (!string.Equals(proposal.PackageId, session.LastPackageId, StringComparison.OrdinalIgnoreCase))
        {
            return "The reply answers a different package than the one last copied from this " +
                "session." + Decide;
        }
        if (session.LastPackageFingerprint != null && session.Fingerprint != null &&
            !string.Equals(session.LastPackageFingerprint, session.Fingerprint, StringComparison.Ordinal))
        {
            return "The session has changed since that package was copied — a measurement or " +
                "capture replaced, a block added, removed or reordered, a chain, gate or datum " +
                "moved, or an import undone." + Decide;
        }

        return null;
    }

    private static void RejectEngineRequestsOnAStaleSession(
        List<AgentOperationVerdict> verdicts, string? staleReason)
    {
        if (staleReason == null)
        {
            return;
        }

        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            // A probe stays: it writes nothing, and what it reads is the session
            // as it is now — which is exactly what a reader of a stale package
            // needs. Its result says whether the session still matches.
            if (verdict.Applicable &&
                verdict.Operation is not AgentSettingsOperation and not ProbeOperation)
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = "The session is not the one the package described; " +
                        "copy a new package and ask again."
                };
            }
        }
    }

    // A settings row from a package the session cannot vouch for is judged on
    // its expected current value as always, and that value can still match after
    // the MEASUREMENT the row was reasoned from has been replaced. The row stays
    // — the user may know the change does not bear on it — but is offered
    // unticked and marked, rather than ticked by default among rows that are.
    private static void MarkSettingsRowsOnAStaleSession(
        List<AgentOperationVerdict> verdicts, string? staleReason)
    {
        if (staleReason == null)
        {
            return;
        }

        const string Note =
            "Written against a package this session cannot vouch for (see the warning " +
            "above); tick it only if what changed does not bear on this row.";
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (verdict.Applicable && verdict.Operation is AgentSettingsOperation)
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Warning,
                    Message = verdict.Status == AgentVerdictStatus.Warning
                        ? verdict.Message + " " + Note
                        : Note,
                    Ticked = false
                };
            }
        }
    }

    // The panel runs each engine once per import, and a second request for the
    // same one carries a second set of inputs. The first is kept rather than the
    // set silently deciding which won — the reply listed them in an order.
    // Probes are exempt: a probe writes nothing, so a second one on the same
    // junction is another QUESTION about it, not a second run of the same
    // engine. What bounds them is the variant budget below.
    private static void RejectRepeatedEngineRequests(List<AgentOperationVerdict> verdicts)
    {
        var first = new Dictionary<(string Op, string? ChannelId), string>();
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (!verdict.Applicable ||
                verdict.Operation is null or AgentSettingsOperation or ProbeOperation)
            {
                continue;
            }

            (string, string?) key = (verdict.Operation.Op, ScopeOf(verdict.Operation));
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

    /// <summary>
    /// One import reads at most <see cref="AgentProtocol.MaxProbeVariantsPerImport"/>
    /// variants, however the reply splits them between probes. The bound is the
    /// user's: the readings run while they wait, with no progress bar and
    /// nothing to cancel, and the answer is a text they have to paste — about
    /// 65 ms and 0.8 KB per variant on a reference session, so the budget is a
    /// second or two and a text the size of a package. A reply that wants more
    /// than this searched is asking for the junction tune, which searches a
    /// window properly and reports the few candidates that matter.
    /// </summary>
    private static void RejectProbesOverTheVariantBudget(List<AgentOperationVerdict> verdicts)
    {
        int spent = 0;
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (!verdict.Applicable ||
                verdict.Operation is not ProbeOperation { Variants: { } variants })
            {
                continue;
            }

            if (spent + variants.Count > AgentProtocol.MaxProbeVariantsPerImport)
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = spent == 0
                        ? $"An import reads at most {AgentProtocol.MaxProbeVariantsPerImport} " +
                            $"probe variants; this one asks for {variants.Count}. Ask for the " +
                            "junction tune to search a window instead."
                        : $"An import reads at most {AgentProtocol.MaxProbeVariantsPerImport} " +
                            $"probe variants and {spent} are already asked for above; this row " +
                            $"asks for {variants.Count} more."
                };
                continue;
            }

            spent += variants.Count;
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

    // The crossover wizard rewrites every junction of the chain, so a junction
    // tune beside it would tune a crossover the wizard is about to replace (the
    // wizard runs first). The wizard keeps its row, as the engine that reaches
    // further; the junction tune is refused naming it.
    private static void RejectJunctionTunesUnderTheWizard(List<AgentOperationVerdict> verdicts)
    {
        AgentOperationVerdict? wizard = verdicts.FirstOrDefault(verdict =>
            verdict.Applicable && verdict.Operation is RunAutoCrossoverOperation);
        if (wizard == null)
        {
            return;
        }

        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (verdict.Applicable && verdict.Operation is TuneJunctionOperation)
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = $"Would be overwritten by {wizard.Parameter} ({wizard.Id}), which " +
                        "rewrites every junction of the chain."
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
            // The tune writes one crossover to both sides of the two blocks it
            // names, so a hand-written crossover on either block, either side,
            // is what it erases.
            TuneJunctionOperation junction =>
                written is SetCrossoverOperation &&
                AgentJunctionIds.TryParse(junction.JunctionId, out _, out string lower, out string upper) &&
                session.Find(written.ChannelId) is { } channel &&
                (string.Equals(channel.Block, lower, StringComparison.Ordinal) ||
                    string.Equals(channel.Block, upper, StringComparison.Ordinal)),
            AutoTunePeqOperation tune =>
                written is ReplacePeqBankOperation &&
                string.Equals(tune.ChannelId, written.ChannelId, StringComparison.Ordinal),
            _ => false
        };

    // What an engine request is scoped to, for the once-per-import rule: a
    // channel for the per-channel engines, a junction for the junction tune and
    // for a probe (which also counts per reading, since one junction can be
    // asked two different questions), nothing for the whole-project ones.
    private static string? ScopeOf(AgentOperation operation) => operation switch
    {
        AgentChannelOperation channel => channel.ChannelId,
        TuneJunctionOperation junction => junction.JunctionId,
        ProbeOperation probe => $"{probe.Probe}:{probe.JunctionId}",
        _ => null
    };

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
                    $"sits in the junction zone around the {Hz(cornerHz)} crossover. A bell on a " +
                    "feature the spatial average shows too corrects a stable feature and its " +
                    "minimum-phase turn, whatever excess dispersion the channel also carries there " +
                    "(that part stays for timing and all-pass); whether it helps the pair is read " +
                    "off the junction phase before and after. On a dip the average does not show " +
                    $"it turns the pair's phase for nothing; keep Q at or below {JunctionQLimit.ToString("0.#", CultureInfo.InvariantCulture)} there.");
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
        channel?.Label ?? operation switch
        {
            AgentChannelOperation => string.Empty,
            // The two blocks, both sides: one crossover is written to both.
            TuneJunctionOperation junction =>
                AgentJunctionIds.TryParse(junction.JunctionId, out _, out string lower, out string upper)
                    ? $"{lower}/{upper}"
                    : junction.JunctionId,
            ProbeOperation { JunctionId: { } id } =>
                AgentJunctionIds.TryParse(id, out _, out string lower, out string upper)
                    ? $"{lower}/{upper}"
                    : id,
            _ => AllChannels
        };

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
        // The one row that is not a warning: a probe writes nothing at all.
        ProbeOperation probe => (AgentVerdictStatus.Valid,
            "Reads only — nothing in the tune is changed, and there is nothing to undo. " +
            "The reading is computed on the tune as it stands" +
            (probe.Probe == AgentProtocol.JunctionProbe
                ? " (each variant is measured on a copy of the settings; the tune keeps its own)"
                : string.Empty) +
            ", copied to the clipboard, and the summary asks you to paste it into the same chat."),
        TuneJunctionOperation => (AgentVerdictStatus.Warning,
            "The junction tune rewrites the lower block's low-pass and the upper block's " +
            "high-pass on both sides — corner, family and slopes — scored on the pair's sum " +
            "at the current delays; gains, delays, polarity, PEQ and every other junction " +
            "stay. It runs without a dialog and keeps the current crossover unless a " +
            "candidate clearly beats it; its report goes into the import's summary."),
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

            case TuneJunctionOperation junction:
                return CheckTuneJunction(junction, session);

            case ProbeOperation probe:
                return CheckProbe(probe, session);

            case UseSpatialAverageOperation spatial:
                return CheckSpatialAverage(spatial, session);

            default:
                return null;
        }
    }

    /// <summary>
    /// The two channels a junction id names on the session, or why it names
    /// none: the id must be the package's own shape, both blocks must be on
    /// that side with a measurement, in the sum and not bypassed, in one group,
    /// and neighbours along the spectrum that actually hand over to each other —
    /// the same adjacency the panel's junction read-outs use.
    /// </summary>
    public static string? ResolveJunction(
        AgentSessionSnapshot session, string junctionId,
        out AgentChannelSnapshot? lower, out AgentChannelSnapshot? upper)
    {
        ArgumentNullException.ThrowIfNull(session);
        lower = null;
        upper = null;
        if (!AgentJunctionIds.TryParse(junctionId, out AgentChannelSide side, out string lowerBlock, out string upperBlock))
        {
            return $"'{junctionId}' is not a junction id; the package names its junctions as " +
                "side:lower-upper, for example left:B-C.";
        }

        AgentChannelSnapshot? lowerFound = session.Channels.FirstOrDefault(channel =>
            channel.PlaysOn(side) && string.Equals(channel.Block, lowerBlock, StringComparison.Ordinal));
        AgentChannelSnapshot? upperFound = session.Channels.FirstOrDefault(channel =>
            channel.PlaysOn(side) && string.Equals(channel.Block, upperBlock, StringComparison.Ordinal));
        if (lowerFound == null || upperFound == null)
        {
            return $"Unknown junction '{junctionId}'; the package names the junctions this session has.";
        }
        foreach (AgentChannelSnapshot channel in new[] { lowerFound, upperFound })
        {
            if (!channel.HasMeasurement)
            {
                return $"{channel.Label} has no measurement, so the junction cannot be read.";
            }
            if (!channel.Enabled || channel.Bypass)
            {
                return $"{channel.Label} is {(channel.Enabled ? "bypassed" : "disabled")}, so the " +
                    "junction is not in the sum.";
            }
        }
        if (VirtualCrossoverAlignmentStages.StageOf(lowerFound.Zone) !=
            VirtualCrossoverAlignmentStages.StageOf(upperFound.Zone))
        {
            return $"{lowerFound.Label} and {upperFound.Label} are in different groups; no " +
                "crossover hands over between them.";
        }

        // Neighbours along the spectrum among the side's summed channels of that
        // group, and a real handover: both play inside the octave-each-way band
        // around the pair's corner.
        VirtualCrossoverAlignmentStage stage = VirtualCrossoverAlignmentStages.StageOf(lowerFound.Zone);
        List<AgentChannelSnapshot> byBand = session.Channels
            .Where(channel => channel.PlaysOn(side) && channel.HasMeasurement &&
                channel.Enabled && !channel.Bypass &&
                VirtualCrossoverAlignmentStages.StageOf(channel.Zone) == stage)
            .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))
            .ToList();
        int lowerIndex = byBand.IndexOf(lowerFound);
        int upperIndex = byBand.IndexOf(upperFound);
        if (upperIndex != lowerIndex + 1)
        {
            return upperIndex < lowerIndex
                ? $"{lowerFound.Label} plays above {upperFound.Label}; name the junction lower block first."
                : $"{lowerFound.Label} and {upperFound.Label} are not neighbours along the spectrum.";
        }

        double pairHz = VirtualCrossoverJunctions.GetPairCrossoverHz(lowerFound.Settings, upperFound.Settings);
        (double bandLowHz, double bandHighHz) = VirtualCrossoverJunctions.OverlapBand(pairHz);
        if (!PlaysWithin(lowerFound.Settings, bandLowHz, bandHighHz) ||
            !PlaysWithin(upperFound.Settings, bandLowHz, bandHighHz))
        {
            return $"{lowerFound.Label} and {upperFound.Label} do not hand over to each other: " +
                "one of them does not play within an octave of the pair's corner.";
        }

        lower = lowerFound;
        upper = upperFound;
        return null;
    }

    private static bool PlaysWithin(VirtualCrossoverChannelSettings settings, double lowHz, double highHz)
    {
        (double channelLow, double channelHigh) = VirtualCrossoverJunctions.GetChannelBand(settings);
        return channelHigh > lowHz && channelLow < highHz;
    }

    /// <summary>
    /// The corner window a junction tune searches when the reply states none:
    /// half an octave each way, snapped to the wizard's lattice.
    /// </summary>
    public static (double MinHz, double MaxHz) DefaultJunctionWindow(double currentHz) =>
        (Math.Max(EqAutoTuneHeadless.WindowMinHz, CrossoverAutoSetup.RoundToLattice(currentHz / Math.Sqrt(2))),
            Math.Min(EqAutoTuneHeadless.WindowMaxHz, CrossoverAutoSetup.RoundToLattice(currentHz * Math.Sqrt(2))));

    // A probe is held to what it can be computed from, and to nothing else: it
    // writes nothing, so there is no value to protect — only a question that
    // must be answerable. The settings a variant states are held to the very
    // limits a settings operation is, since a variant that reads well is meant
    // to become one.
    private static string? CheckProbe(ProbeOperation probe, AgentSessionSnapshot session)
    {
        if (!AgentProtocol.Reads(probe.Probe))
        {
            return AgentProtocol.ProbeNotAvailable(probe.Probe);
        }
        if (probe.Probe == AgentProtocol.ExcessGroupDelayProbe)
        {
            return session.Channels.Any(channel => channel.HasMeasurement)
                ? null
                : "No channel in this session has a measurement to read.";
        }

        string? problem = ResolveJunction(
            session, probe.JunctionId ?? string.Empty,
            out AgentChannelSnapshot? lower, out AgentChannelSnapshot? upper);
        if (problem != null)
        {
            return problem;
        }
        if (probe.Probe == AgentProtocol.JunctionDelayProbe)
        {
            return lower!.Settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass ||
                upper!.Settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
                ? null
                : $"{lower.Label} and {upper!.Label} have no crossover between them, so there is " +
                    "no junction band to search a delay in.";
        }

        IReadOnlyList<AgentProbeVariant> variants = probe.Variants ?? [];
        if (variants.Count == 0)
        {
            return "A junction probe names no variant to read the junction under; the junction " +
                "as it stands is read beside them and is not one of them.";
        }

        foreach (AgentProbeVariant variant in variants)
        {
            if (variant.Changes.Count == 0)
            {
                return "A probe variant changes nothing, so it would read the same as the " +
                    "junction as it stands.";
            }
            if (variant.Changes.Count > AgentProtocol.MaxProbeChanges)
            {
                return $"A probe variant changes at most {AgentProtocol.MaxProbeChanges} channels " +
                    "— the two the junction is made of.";
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AgentProbeChange change in variant.Changes)
            {
                AgentChannelSnapshot? channel =
                    string.Equals(change.ChannelId, lower!.Id, StringComparison.Ordinal) ? lower
                    : string.Equals(change.ChannelId, upper!.Id, StringComparison.Ordinal) ? upper
                    : null;
                if (channel == null)
                {
                    return $"'{change.ChannelId}' is not one of the junction's two channels " +
                        $"({lower.Id} and {upper!.Id}); a probe reads the junction it names.";
                }
                if (!seen.Add(change.ChannelId))
                {
                    return $"A probe variant states {change.ChannelId} twice.";
                }
                if (change.StatesNothing)
                {
                    return $"A probe variant's change for {change.ChannelId} states no setting.";
                }

                problem = ApplyProbeChange(
                    change, session, AgentOperations.CloneEditable(channel.Settings));
                if (problem != null)
                {
                    return problem;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Applies one probe variant's change INTO <paramref name="copy"/> — which
    /// must already be a copy of the channel's settings — holding every stated
    /// value to the same limit a settings operation is held to: the probe reads
    /// what a proposal could write, and nothing else. Returns the first problem,
    /// or null with the copy carrying the variant.
    /// </summary>
    public static string? ApplyProbeChange(
        AgentProbeChange change,
        AgentSessionSnapshot session,
        VirtualCrossoverChannelSettings copy)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(copy);

        var notes = new List<string>();
        foreach (AgentSettingsOperation operation in ProbeOperationsOf(change, copy))
        {
            string? problem = CheckValue(operation, session, copy, notes);
            if (problem != null)
            {
                return $"{change.ChannelId}: {problem}";
            }
        }

        return null;
    }

    // A change as the settings operations that would write it. The expected
    // values are the copy's own — a probe writes nothing, so there is no tune to
    // have moved under it, and the expected-value guard has nothing to guard.
    private static IEnumerable<AgentSettingsOperation> ProbeOperationsOf(
        AgentProbeChange change, VirtualCrossoverChannelSettings copy)
    {
        if (change.GainDb is { } gainDb)
        {
            yield return new SetGainOperation("probe", change.ChannelId, string.Empty, copy.GainDb, gainDb);
        }
        if (change.DelayMs is { } delayMs)
        {
            yield return new SetDelayOperation("probe", change.ChannelId, string.Empty, copy.DelayMs, delayMs);
        }
        if (change.InvertPolarity is { } inverted)
        {
            yield return new SetPolarityOperation(
                "probe", change.ChannelId, string.Empty, copy.InvertPolarity, inverted);
        }
        if (change.Crossover is { } crossover)
        {
            yield return new SetCrossoverOperation(
                "probe", change.ChannelId, string.Empty, crossover, crossover);
        }
        if (change.Peq is { } peq)
        {
            yield return new ReplacePeqBankOperation(
                "probe", change.ChannelId, string.Empty, string.Empty, peq);
        }
    }

    private static string? CheckTuneJunction(TuneJunctionOperation junction, AgentSessionSnapshot session)
    {
        string? problem = ResolveJunction(
            session, junction.JunctionId, out AgentChannelSnapshot? lower, out AgentChannelSnapshot? upper);
        if (problem != null)
        {
            return problem;
        }

        double nyquistHz = session.ProcessorSampleRateHz / 2.0;
        problem = Edge(junction.MinHz, nyquistHz, "lower", "junction") ??
            Edge(junction.MaxHz, nyquistHz, "upper", "junction");
        if (problem != null)
        {
            return problem;
        }

        double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(lower!.Settings, upper!.Settings);
        (double defaultMin, double defaultMax) = DefaultJunctionWindow(currentHz);
        double minHz = junction.MinHz ?? defaultMin;
        double maxHz = junction.MaxHz ?? defaultMax;
        if (!(maxHz >= minHz))
        {
            return $"The junction's corner window would be {Hz(minHz)} to {Hz(maxHz)}: its lower " +
                "edge must not sit above its upper edge.";
        }

        var families = new List<CrossoverFilterFamily>();
        if (junction.Families != null)
        {
            foreach (string name in junction.Families)
            {
                if (!AgentOperations.TryParseName(name, out CrossoverFilterFamily family))
                {
                    return $"Unknown crossover family '{name}'; the package's limits.slopes names the families.";
                }
                families.Add(family);
            }
            if (families.Count == 0)
            {
                return "The junction's family list is empty.";
            }
        }
        else
        {
            families.AddRange(CurrentFamilies(lower.Settings, upper.Settings));
        }

        if (junction.Slopes != null)
        {
            if (junction.Slopes.Count == 0)
            {
                return "The junction's slope list is empty.";
            }
            var offered = families.SelectMany(CrossoverFilter.SupportedSlopes).ToHashSet();
            foreach (int slope in junction.Slopes)
            {
                if (!offered.Contains(slope))
                {
                    return $"No admitted family offers a {slope} dB/oct slope; the package's " +
                        "limits.slopes lists the slopes per family.";
                }
            }
        }

        return null;
    }

    // The families the junction's facing edges use today, the low-pass first;
    // Linkwitz-Riley where neither edge exists yet.
    public static IReadOnlyList<CrossoverFilterFamily> CurrentFamilies(
        VirtualCrossoverChannelSettings lower, VirtualCrossoverChannelSettings upper)
    {
        var families = new List<CrossoverFilterFamily>();
        if (lower.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass)
        {
            families.Add(lower.LowPassEdge.Family);
        }
        if (upper.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass &&
            !families.Contains(upper.HighPassEdge.Family))
        {
            families.Add(upper.HighPassEdge.Family);
        }
        if (families.Count == 0)
        {
            families.Add(CrossoverFilterFamily.LinkwitzRiley);
        }

        return families;
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
    private static string? Edge(double? value, double nyquistHz, string name, string window = "auto-tune") =>
        value is not { } frequency ||
        (double.IsFinite(frequency) &&
            frequency >= EqAutoTuneHeadless.WindowMinHz &&
            frequency <= EqAutoTuneHeadless.WindowMaxHz &&
            frequency < nyquistHz)
            ? null
            : $"The {window} window's {name} edge must sit between " +
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
            TuneJunctionOperation junction => JunctionStartText(junction.JunctionId, session),
            // A probe changes nothing, so there is no "before" to set against an
            // "after": the column says what it will read, once.
            ProbeOperation probe => probe.JunctionId is { } id
                ? JunctionStartText(id, session)
                : "every measured channel",
            UseSpatialAverageOperation => SpatialAverageText(
                session.SpatialAverageMode.ToString(), session.HybridTicked),
            _ => string.Empty
        };

    // The junction's two facing edges as they stand — what a tune may replace,
    // and what a probe reads beside its variants.
    private static string JunctionStartText(string junctionId, AgentSessionSnapshot session)
    {
        if (ResolveJunction(session, junctionId, out AgentChannelSnapshot? lower, out AgentChannelSnapshot? upper) != null)
        {
            return string.Empty;
        }

        string lowPass = lower!.Settings.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
            ? "LP " + Edge(lower.Settings.LowPassEdge)
            : "no low-pass";
        string highPass = upper!.Settings.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
            ? "HP " + Edge(upper.Settings.HighPassEdge)
            : "no high-pass";
        return $"{lower.Block}: {lowPass}; {upper.Block}: {highPass}";
    }

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
        TuneJunctionOperation junction => TuneJunctionText(junction),
        ProbeOperation probe => ProbeText(probe),
        UseSpatialAverageOperation spatial => SpatialAverageText(spatial.Mode, spatial.Hybrid),
        _ => string.Empty
    };

    // What the probe will read, in one phrase — the row's Proposed column, which
    // for a probe is a question rather than a value.
    private static string ProbeText(ProbeOperation probe) => probe.Probe switch
    {
        AgentProtocol.JunctionProbe =>
            $"read this junction under {Count(probe.Variants?.Count ?? 0, "variant")} " +
            $"({ProbeVariantText(probe)}) beside the settings it has now",
        AgentProtocol.JunctionDelayProbe =>
            "read what a delay search would find at this junction",
        AgentProtocol.ExcessGroupDelayProbe =>
            "read every measured channel's excess group delay",
        _ => $"read '{probe.Probe}'"
    };

    // What the variants actually touch, so the row says what is being asked
    // about rather than only how many questions there are.
    private static string ProbeVariantText(ProbeOperation probe)
    {
        var parts = new List<string>();
        IEnumerable<AgentProbeChange> changes =
            (probe.Variants ?? []).SelectMany(variant => variant.Changes);
        foreach (AgentProbeChange change in changes)
        {
            Add(change.Crossover != null, "crossover");
            Add(change.Peq != null, "PEQ");
            Add(change.GainDb != null, "gain");
            Add(change.DelayMs != null, "delay");
            Add(change.InvertPolarity != null, "polarity");
        }

        return parts.Count == 0 ? "nothing stated" : string.Join(", ", parts);

        void Add(bool present, string name)
        {
            if (present && !parts.Contains(name))
            {
                parts.Add(name);
            }
        }
    }

    private static string Count(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    // Only what the reply states; an input it leaves out is the tuner's own.
    private static string TuneJunctionText(TuneJunctionOperation junction)
    {
        var parts = new List<string>();
        if (junction.MinHz != null || junction.MaxHz != null)
        {
            parts.Add(
                (junction.MinHz is { } low ? Hz(low) : "half an octave below") + " to " +
                (junction.MaxHz is { } high ? Hz(high) : "half an octave above"));
        }
        if (junction.Families is { Count: > 0 } families)
        {
            parts.Add(string.Join("/", families));
        }
        if (junction.Slopes is { Count: > 0 } slopes)
        {
            parts.Add(string.Join("/", slopes.Select(slope => slope.ToString(CultureInfo.InvariantCulture))) + " dB/oct");
        }
        if (junction.IndependentSlopes is { } independent)
        {
            parts.Add(independent ? "slopes free per edge" : "one slope for both edges");
        }

        return parts.Count == 0
            ? "tune the junction with the tuner's own settings"
            : "tune the junction: " + string.Join(", ", parts);
    }

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
