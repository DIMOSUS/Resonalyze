using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    /// <summary>The AI assistant's junction tune (<see cref="CrossoverJunctionTuner"/>) with a dialog in front.</summary>
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

    private void ApplyJunctionTune(
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        JunctionTuneResult landed,
        JunctionAcousticTarget? goal)
    {
        AgentImportUndo before = CaptureAgentUndo();
        // The found crossover whenever it differs, won or not: the keep margin is advice.
        AgentJunctionTune.Write(landed, lower, upper, goal, applyCrossover: landed.Moves);
        ApplySettingsToControl(lower);
        ApplySettingsToControl(upper);
        // Both sides were decided here, so the Lock remembers rather than carries.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        junctionTuneUndo = new JunctionTuneUndo(
            before, projectGeneration, $"{lower.Name}/{upper.Name}", ComputeAgentFingerprint());
    }

    /// <summary>Changes made since the Apply go too, so that is asked first.</summary>
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

    private sealed record JunctionTuneUndo(
        AgentImportUndo Channels, long Generation, string Junction, string FingerprintAfter);

    private JunctionTuneUndo? junctionTuneUndo;

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

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        lastJunctionTune = result;
        string searched =
            $"{result.CandidatesEvaluated} candidates read over " +
            $"{options.MinCrossoverHz:0.###}–{options.MaxCrossoverHz:0.###} Hz.";
        // Apply is offered whenever it changes something, and only then.
        JunctionTuneCandidate applied = result.Moves ? result.Best : result.Current;
        bool goalChanges = AgentJunctionTune.WouldChangeGoal(lower, upper, request.AcousticGoal, applied);
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
            Recommended: (result.Changed || (!result.Moves && goalChanges)) &&
                (request.AcousticGoal == null || goalLands));

        static JunctionTuneOutcome Refusal(string because) =>
            new(
                [JunctionTuneLine.Of(because[..1].ToUpperInvariant() + because[1..] + ".")],
                false,
                "Refused.",
                true);
    }

    /// <summary>A designed FIR crossover on a facing edge is a second stage, not an edge this tune fits.</summary>
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
