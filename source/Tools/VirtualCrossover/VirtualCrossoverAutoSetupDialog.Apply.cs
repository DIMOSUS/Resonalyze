using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private bool ConfirmChainOrder() =>
        AutoSetupWizardChainOrder.Question(session) is not { } question ||
        MessageBox.Show(
            this,
            question,
            "Auto crossover",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    // Frozen during ranking so the applied result matches the visible settings.
    private IEnumerable<Control> RankingInputControls()
    {
        foreach (AutoSetupWizardRow source in session.Rows)
        {
            ChannelRow row = rows[source];
            yield return row.TypeComboBox;
            yield return row.Up;
            yield return row.Down;
        }

        // The ranked run takes its options snapshot before it starts; a junction edited after that would show one
        // window while Apply wrote a proposal built from another.
        foreach (JunctionRow junction in junctions)
        {
            yield return junction.MinHz;
            yield return junction.MaxHz;
            yield return junction.MinSlope;
            yield return junction.MaxSlope;
            yield return junction.Split;
        }

        foreach ((CheckBox box, CrossoverFilterFamily _) in familyBoxes)
        {
            yield return box;
        }

        yield return minCrossover;
        yield return maxCrossover;
        yield return independentSlopes;
        yield return reorderBlocks;
        yield return subElevation;
    }

    private void SetRankingInputsEnabled(bool enabled)
    {
        foreach (Control control in RankingInputControls())
        {
            control.Enabled = enabled;
        }

        subElevation.Enabled = enabled && session.SubElevationApplies;
        // As in the other dialogs: no Undo while a write is on its way.
        buttonUndo.Enabled = enabled && undoOffered;
        if (enabled)
        {
            PopulateTable();
        }
    }

    private async void ApplyClick(object? sender, EventArgs e)
    {
        List<AutoSetupGroupFit>? quick = AutoSetupWizardFit.TryFit(session);
        if (quick == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (!ConfirmChainOrder())
        {
            return;
        }

        List<AutoSetupGroupPlan> plan = AutoSetupWizardPlan.Groups(session, withImpulseResponses: true);
        if (plan.All(group => group.ImpulseResponses == null))
        {
            Result = AutoSetupWizardFit.InInitOrder(quick, session.Rows.Count);
            ChainOrder = session.RequestedChainOrder();
            DialogResult = DialogResult.OK;
            return;
        }

        // Ranking takes seconds on a 4-way; the preview shows the magnitude-only proposal until it lands.
        IReadOnlyList<int>? order = session.RequestedChainOrder();
        // Snapshot per group on the UI thread: the ranked search runs off it and must not read the controls.
        Dictionary<VirtualCrossoverAlignmentStage, CrossoverAutoSetupOptions> snapshot =
            AutoSetupWizardPlan.Snapshot(session, plan);
        CrossoverAutoSetupOptions Options(AutoSetupGroupPlan group) => snapshot[group.Group];
        string previousPreview = labelPreview.Text;
        int count = session.Rows.Count;
        double rateHz = session.SampleRateHz;
        rankingInProgress = true;
        CancelPreviewWork();
        buttonApply.Enabled = false;
        SetRankingInputsEnabled(false);
        labelPreview.Text = "Ranking candidates against the measured responses…";
        try
        {
            List<AutoSetupGroupFit> ranked = await Task.Run(
                () => AutoSetupWizardFit.Fit(plan, Options, rateHz));
            if (IsDisposed)
            {
                return;
            }

            Result = AutoSetupWizardFit.InInitOrder(ranked, count);
            ChainOrder = order;
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException)
        {
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
            rankingInProgress = false;
            buttonApply.Enabled = true;
            SetRankingInputsEnabled(true);
            System.Media.SystemSounds.Beep.Play();
        }
        catch (Exception exception)
        {
            // An exception after await in async void would kill the process via the WinForms context.
            if (IsDisposed)
            {
                return;
            }

            labelPreview.Text = previousPreview;
            rankingInProgress = false;
            buttonApply.Enabled = true;
            SetRankingInputsEnabled(true);
            MessageBox.Show(
                this,
                $"Candidate ranking failed.\r\n\r\n{exception.Message}",
                "Auto crossover",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
