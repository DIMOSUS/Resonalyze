using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

internal enum AgentVerdictStatus
{
    Valid,
    Warning,
    Rejected
}

/// <summary>One review row. A rejected row keeps its current/proposed text so the user sees what was refused; it can never be ticked.</summary>
internal sealed record AgentOperationVerdict(
    string Id,
    string ChannelLabel,
    string Parameter,
    string Current,
    string Proposed,
    AgentVerdictStatus Status,
    string Message,
    string? Reason,
    AgentOperation? Operation,
    AgentChannelSnapshot? Channel)
{
    /// <summary>Offered unticked when the session can no longer vouch for the package the reply answers.</summary>
    public bool Ticked { get; init; } = true;

    public bool Applicable =>
        Status != AgentVerdictStatus.Rejected && Operation != null &&
        (Operation is not AgentChannelOperation || Channel != null);
}

internal sealed record AgentProposalReview(
    AgentProposal Proposal,
    IReadOnlyList<AgentOperationVerdict> Verdicts,
    IReadOnlyList<string> Warnings)
{
    public bool HasApplicable => Verdicts.Any(verdict => verdict.Applicable);
}

/// <summary>Admissibility only: a valid proposal can still be a worse tune. Trial edits run on copies through the loader's <see cref="VirtualCrossoverChannelSettings.Validate"/>. See docs/tech/agent-bridge.md#review-rules.</summary>
internal static class AgentProposalValidator
{
    public const string PointSource = "point";
    public const string SpatialAverageSource = "spatialAverage";

    public const string BoostsOff = "off";
    public const string BoostsRefillOwnCuts = "refillOwnCuts";
    public const string BoostsAllowed = "allowed";

    public const string AllChannels = "all";

    public const string DeviceLimitsUnknown =
        "Device limits unknown; only Virtual DSP limits were checked.";

    // Below this a net rise is bilinear warping and rounding, not a boost.
    private const double HeadroomToleranceDb = 0.05;

    /// <summary>Highest bell Q passed silently within an octave of one of the channel's active corners; narrower bells turn the phase where the pair sums. See docs/tech/agent-bridge.md#junction-zone-q.</summary>
    public const double JunctionQLimit = 2;

