using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    /// <summary>
    /// Tune junction: refines one junction's facing edges on the coherent sum through the full chains, with the
    /// acoustic goal as an optional constraint. The engine is the one the AI assistant drives
    /// (<see cref="CrossoverJunctionTuner"/>); this is the same tune with a dialog in front of it.
    /// </summary>
    private async Task ShowJunctionTuneDialogAsync()
    {
        List<AdjacentPair> junctions = CurrentCorrelationPairs();
        using var dialog = new VirtualCrossoverJunctionTuneDialog();
        dialog.Init(
            junctions
                .Select(pair => $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}")
                .ToList(),
            index => JunctionTuneOpening(junctions, index),
            request => RunJunctionTuneAsync(junctions, request),
            session.Project.JunctionTune,
            junctionTuneUndo is { } undo && undo.Generation == projectGeneration ? undo.Junction : null);
        DialogResult answer = dialog.ShowDialog(FindForm());
        if (IsDisposed)
        {
            lastJunctionTune = null;
            return;
        }

        // Kept however the dialog closed: the next opening starts where this one was left, Apply or not.
        session.Project.JunctionTune = dialog.Remembered();
        if (dialog.UndoRequested)
        {
            lastJunctionTune = null;
            UndoJunctionTune();
            return;
        }

        if (answer != DialogResult.OK ||
            dialog.Result is not { } request ||
            lastJunctionTune is not { } landed)
        {
            lastJunctionTune = null;
            ScheduleSave();
            return;
        }

        lastJunctionTune = null;
        if (request.JunctionIndex >= junctions.Count)
        {
            return;
        }

        ApplyJunctionTune(
            junctions[request.JunctionIndex].Lower.Channel,
            junctions[request.JunctionIndex].Upper.Channel,
            landed,
            request.AcousticGoal);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>What Apply writes for a landed tune, onto the settings and the channel cards.</summary>
    private void ApplyJunctionTune(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionTuneResult landed,
        JunctionAcousticTarget? goal)
    {
        // Every channel as it was, taken before the first write: Undo last Apply in the dialog puts it back, as
        // Undo AI import does for an import.
        AgentImportUndo before = CaptureAgentUndo();
        // The same write the assistant's tune makes: one crossover into both sides of both blocks, and the goal onto
        // the edges the applied crossover runs. The crossover is the one the report calls found whenever it differs
        // from the one on screen: the keep margin is the report's advice, and Apply is the user overruling it.
        // Where nothing different was found, only the goal is written. The goal is written as asked whether or not
        // the crossover lands on it; the report says how far it is.
        AgentJunctionTune.Write(landed, lower, upper, goal, applyCrossover: landed.Moves);
        ApplySettingsToControl(lower);
        ApplySettingsToControl(upper);
        // Both sides were decided here, so the Lock remembers rather than carries.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        junctionTuneUndo = new JunctionTuneUndo(
            before, projectGeneration, $"{lower.Name}/{upper.Name}", ComputeAgentFingerprint());
    }

    /// <summary>Puts every channel back as it was before the last Apply. Where the session has changed since, the
    /// later changes go too, so that is asked first.</summary>
    private void UndoJunctionTune()
    {
        if (junctionTuneUndo is not { } undo || undo.Generation != projectGeneration)
        {
            junctionTuneUndo = null;
            return;
        }

        if (!string.Equals(undo.FingerprintAfter, ComputeAgentFingerprint(), StringComparison.Ordinal) &&
            MessageBox.Show(
                FindForm(),
                $"The session has changed since the tune of {undo.Junction} was applied. Undo puts every channel " +
                "back exactly as it was before that Apply, so the later changes go as well." +
                Environment.NewLine + Environment.NewLine + "Undo anyway?",
                "Tune junction",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        junctionTuneUndo = null;
        RestoreChannels(undo.Channels);
    }

    private JunctionTuneResult? lastJunctionTune;

    /// <summary>The last Apply's undo: every channel before it, the project generation it wrote into (a session
    /// load retires it), the junction it was for, and the session as the Apply left it.</summary>
    private sealed record JunctionTuneUndo(
        AgentImportUndo Channels, long Generation, string Junction, string FingerprintAfter);

    private JunctionTuneUndo? junctionTuneUndo;

    /// <summary>What the dialog opens on for a junction: the assistant's own window, the families in use, and the
    /// acoustic goal the two channel cards already hold.</summary>
    private JunctionTuneDefaults JunctionTuneOpening(List<AdjacentPair> junctions, int index)
    {
        if (index < 0 || index >= junctions.Count)
        {
            return new JunctionTuneDefaults(20, 20_000, [CrossoverFilterFamily.LinkwitzRiley], null);
        }

        VirtualCrossoverChannelSettings lower = junctions[index].Lower.Channel.Settings;
        VirtualCrossoverChannelSettings upper = junctions[index].Upper.Channel.Settings;
        double currentHz = VirtualCrossoverJunctions.GetPairCrossoverHz(lower, upper);
        (double minHz, double maxHz) = currentHz > 0
            ? AgentProposalValidator.DefaultJunctionWindow(currentHz)
            : (junctions[index].BandLowHz, junctions[index].BandHighHz);
        return new JunctionTuneDefaults(
            minHz,
            maxHz,
            AgentProposalValidator.CurrentFamilies(lower, upper),
            (lower.RunsLowPass ? lower.AcousticLowPass : null) ?? (upper.RunsHighPass ? upper.AcousticHighPass : null),
            currentHz > 0 ? currentHz : null);
    }

    /// <summary>Runs the search off the UI thread and builds the report; writes nothing.</summary>
    private async Task<JunctionTuneOutcome> RunJunctionTuneAsync(
        List<AdjacentPair> junctions, JunctionTuneRequest request)
    {
        lastJunctionTune = null;
        if (request.JunctionIndex < 0 || request.JunctionIndex >= junctions.Count)
        {
            return new JunctionTuneOutcome(
                [JunctionTuneLine.Of("The junction is no longer in this view.")],
                false,
                "Nothing to search.",
                true);
        }

        VirtualCrossoverChannel lower = junctions[request.JunctionIndex].Lower.Channel;
        VirtualCrossoverChannel upper = junctions[request.JunctionIndex].Upper.Channel;
        string label = $"Junction tune {lower.Name}/{upper.Name}";
        if (GateIsMisplaced)
        {
            return new JunctionTuneOutcome(
                [JunctionTuneLine.Of(
                    "The phase gate is misplaced, so the junction cannot be read through it.")],
                false,
                "Place the gate first.",
                true);
        }

        // A designed FIR crossover on a facing edge is a second crossover stage, not a slope this tune can fit.
        if (FirCrossoverOn(lower.Settings, lowPass: true) is { } lowerFir)
        {
            return Refusal(lowerFir);
        }
        if (FirCrossoverOn(upper.Settings, lowPass: false) is { } upperFir)
        {
            return Refusal(upperFir);
        }

        (List<JunctionTuneSide> sides, string? refusal) =
            AgentProbeReader.JunctionTuneSides(lower, upper, rightSideOnly: null);
        if (refusal != null)
        {
            return Refusal(refusal);
        }

        // A stated slope is judged on the curve the EQ stage fits next: the spatial average wherever the hybrid
        // hands one to Auto Tune, the gated reading elsewhere.
        if (request.AcousticGoal != null)
        {
            sides = AgentProbeReader.WithSpatialAverages(
                sides,
                lower,
                upper,
                HybridRequested ? session.SpatialAverageMode : null,
                session.Calibration.SpatialAverageFor(),
                session.ProcessorSampleRateHz);
        }

        var options = new JunctionTuneOptions(
            request.Families,
            // Null would mean "every slope at or above the floor"; the dialog always states a window.
            request.Slopes.Count > 0 ? request.Slopes : null,
            Math.Min(request.MinHz, request.MaxHz),
            Math.Max(request.MinHz, request.MaxHz),
            request.IndependentSlopes,
            session.ProcessorSampleRateHz,
            AcousticTarget: request.AcousticGoal,
            TargetCurveDb: request.AcousticGoal == null ? null : TargetCurvePoints(),
            SumSlackDb: request.SumSlackDb,
            SplitCorners: request.SplitCorners,
            OneAlignmentForAllSides: AgentProbeReader.SharesOneAlignment(lower, upper));
        var plan = new JunctionTunePlan(label, lower, upper, sides, options);
        string fingerprintBefore = ComputeAgentFingerprint();
        JunctionTuneResult result;
        UseWaitCursor = true;
        try
        {
            result = await Task.Run(() => CrossoverJunctionTuner.Tune(sides, options))
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Refusal(exception.Message.TrimEnd('.'));
        }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }

        if (IsDisposed)
        {
            return new JunctionTuneOutcome([], false, "Closed.", true);
        }
        if (!string.Equals(fingerprintBefore, ComputeAgentFingerprint(), StringComparison.Ordinal))
        {
            return Refusal("the session changed while the search ran");
        }

        // The dialog's own layout, not the import summary's one-line-per-item list: a monospace pane wants columns.
        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        lastJunctionTune = result;
        // What the search cost goes in the status line: it says whether the question was wide enough, which is not
        // something to read in the pane every time.
        string searched =
            $"{result.CandidatesEvaluated} candidates read over " +
            $"{options.MinCrossoverHz:0.###}–{options.MaxCrossoverHz:0.###} Hz.";
        // Apply writes what the report calls found and the goal that was asked, so it is offered whenever that
        // changes something: a different crossover, or a goal the cards do not state yet. A button that closes
        // the window and changes nothing is the one thing it must not be.
        JunctionTuneCandidate applied = result.Moves ? result.Best : result.Current;
        bool goalChanges = AgentJunctionTune.WouldChangeGoal(lower, upper, request.AcousticGoal, applied);
        // At the channel that misses most, as the report reads it.
        bool goalLands = request.AcousticGoal != null &&
            CrossoverJunctionTuner.WasAcousticTargetReached(applied.WorstAcousticCostDb);
        bool forTheGoal = request.AcousticGoal != null &&
            result.Best.RankingScoreDb > result.Current.RankingScoreDb;
        string verdict = result.Changed
            ? forTheGoal
                ? "A crossover nearer the goal was found, within the budget; Apply writes it. "
                : "A better crossover was found; Apply writes it. "
            : result.Moves
                ? "Keeping the crossover on screen is recommended; Apply writes the found one anyway. "
                : goalChanges
                    ? "The crossover on screen is the best found; Apply writes the goal onto the cards. "
                    : "The crossover on screen is the best found; nothing to apply. ";
        return new JunctionTuneOutcome(
            report,
            result.Moves || goalChanges,
            verdict + searched,
            false,
            // A goal the crossover misses is written as asked, but it is not what the search advises.
            Recommended: (result.Changed || (!result.Moves && goalChanges)) &&
                (request.AcousticGoal == null || goalLands));

        static JunctionTuneOutcome Refusal(string because) =>
            new(
                [JunctionTuneLine.Of(because[..1].ToUpperInvariant() + because[1..] + ".")],
                false,
                "Refused.",
                true);
    }

    /// <summary>Why a facing edge cannot be tuned here, or null. A correction FIR is no obstacle; a crossover one is.</summary>
    private static string? FirCrossoverOn(VirtualCrossoverChannelSettings settings, bool lowPass)
    {
        if (!settings.HasFirCrossover || settings.FirDesign is not { } design)
        {
            return null;
        }

        bool facing = lowPass
            ? design.Kind is CrossoverKind.LowPass or CrossoverKind.BandPass
            : design.Kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        return facing
            ? $"the {(lowPass ? "low" : "high")}-pass here is a designed FIR crossover, and this tune " +
              "fits IIR edges only — clear the kernel or tune the junction by hand"
            : null;
    }

    private IReadOnlyList<SignalPoint> TargetCurvePoints()
    {
        TargetCurveSpec spec = (session.Project.Target ?? new VirtualCrossoverTargetSettings())
            .ToCurve().Normalized().Spec;
        return EqualizationCurve.LogFrequencyGrid(20, 20_000, 400)
            .Select(hz => new SignalPoint(hz, spec.Evaluate(hz)))
            .ToList();
    }
}
