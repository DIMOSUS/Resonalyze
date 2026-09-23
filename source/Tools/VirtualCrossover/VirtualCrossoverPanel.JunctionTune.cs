using Resonalyze.Dsp;

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
            index => VirtualCrossoverJunctionTuneSearch.Opening(junctions, index),
            request => RunJunctionTuneAsync(junctions, request),
            session.Project.JunctionTune,
            junctionTune.Undoable(session.ProjectGeneration));
        DialogResult answer = dialog.ShowDialog(FindForm());
        if (IsDisposed)
        {
            junctionTune.Landed = null;
            return;
        }

        session.Project.JunctionTune = dialog.Remembered();
        if (dialog.UndoRequested)
        {
            junctionTune.Landed = null;
            UndoJunctionTune();
            return;
        }

        if (answer != DialogResult.OK ||
            dialog.Result is not { } request ||
            junctionTune.Landed is not { } landed)
        {
            junctionTune.Landed = null;
            ScheduleSave();
            return;
        }

        junctionTune.Landed = null;
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
        AgentImportUndo before = junctionTune.Apply(lower, upper, landed, goal, AgentView());
        ApplySettingsToControl(lower);
        ApplySettingsToControl(upper);
        // Both sides were decided here, so the Lock remembers rather than carries.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        junctionTune.Remember(before, session.ProjectGeneration, lower, upper, ComputeAgentFingerprint());
    }

    /// <summary>Changes made since the Apply go too, so that is asked first.</summary>
    private void UndoJunctionTune()
    {
        if (junctionTune.UndoFor(session.ProjectGeneration) is not { } undo)
        {
            return;
        }

        if (!junctionTune.Unchanged(ComputeAgentFingerprint()) &&
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

        RestoreChannels(junctionTune.TakeUndo());
    }

    private readonly VirtualCrossoverJunctionTuneApply junctionTune;

    private async Task<JunctionTuneOutcome> RunJunctionTuneAsync(
        List<AdjacentPair> junctions, JunctionTuneRequest request)
    {
        junctionTune.Landed = null;
        if (request.JunctionIndex < 0 || request.JunctionIndex >= junctions.Count)
        {
            return VirtualCrossoverJunctionTuneSearch.NotInView();
        }

        if (GateIsMisplaced)
        {
            return VirtualCrossoverJunctionTuneSearch.GateMisplaced();
        }

        (JunctionTunePlan? plan, JunctionTuneOutcome? refused) = VirtualCrossoverJunctionTuneSearch.Plan(
            session,
            junctions[request.JunctionIndex],
            request,
            HybridRequested ? session.SpatialAverageMode : null);
        if (plan == null)
        {
            return refused!;
        }

        string fingerprintBefore = ComputeAgentFingerprint();
        JunctionTuneResult result;
        UseWaitCursor = true;
        try
        {
            result = await Task.Run(() => CrossoverJunctionTuner.Tune(plan.Sides, plan.Options))
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return VirtualCrossoverJunctionTuneSearch.Refusal(exception.Message.TrimEnd('.'));
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
            return VirtualCrossoverJunctionTuneSearch.Refusal("the session changed while the search ran");
        }

        junctionTune.Landed = result;
        return VirtualCrossoverJunctionTuneSearch.Outcome(plan, request, result);
    }
}
