using Resonalyze.Dsp;

namespace Resonalyze;

// Virtual DSP handoff: the session holds which side the bank belongs to; this binds the Return and Back buttons.
public partial class EqWizardPanel
{
    /// <summary>
    /// Sends the bank back to Virtual DSP, with the target LEVEL: the preamp was fitted against it.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<VirtualDspEqReturnToken, EqualizationCurve, double>?
        ReturnPeqRequested
    { get; set; }

    /// <summary>Leaves the session without applying; the channel keeps its PEQ and the wizard keeps its edits.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action? BackToVirtualDspRequested { get; set; }

    /// <summary>
    /// Installs a channel side: curve as source, PEQ seeds the bank (one undo step), crossover sets the Auto Tune window.
    /// </summary>
    internal void BeginVirtualDspHandoff(VirtualDspEqHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Orphan in-flight source loads, which would otherwise replace the handed-over channel and end the session.
        sourceLoadGeneration++;
        bankEditTimer.Stop();
        session.BeginHandoff(request);
        Present(() => comboBoxSmooth.SelectedItem = session.SourceSmoothingInverseOctaves);
        PresentSource();
        PresentBank(keepSelection: true);
        PresentViewSettings();
        PresentWindow();
        Redraw();
    }

    // The target level's range narrows while the handoff lasts; the buttons that end it show only then.
    private void PresentHandoff()
    {
        PresentTargetOffset();
        bool inHandoff = session.HandoffToken != null;
        buttonReturnToDsp.Visible = inHandoff;
        buttonBackToDsp.Visible = inHandoff;
    }

    private void ReturnPeqToVirtualDsp()
    {
        if (session.HandoffToken == null)
        {
            return;
        }

        CommitPendingBankEdit();
        // Ends either way: on failure the channel is gone; the wizard keeps the bank for export.
        EqWizardReturn sent = session.CompleteHandoff()!;
        PresentHandoff();
        ReturnPeqRequested?.Invoke(sent.Token, sent.Bank, sent.TargetLevelDb);
    }

    // Unlike a tab switch, which keeps the session and Return button alive.
    private void BackToVirtualDsp()
    {
        if (!session.LeaveHandoff())
        {
            return;
        }

        PresentHandoff();
        BackToVirtualDspRequested?.Invoke();
    }
}
