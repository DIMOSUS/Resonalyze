using Resonalyze.Dsp;

namespace Resonalyze;

// The Virtual DSP handoff session: a channel side arrives from the DSP tool with its
// curve, its PEQ and a return address, gets edited with the wizard's ordinary tools,
// and goes back through the Return button. The session only adds bookkeeping — which
// side the bank belongs to and whether the button shows; everything else (source,
// bank, undo) is the wizard's normal machinery.
public partial class EqWizardPanel
{
    // The return address of the running handoff; null when none. Loading any other
    // source ends the session (ApplySource), so the token can never point at a side
    // whose curve is no longer the one on screen.
    private VirtualDspEqReturnToken? virtualDspToken;

    /// <summary>
    /// Raised when the user sends the edited bank back to Virtual DSP. The host owns
    /// what happens next — landing the bank and switching the mode — because the
    /// wizard knows nothing of other panels.
    /// </summary>
    /// <remarks>
    /// The target LEVEL rides along with the bank. It is the user's knob here — the
    /// wizard deliberately never moves it by itself — and the bank's preamp is fitted
    /// against wherever they put it, so returning the filters without the level would
    /// realize a tune aimed at a height the panel does not have.
    /// </remarks>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<VirtualDspEqReturnToken, EqualizationCurve, double>?
        ReturnPeqRequested
    { get; set; }

    /// <summary>
    /// Raised when the user leaves the session WITHOUT applying: the host switches
    /// back to Virtual DSP and nothing is written anywhere — the channel keeps the
    /// PEQ it had, and the wizard keeps the edits (still exportable, still one
    /// Ctrl+Z chain back to the pre-handoff bank).
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action? BackToVirtualDspRequested { get; set; }

    /// <summary>
    /// Installs a channel side sent over by the Virtual DSP tool: its curve becomes
    /// the source, its PEQ seeds the bank (one undo step — Ctrl+Z is the handoff's
    /// cancel), its crossover sets the Auto Tune window, and the Return button
    /// appears. The bank is the wizard's single global one on purpose; the previous
    /// content stays one undo away.
    /// </summary>
    internal void BeginVirtualDspHandoff(VirtualDspEqHandoffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Orphan any source load still in flight, exactly as picking a source from
        // the menu does. A slow file or history read started before the user left for
        // Virtual DSP would otherwise land afterwards, still holding the current
        // generation, and replace the channel that was just handed over — taking the
        // session with it (ApplySource ends one).
        sourceLoadGeneration++;
        // The DSP panel's smoothing first, so the source lands rendered exactly as
        // that plot showed it; the selector then lives its own life.
        SetSourceSmoothing(request.SmoothingInverseOctaves);
        // Ends any previous session on its way in (see ApplySource).
        ApplySource(request.Source);
        ApplyEqualizationCurve(request.BankSeed);
        // A bypassed preview would edit blind, same reasoning as after Auto Tune.
        checkBoxBypass.Checked = false;
        if (request is { AutoTuneMinHz: { } minHz, AutoTuneMaxHz: { } maxHz })
        {
            SetAutoTuneWindow(minHz, maxHz);
        }

        // The handoff curve lives in the SAME dB frame as the Virtual DSP plot (same
        // gate, same calibration), so that panel's target level is valid here
        // verbatim. One target means one height too: the curve must hang exactly
        // where the user just saw it, whatever the wizard's previous offset was —
        // the one case where a source load moves the Target Level, and even then it
        // is only carrying the user's own setting from the panel they came from.
        NumericTargetOffset.Value = NumericTargetOffset.ClampValue(request.TargetLevelDb);

        virtualDspToken = request.Token;
        buttonReturnToDsp.Visible = true;
        buttonBackToDsp.Visible = true;
        RaiseSettingsChanged();
    }

    // Sets the Auto Tune From/To window programmatically: both bounds clamped into
    // the controls' range with the minimum gap kept, written under suppression so the
    // ordinary mutual-push enforcement does not fight the assignment order.
    private void SetAutoTuneWindow(double minHz, double maxHz)
    {
        decimal from = Math.Clamp(
            (decimal)Math.Min(minHz, maxHz),
            numericFromHz.Minimum,
            numericFromHz.Maximum - MinFrequencyGapHz);
        decimal to = Math.Clamp(
            (decimal)Math.Max(minHz, maxHz),
            from + MinFrequencyGapHz,
            numericToHz.Maximum);
        suppressWindowClamp = true;
        try
        {
            numericFromHz.Value = from;
            numericToHz.Value = to;
        }
        finally
        {
            suppressWindowClamp = false;
        }

        OnFrequencyWindowChanged();
    }

    private void ReturnPeqToVirtualDsp()
    {
        if (virtualDspToken is not { } token)
        {
            return;
        }

        // The bank the user can SEE, half-typed field included.
        CommitPendingBankEdit();
        PeqBankState bank = CaptureBankState();
        // The session ends either way: on success the bank now lives in the channel,
        // and on failure the channel is gone and there is nothing left to return to —
        // the host says so, and the wizard keeps the bank for an export instead.
        EndVirtualDspHandoff();
        ReturnPeqRequested?.Invoke(
            token,
            new EqualizationCurve(bank.Bands, bank.PreampDb),
            (double)NumericTargetOffset.Value);
    }

    // Leaves the session without applying: nothing lands on the channel, the wizard
    // keeps its edits, and the host takes the user back to Virtual DSP. Distinct
    // from simply clicking that tab — the tab switch keeps the session (and the
    // Return button) alive for coming back; this one says the editing is over.
    private void BackToVirtualDsp()
    {
        if (virtualDspToken == null)
        {
            return;
        }

        EndVirtualDspHandoff();
        BackToVirtualDspRequested?.Invoke();
    }

    private void EndVirtualDspHandoff()
    {
        virtualDspToken = null;
        buttonReturnToDsp.Visible = false;
        buttonBackToDsp.Visible = false;
    }
}
