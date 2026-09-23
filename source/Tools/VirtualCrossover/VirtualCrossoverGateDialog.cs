using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>Manual Tukey gate for the Virtual DSP views with a live IR preview; nothing is committed until Save.</summary>
internal sealed partial class VirtualCrossoverGateDialog : Form
{
    private readonly WrappingToolTip toolTip = new()
    {
        InitialDelay = 500,
        ReshowDelay = 150,
        AutoPopDelay = 12_000,
        ShowAlways = true
    };

    private IReadOnlyList<IrPreviewTrace> traces = Array.Empty<IrPreviewTrace>();
    private int sampleRate;
    private double fitOffsetMs;
    private bool initialized;

    /// <summary>Live preview on every change (the Auto flag too, since an unpinned gate follows each curve's own arrival).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<VirtualCrossoverGatePreview>? PreviewChanged { get; set; }

    public VirtualCrossoverGateDialog()
    {
        InitializeComponent();
        PlotInteraction.Enable(irPlotView);
        numericGateOffset.ValueChanged += (_, _) => OnGateChanged();
        numericLeft.ValueChanged += (_, _) => OnGateChanged();
        numericPlateau.ValueChanged += (_, _) => OnGateChanged();
        numericRight.ValueChanged += (_, _) => OnGateChanged();
        numericTau.ValueChanged += (_, _) => OnGateChanged();
        comboWindowMode.SelectedIndexChanged += (_, _) => OnGateChanged();
        comboFdwCycles.SelectedIndexChanged += (_, _) => OnGateChanged();
        comboDetrendMode.SelectedIndexChanged += (_, _) => OnGateChanged();
        checkAutoOffset.CheckedChanged += (_, _) =>
        {
            numericGateOffset.Enabled = !checkAutoOffset.Checked;
            if (checkAutoOffset.Checked)
            {
                numericGateOffset.Value = numericGateOffset.ClampValue(fitOffsetMs);
            }

            // ValueChanged fires only when the snap moves the value, but Auto itself changes the gating.
            OnGateChanged();
        };
        buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
        buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
        buttonSave.Click += (_, _) => CommitGateEditors();
        CancelButton = buttonCancel;
        InitializeToolTips();
        // The tooltip is not in the designer's components container.
        Disposed += (_, _) => toolTip.Dispose();
    }

    /// <summary>The gate as the fields state it; an Auto offset is stored unpinned and follows the earliest channel IR start.</summary>
    public VirtualCrossoverGatePreview Gate => new(
        (double)numericGateOffset.Value,
        checkAutoOffset.Checked,
        (double)numericLeft.Value,
        (double)numericPlateau.Value,
        (double)numericRight.Value,
        comboWindowMode.SelectedIndex == 0 ? PhaseWindowMode.Fixed : PhaseWindowMode.FrequencyDependent,
        comboFdwCycles.SelectedItem is int cycles ? cycles : PhaseAnalysisSettings.DefaultFdwCycles,
        Enum.IsDefined((PhaseDetrendMode)comboDetrendMode.SelectedIndex)
            ? (PhaseDetrendMode)comboDetrendMode.SelectedIndex
            : PhaseDetrendMode.Auto,
        (double)numericTau.Value);

    public void Init(
        IReadOnlyList<IrPreviewTrace> previewTraces,
        int previewSampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        double detrendMs,
        PhaseWindowMode windowMode,
        int fdwCycles,
        PhaseDetrendMode detrendMode,
        double fitToMs,
        bool autoOffset)
    {
        traces = previewTraces;
        sampleRate = previewSampleRate;
        fitOffsetMs = fitToMs;

        numericGateOffset.Value = numericGateOffset.ClampValue(gateOffsetMs);
        // false -> false never fires CheckedChanged, so the enabled state is synced explicitly.
        checkAutoOffset.Checked = autoOffset;
        numericGateOffset.Enabled = !autoOffset;
        numericLeft.Value = numericLeft.ClampValue(leftMs);
        numericPlateau.Value = numericPlateau.ClampValue(plateauMs);
        numericRight.Value = numericRight.ClampValue(rightMs);
        numericTau.Value = numericTau.ClampValue(detrendMs);
        comboWindowMode.SelectedIndex = windowMode == PhaseWindowMode.Fixed ? 0 : 1;
        comboFdwCycles.SelectedItem = fdwCycles is 4 or 6 or 8
            ? fdwCycles
            : PhaseAnalysisSettings.DefaultFdwCycles;
        comboDetrendMode.SelectedIndex = (int)detrendMode;
        initialized = true;
        OnGateChanged();
    }

    private void ApplyEstimatedTau(bool useSlope)
    {
        if (VirtualCrossoverGateEstimate.Tau(traces, sampleRate, Gate) is not { } tau)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        numericTau.Value = numericTau.ClampValue(useSlope ? tau.SlopeMs : tau.PeakMs);
    }

