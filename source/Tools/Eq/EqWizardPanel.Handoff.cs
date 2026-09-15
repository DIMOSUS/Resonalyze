using Resonalyze.Dsp;

namespace Resonalyze;

// Virtual DSP handoff session: only bookkeeping (which side the bank belongs to, the Return button) over normal wizard machinery.
public partial class EqWizardPanel
{
    // Any other source load ends the session (ApplySource), so the token never points at a curve no longer on screen.
    private VirtualDspEqReturnToken? virtualDspToken;

    // Captured from the control before a handoff narrows it, so the designer stays the single source of the range.
    private decimal? defaultTargetOffsetMinimum;
    private decimal? defaultTargetOffsetMaximum;

    private decimal DefaultTargetOffsetMinimum =>
        defaultTargetOffsetMinimum ??= NumericTargetOffset.Minimum;

    private decimal DefaultTargetOffsetMaximum =>
        defaultTargetOffsetMaximum ??= NumericTargetOffset.Maximum;

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
        // Read before narrowing, so the wizard's own range is restored at the end.
        _ = DefaultTargetOffsetMinimum;
        _ = DefaultTargetOffsetMaximum;
        sourceLoadGeneration++;
        // The panel's smoothing first, so the source lands as that plot showed it.
        SetSourceSmoothing(request.SmoothingInverseOctaves);
        ApplySource(request.Source);
        ApplyEqualizationCurve(request.BankSeed);
        checkBoxBypass.Checked = false;
        if (request is { AutoTuneMinHz: { } minHz, AutoTuneMaxHz: { } maxHz })
        {
            SetAutoTuneWindow(minHz, maxHz);
        }

        // Same dB frame as the Virtual DSP plot, so its target level applies verbatim (the one case a load moves it).
        // Narrowed to the panel's range because the level travels back and would be silently clamped there.
        NumericTargetOffset.Minimum = (decimal)request.TargetLevelMinDb;
        NumericTargetOffset.Maximum = (decimal)request.TargetLevelMaxDb;
        NumericTargetOffset.Value = NumericTargetOffset.ClampValue(request.TargetLevelDb);

        virtualDspToken = request.Token;
        buttonReturnToDsp.Visible = true;
        buttonBackToDsp.Visible = true;
        RaiseSettingsChanged();
    }

    // Written under suppression so the mutual-push bound enforcement does not fight the assignment order.
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

        CommitPendingBankEdit();
        PeqBankState bank = CaptureBankState();
        // Ends either way: on failure the channel is gone; the wizard keeps the bank for export.
        EndVirtualDspHandoff();
        ReturnPeqRequested?.Invoke(
            token,
            new EqualizationCurve(bank.Bands, bank.PreampDb),
            (double)NumericTargetOffset.Value);
    }

    // Unlike a tab switch, which keeps the session and Return button alive.
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
        NumericTargetOffset.Minimum = DefaultTargetOffsetMinimum;
        NumericTargetOffset.Maximum = DefaultTargetOffsetMaximum;
        virtualDspToken = null;
        buttonReturnToDsp.Visible = false;
        buttonBackToDsp.Visible = false;
    }
}