    // Bells only: shelves are wide by nature, and an all-pass at a junction is there for the phase.
    private static double? JunctionCornerNear(VirtualCrossoverChannelSettings settings, PeqBand band)
    {
        if (band.Type != PeqBandType.Peaking || band.Q <= JunctionQLimit)
        {
            return null;
        }

        // FIR crossover corners count where the IIR crossover is off.
        foreach (double cornerHz in new[]
        {
            settings.EffectiveHighPassHz ?? double.NaN,
            settings.EffectiveLowPassHz ?? double.NaN
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

        // Missing prose is reported, not refused.
        if (proposal.Summary == null)
        {
            warnings.Add("The reply gave no summary of what it is proposing.");
        }
        int unexplained = proposal.Operations.Count(operation => operation.Reason == null);
        if (unexplained > 0)
        {
            warnings.Add(unexplained == proposal.Operations.Count
                ? $"No operation says why: judge the {unexplained} row" +
                    $"{(unexplained == 1 ? "" : "s")} below by the values alone."
                : $"{unexplained} of {proposal.Operations.Count} operations do not say why; " +
                    "they are marked in the reason column.");
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

        // Two applicable edits of one parameter cannot both be meant; neither wins.
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
        RejectSeriesProbesBeyondOne(verdicts);
        RejectDisagreeingTargetLevels(verdicts);
        RejectJunctionTunesUnderTheWizard(verdicts);
        RejectOverwrittenSettings(verdicts, session);

        // Notes about the channel's final state; last, so they never read rows that were just refused.
        AddFinalStateNotes(verdicts);
        // The stale mark is the last word on a row.
        MarkSettingsRowsOnAStaleSession(verdicts, stale);
        return new AgentProposalReview(proposal, verdicts, warnings);
    }

    // Why the session cannot vouch for the reply's package, or null. See docs/tech/agent-bridge.md#stale-session.
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
            // A probe stays: it writes nothing and reads the session as it is now.
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

    // An expected value can still match after the measurement it was reasoned from was replaced: kept, but unticked and marked.
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

    // Each engine runs once per scope (channel, junction or project) per import: the first request is kept. Probes are exempt (bounded by the variant budget).
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

    /// <summary>Caps variants per import at <see cref="AgentProtocol.MaxProbeVariantsPerImport"/> (about 65 ms and 0.8 KB each). See docs/tech/agent-bridge.md#probe-budgets.</summary>
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

    /// <summary>One series probe per import (<see cref="AgentProtocol.MaxSeriesProbesPerImport"/>): each is a full gather that the variant budget cannot see.</summary>
    private static void RejectSeriesProbesBeyondOne(List<AgentOperationVerdict> verdicts)
    {
        int seen = 0;
        string? owner = null;
        for (int index = 0; index < verdicts.Count; index++)
        {
            AgentOperationVerdict verdict = verdicts[index];
            if (!verdict.Applicable ||
                verdict.Operation is not ProbeOperation { Probe: AgentProtocol.SeriesProbe })
            {
                continue;
            }

            if (seen >= AgentProtocol.MaxSeriesProbesPerImport)
            {
                verdicts[index] = verdict with
                {
                    Status = AgentVerdictStatus.Rejected,
                    Message = $"An import reads at most {AgentProtocol.MaxSeriesProbesPerImport} series " +
                        $"probe and {owner} already asks for one; put every series, channel and " +
                        "junction the question needs into that one."
                };
                continue;
            }

            seen++;
            owner = verdict.Id;
        }
    }

    // The target level is one project datum: only the first stated level stands.
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

    // The wizard rewrites every junction and runs first, so a junction tune beside it is refused.
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

    // An engine and a hand-written value it overwrites cannot both be meant: the engine wins. Only a runnable engine erases anything.
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

    /// <summary>Whether running <paramref name="engine"/> overwrites <paramref name="written"/>, per what the panel's buttons write.</summary>
    public static bool Overwrites(
        AgentOperation engine, AgentSettingsOperation written, AgentSessionSnapshot session) =>
        engine switch
        {
            RunAutoDelayOperation delay =>
                written is SetDelayOperation or SetPolarityOperation ||
                (written is SetGainOperation && (delay.AdjustGains ?? session.AutoDelay.AdjustGains)),
            RunAutoCrossoverOperation => written is SetCrossoverOperation or SetGainOperation,
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

    // Scope for the once-per-import rule: channel, junction, or none for whole-project engines. Probes never reach the rule.
    private static string? ScopeOf(AgentOperation operation) => operation switch
    {
        AgentChannelOperation channel => channel.ChannelId,
        TuneJunctionOperation junction => JunctionScope(junction.JunctionId),
        ProbeOperation probe => $"{probe.Probe}:{probe.JunctionId}",
        _ => null
    };

    // A tune writes one crossover to both sides of both blocks, so left:A-B and right:A-B are one junction to it.
    private static string JunctionScope(string junctionId) =>
        AgentJunctionIds.TryParse(junctionId, out _, out string lower, out string upper)
            ? $"{lower}-{upper}"
            : junctionId;

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

    /// <summary>Per-channel warnings about the state after the given rows. The commit reruns it on the ticked rows: unticking can leave a state the review never showed.</summary>
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

    /// <summary>Commit-time check of the ticked set applied to copies. Null when admissible; otherwise the first problem refuses the whole commit.</summary>
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

    private static string LabelFor(AgentOperation operation, AgentChannelSnapshot? channel) =>
        channel?.Label ?? operation switch
        {
            AgentChannelOperation => string.Empty,
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

    /// <summary>An engine request judged on its inputs: no current value to compare; the row states the start, the ask and what it overwrites.</summary>
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
        if (operation is TuneJunctionOperation junction && FirCutJunctionNote(junction, session) is { } firNote)
        {
            message += " " + firNote;
        }

        return new AgentOperationVerdict(
            operation.Id, LabelFor(operation, channel), operation.Parameter, current, proposed,
            status, message, operation.Reason, operation, channel);
    }

    // The junction tune writes IIR edges on both sides of both blocks; a side already cut by a FIR crossover would be filtered twice. Allowed, but said.
    private static string? FirCutJunctionNote(TuneJunctionOperation junction, AgentSessionSnapshot session)
    {
        if (ResolveJunction(session, junction.JunctionId, out AgentChannelSnapshot? lower, out AgentChannelSnapshot? upper) != null)
        {
            return null;
        }

        var cut = session.Channels
            .Where(channel =>
                (channel.Block == lower!.Block || channel.Block == upper!.Block) &&
                channel.Settings.HasFirCrossover)
            .Select(channel => $"{channel.Label} ({FirCrossoverDescription.Short(channel.Settings.FirDesign!)})")
            .Distinct()
            .ToList();
        return cut.Count == 0
            ? null
            : $"{string.Join(", ", cut)} {(cut.Count == 1 ? "is" : "are")} already cut by a " +
              "linear-phase FIR crossover; the IIR edges this tune writes filter " +
              (cut.Count == 1 ? "it" : "them") + " twice.";
    }

    // A warning wherever the run reaches past the channels the reply names.
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
            "after re-aligning the upper block for each candidate; gains, delays, polarity, " +
            "PEQ and every other junction stay. It runs without a dialog and keeps the " +
            "current crossover unless a " +
            "candidate clearly beats it; its report goes into the import's summary."),
        _ => (AgentVerdictStatus.Valid, "OK")
    };

    // Stated inputs are held to their dialog fields; omitted ones are the panel's own answer.
    private static string? CheckEngineRequest(
        AgentOperation operation, AgentChannelSnapshot? channel, AgentSessionSnapshot session)
    {
        switch (operation)
        {
            case RunAutoDelayOperation delay:
                // The fields' own ranges: a clamped input would not be the reviewed run.
                return Bounded(delay.SceneOffsetMs, VirtualCrossoverLimits.SceneOffset, "The scene offset", "ms")
                    ?? Bounded(delay.NearSideCutDb, VirtualCrossoverLimits.NearSideCut, "The near-side cut", "dB")
                    ?? Bounded(delay.RearFillOffsetMs, VirtualCrossoverLimits.RearFillOffset, "The rear fill offset", "ms");

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

    /// <summary>The two channels a junction id names, or why none: both measured, summed, not bypassed, one group, and spectral neighbours that hand over (the panel's junction adjacency).</summary>
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

        // A real handover: both play inside the octave-each-way band around the pair's corner.
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

    /// <summary>The other junctions the changed channels take part in (a channel hands over twice), excluding <paramref name="junctionId"/>.</summary>
    public static IReadOnlyList<string> NeighbourJunctionIds(
        AgentSessionSnapshot session, string junctionId, IReadOnlyCollection<string> changedChannelIds)
    {
        ArgumentNullException.ThrowIfNull(session);
        var found = new List<string>();
        if (changedChannelIds == null || changedChannelIds.Count == 0)
        {
            return found;
        }

        // Only candidates ResolveJunction accepts, so the report never names an unprobeable junction.
        foreach (AgentChannelSide side in new[] { AgentChannelSide.Left, AgentChannelSide.Right })
        {
            IEnumerable<IGrouping<VirtualCrossoverAlignmentStage, AgentChannelSnapshot>> groups = session.Channels
                .Where(channel => channel.PlaysOn(side) && channel.HasMeasurement &&
                    channel.Enabled && !channel.Bypass)
                .GroupBy(channel => VirtualCrossoverAlignmentStages.StageOf(channel.Zone));
            foreach (IGrouping<VirtualCrossoverAlignmentStage, AgentChannelSnapshot> group in groups)
            {
                List<AgentChannelSnapshot> byBand = group
                    .OrderBy(channel => VirtualCrossoverJunctions.BandCenterHz(channel.Settings))
                    .ToList();
                for (int index = 0; index + 1 < byBand.Count; index++)
                {
                    if (!changedChannelIds.Contains(byBand[index].Id, StringComparer.Ordinal) &&
                        !changedChannelIds.Contains(byBand[index + 1].Id, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    string id = AgentJunctionIds.Format(side, byBand[index].Block, byBand[index + 1].Block);
                    if (string.Equals(id, junctionId, StringComparison.Ordinal) ||
                        found.Contains(id, StringComparer.Ordinal) ||
                        ResolveJunction(session, id, out _, out _) != null)
                    {
                        continue;
                    }

                    found.Add(id);
                }
            }
        }

        return found;
    }

    private static bool PlaysWithin(VirtualCrossoverChannelSettings settings, double lowHz, double highHz)
    {
        (double channelLow, double channelHigh) = VirtualCrossoverJunctions.GetChannelBand(settings);
        return channelHigh > lowHz && channelLow < highHz;
    }

    /// <summary>Default corner window: half an octave each way, snapped to the wizard's lattice.</summary>
    public static (double MinHz, double MaxHz) DefaultJunctionWindow(double currentHz) =>
        (Math.Max(EqAutoTuneHeadless.WindowMinHz, CrossoverAutoSetup.RoundToLattice(currentHz / Math.Sqrt(2))),
            Math.Min(EqAutoTuneHeadless.WindowMaxHz, CrossoverAutoSetup.RoundToLattice(currentHz * Math.Sqrt(2))));

    // Held to what the package could have printed: known series, channels, resolvable junction, density ceilings.
    private static string? CheckSeriesProbe(ProbeOperation probe, AgentSessionSnapshot session)
    {
        IReadOnlyList<string> series = probe.Series ?? [];
        if (series.Count == 0)
        {
            return "A series probe names nothing to read; its `series` lists one or more of " +
                string.Join(", ", AgentProtocol.SeriesNames) + ".";
        }
        foreach (string name in series)
        {
            if (!AgentProtocol.SeriesNames.Contains(name, StringComparer.Ordinal))
            {
                return $"'{name}' is not a series a probe can read; the names are " +
                    string.Join(", ", AgentProtocol.SeriesNames) + ".";
            }
        }
        foreach (string id in probe.ChannelIds ?? [])
        {
            if (!session.Channels.Any(channel => string.Equals(channel.Id, id, StringComparison.Ordinal)))
            {
                return $"'{id}' is not a channel of this session; the package's channel ids are " +
                    "block and side, as in 'C:left'.";
            }
        }
        if (probe.JunctionId != null)
        {
            string? problem = ResolveJunction(session, probe.JunctionId, out _, out _);
            if (problem != null)
            {
                return problem;
            }
        }
        if (probe.PointsPerOctave is { } points && (points < 1 || points > AgentSampling.MaxPointsPerOctave))
        {
            return $"A series probe reads between 1 and {AgentSampling.MaxPointsPerOctave} points per " +
                $"octave (limits.seriesPointsPerOctave); this one asks for {points}.";
        }
        if (probe.Rows is { } rows && (rows < 2 || rows > AgentSampling.MaxRows))
        {
            return $"A series probe reads between 2 and {AgentSampling.MaxRows} rows of a lag series " +
                $"(limits.seriesRows); this one asks for {rows}.";
        }

        return session.Channels.Any(channel => channel.HasMeasurement)
            ? null
            : "No channel in this session has a measurement to read.";
    }

    // A probe protects no value, only answerability; variant settings get settings-operation limits, since a good variant is meant to become one.
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
        if (probe.Probe == AgentProtocol.SeriesProbe)
        {
            return CheckSeriesProbe(probe, session);
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
            return lower!.Settings.EffectiveLowPassHz != null ||
                upper!.Settings.EffectiveHighPassHz != null
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

    /// <summary>Applies a probe variant's change into <paramref name="copy"/> (already a copy) under settings-operation limits. Returns the first problem, or null.</summary>
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

    // Expected values are the copy's own: nothing can move under a probe.
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

    // Low-pass first; Linkwitz-Riley where neither edge exists yet.
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
        // A fit is built on the shown side's handoff (gate pin, render anchor, hybrid datum), as the PEQ menu builds it.
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
        if (tune.Boosts != null &&
            tune.Boosts != BoostsOff && tune.Boosts != BoostsRefillOwnCuts && tune.Boosts != BoostsAllowed)
        {
            return $"Unknown auto-tune boosts '{tune.Boosts}'; " +
                $"use '{BoostsOff}', '{BoostsRefillOwnCuts}' or '{BoostsAllowed}'.";
        }
        if (tune.Boosts != null && tune.CutsOnly != null)
        {
            return "State boosts or cutsOnly, not both.";
        }

        string? problem =
            Bounded(tune.TargetLevelDb, VirtualCrossoverLimits.TargetLevel, "The target level", "dB")
            ?? Edge(tune.MinHz, nyquistHz, "lower")
            ?? Edge(tune.MaxHz, nyquistHz, "upper");
        if (problem != null)
        {
            return problem;
        }

        // Judged as a whole with the wizard's defaults filled in: a stated lower edge above the passband's upper is inverted too.
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

    // A field's range and its step: a typed value is rounded to the field's decimal places.
    private static string? Bounded(double? value, NumericFieldRange range, string name, string unit)
    {
        if (value is not { } number)
        {
            return null;
        }

        (double minimum, double maximum, double step, int decimals) =
            ((double)range.Minimum, (double)range.Maximum, (double)range.Step, range.Decimals);
        if (!double.IsFinite(number) || number < minimum || number > maximum)
        {
            return $"{name} must be between {Fixed(minimum, decimals)} and " +
                $"{Fixed(maximum, decimals)} {unit}.";
        }

        return OnStep(number, step)
            ? null
            : $"{name} must be a multiple of {Fixed(step, decimals)} {unit}.";
    }

    // A value a stated edge moves must be one the block's field shows unchanged; one restated as stored is kept as it is.
    // A ripple another family stored unchecked is moved into use by turning the edge Chebyshev.
    private static string? Showable(AgentCrossoverEdge? stated, CrossoverEdge stored, CrossoverEdge mapped)
    {
        if (stated == null)
        {
            return null;
        }

        string? corner = mapped.FrequencyHz == stored.FrequencyHz
            ? null
            : Bounded(mapped.FrequencyHz, VirtualCrossoverLimits.CrossoverCorner, "A crossover corner", "Hz");
        bool rippleRunsAsStored = stored.Family == CrossoverFilterFamily.Chebyshev && mapped.RippleDb == stored.RippleDb;
        return corner ?? (mapped.Family != CrossoverFilterFamily.Chebyshev || rippleRunsAsStored
            ? null
            : Bounded(mapped.RippleDb, VirtualCrossoverLimits.ChebyshevRipple, "The Chebyshev ripple", "dB"));
    }

    // The From/To fields' range, capped at the processor's Nyquist.
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
            ProbeOperation probe => probe.JunctionId is { } id
                ? JunctionStartText(id, session)
                : "every measured channel",
            UseSpatialAverageOperation => SpatialAverageText(
                session.SpatialAverageMode.ToString(), session.HybridTicked),
            _ => string.Empty
        };

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

    private static string ProbeText(ProbeOperation probe) => probe.Probe switch
    {
        AgentProtocol.JunctionProbe =>
            $"read this junction under {Count(probe.Variants?.Count ?? 0, "variant")} " +
            $"({ProbeVariantText(probe)}) beside the settings it has now",
        AgentProtocol.JunctionDelayProbe =>
            "read what a delay search would find at this junction",
        AgentProtocol.ExcessGroupDelayProbe =>
            "read every measured channel's excess group delay",
        AgentProtocol.SeriesProbe =>
            $"read {string.Join(", ", probe.Series ?? [])} again" +
            (probe.PointsPerOctave is { } points ? $" at {points} points per octave" : string.Empty) +
            (probe.Rows is { } rows ? $", up to {rows} rows" : string.Empty) +
            (probe.ChannelIds is { Count: > 0 } ids ? $" for {string.Join(", ", ids)}" : string.Empty) +
            ", unthinned and with no size limit",
        _ => $"read '{probe.Probe}'"
    };

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

    // Only what the reply states; omitted inputs are the tuner's own.
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

    // Near-side cut only with gain balance on: otherwise it is an input to nothing.
    private static string AutoDelayText(
        double sceneOffsetMs, bool rightHandDrive, bool adjustGains,
        double nearSideCutDb, double rearFillOffsetMs) =>
        $"scene {Ms(sceneOffsetMs)} {(rightHandDrive ? "RHD" : "LHD")}, " +
        $"gains {(adjustGains ? "on" : "off")}" +
        (adjustGains ? $", near-side cut {Db(nearSideCutDb)}" : string.Empty) +
        $", rear fill {Ms(rearFillOffsetMs)}";

    // Only what the reply states: printing defaults would read as the reply's choice.
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
        if (tune.BoostMode is { } boosts)
        {
            parts.Add(EqWizardFit.DescribeBoosts(boosts));
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

    // Exact: the package prints round-trip values, so a tolerance would admit a reply reasoned about a different value.
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
                if (Bounded(gain.ProposedDb, VirtualCrossoverLimits.ChannelGain, "Gain", "dB") is { } gainProblem)
                {
                    return gainProblem;
                }
                break;

            case SetDelayOperation delay:
                if (Bounded(delay.ProposedMs, VirtualCrossoverLimits.ChannelDelay, "Delay", "ms") is { } delayProblem)
                {
                    return delayProblem;
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
                if ((Showable(crossover.Proposed.HighPass, copy.HighPassEdge, highPass) ??
                     Showable(crossover.Proposed.LowPass, copy.LowPassEdge, lowPass)) is { } unshowable)
                {
                    return unshowable;
                }
                // Allowed (two crossovers are a legitimate chain) but said, to explain the red FIR button.
                if (kind != CrossoverKind.Off && copy.HasFirCrossover)
                {
                    notes.Add(
                        "This side is already cut by a linear-phase FIR crossover " +
                        $"({FirCrossoverDescription.Short(copy.FirDesign!)}); an IIR crossover here filters it twice.");
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
                // Headroom on the NET response, not band signs: a net rise above unity is where full scale clips. A warning only.
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

    // Abbreviated like the channel block's combo to fit a table cell.
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

    // "+ 0" folds negative zero, which would print as "-0.0".
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

/// <summary>The one place an operation touches channel settings: the review judges a copy and the commit writes the live object through the same path.</summary>
internal static class AgentOperations
{
    /// <summary>A copy of the editable chain only (no source, history or path).</summary>
    public static VirtualCrossoverChannelSettings CloneEditable(VirtualCrossoverChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The undo restores through CopyEditable, so the snapshot takes exactly the fields it restores.
        var copy = new VirtualCrossoverChannelSettings { DisplayName = settings.DisplayName };
        AgentProposalApplier.CopyEditable(settings, copy);
        return copy;
    }

    /// <summary>Writes the operation; the review has already passed it.</summary>
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

    /// <summary>Resolves a reply's crossover against stored edges: omitted edges and ripple keep the stored ones; edges the kind uses must be stated.</summary>
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

    /// <summary>Whether the reply's expected crossover is current: same kind, used edges equal in family, corner, slope (and ripple if stated).</summary>
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

    // Exact package names: Enum.TryParse would also accept other casing and numeric strings.
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