    private void OnGateChanged()
    {
        if (!initialized)
        {
            return;
        }

        UpdateMinFrequencyLabel();
        UpdatePreview();
        UpdatePhaseControlState();
        PreviewChanged?.Invoke(Gate);
    }

    private void UpdatePhaseControlState()
    {
        VirtualCrossoverGatePreview gate = Gate;
        comboFdwCycles.Enabled = gate.WindowMode == PhaseWindowMode.FrequencyDependent;
        bool manual = gate.DetrendMode == PhaseDetrendMode.Manual;
        numericTau.Enabled = manual;
        buttonTauSlope.Enabled = manual;
        buttonTauPeak.Enabled = manual;
        labelAutoDetrend.Text = gate.DetrendMode == PhaseDetrendMode.Auto
            ? VirtualCrossoverGateEstimate.AutoDetrendLabel(traces, sampleRate, gate)
            : string.Empty;
    }

    private void UpdateMinFrequencyLabel()
    {
        VirtualCrossoverGatePreview gate = Gate;
        labelMinFrequency.Text = GateReadout.ReliableFrom(gate.LeftMs, gate.PlateauMs, gate.RightMs);
    }

    private void UpdatePreview()
    {
        VirtualCrossoverGatePreview gate = Gate;
        ImpulseWindowPreview.UpdateGatedMulti(
            irPlotView,
            traces,
            sampleRate,
            gate.OffsetMs,
            gate.LeftMs,
            gate.PlateauMs,
            gate.RightMs);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        ThemedNumericUpDown? input = keyData == Keys.Enter
            ? GetFocusedGateInput()
            : null;
        if (input != null)
        {
            input.CommitText();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private ThemedNumericUpDown? GetFocusedGateInput() =>
        GateInputs().FirstOrDefault(control => control.ContainsFocus);

    private IEnumerable<ThemedNumericUpDown> GateInputs()
    {
        yield return numericGateOffset;
        yield return numericLeft;
        yield return numericPlateau;
        yield return numericRight;
        yield return numericTau;
    }

    private void CommitGateEditors()
    {
        foreach (ThemedNumericUpDown input in GateInputs())
        {
            input.CommitText();
        }
    }

    private void InitializeToolTips()
    {
        numericGateOffset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end\r\n" +
            "of the left Tukey shoulder. Pinned, it is one absolute\r\n" +
            "window for every curve; the field shows the earliest\r\n" +
            "channel's placement while Auto is pressed.");
        toolTip.SetToolTip(
            checkAutoOffset,
            "Auto places the gate itself and follows source and delay\r\n" +
            "changes: one window for the magnitude, each phase curve at\r\n" +
            "its own arrival. Release to pin one absolute window.");
        numericLeft.ApplyToolTip(
            toolTip,
            "Tukey fade-in before the arrival, in milliseconds.\r\n" +
            "Keep short.");
        numericPlateau.ApplyToolTip(
            toolTip,
            "Flat (weight 1) part of the gate after the arrival,\r\n" +
            "in milliseconds. Long enough to include every\r\n" +
            "channel's arrival plus its delay.");
        numericRight.ApplyToolTip(
            toolTip,
            "Tukey fade-out after the plateau, in milliseconds.\r\n" +
            "End it before the first reflection.");
        numericTau.ApplyToolTip(
            toolTip,
            "τ: one linear-phase reference (delay, ms from the IR start)\r\n" +
            "removed from every channel and the sum alike.\r\n" +
            "Flattens the traces while preserving their relative phase.");
        toolTip.SetToolTip(
            buttonTauSlope,
            "Auto-find τ from the energy-weighted average group delay\r\n" +
            "of the earliest channel (flattens the excess-phase trend).");
        toolTip.SetToolTip(
            buttonTauPeak,
            "Auto-find τ from the dominant arrival of the earliest channel\r\n" +
            "(bulk delay).");
        toolTip.SetToolTip(
            labelMinFrequency,
            "Lowest frequency the current gate can resolve\r\n" +
            "(≈ 1 / gate length).\r\n" +
            "Below it the phase traces are not reliable.");
        toolTip.SetToolTip(
            irPlotView,
            "Preview of every channel's processed impulse response\r\n" +
            "and the gate window used for the magnitude and phase views.");
        toolTip.SetToolTip(comboWindowMode,
            "Fixed uses one gate. FDW shortens the window as frequency\r\n" +
            "rises — it shapes the PHASE view only; the magnitude always\r\n" +
            "reads the fixed gate (no single frequency-dependent window\r\n" +
            "can hold the summed response's spread arrivals).");
        toolTip.SetToolTip(comboFdwCycles,
            "4 cycles suppresses reflections most; 6 is recommended; 8 retains more detail.");
        toolTip.SetToolTip(comboDetrendMode,
            "Auto uses one common reference for every curve, preserving relative timing.");
    }
}
