using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// Manual phase-gate settings for the Virtual DSP phase view, mirroring
/// the Phase mode gate: offset + left/plateau/right Tukey shoulders in
/// milliseconds, with a live preview of every channel's processed impulse
/// response and the window shape, so reflections can be gated out visually.
/// Nothing is committed until Save; the caller reads the properties afterward.
/// </summary>
internal sealed partial class VirtualCrossoverGateDialog : Form
{
    private readonly ToolTip toolTip = new()
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

    /// <summary>
    /// Live preview: fired with the candidate gate values (offset, left,
    /// plateau, right, τ — all ms) on every control change, so the host can
    /// redraw the phase plot immediately. Nothing is committed until Save; the
    /// caller reverts to its stored values on Cancel.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<double, double, double, double, double>? PreviewChanged { get; set; }

    public VirtualCrossoverGateDialog()
    {
        InitializeComponent();
        numericGateOffset.ValueChanged += (_, _) => OnGateChanged();
        numericLeft.ValueChanged += (_, _) => OnGateChanged();
        numericPlateau.ValueChanged += (_, _) => OnGateChanged();
        numericRight.ValueChanged += (_, _) => OnGateChanged();
        numericTau.ValueChanged += (_, _) => OnGateChanged();
        buttonFit.Click += (_, _) =>
            numericGateOffset.Value = Clamp(numericGateOffset, fitOffsetMs);
        buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
        buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
        buttonSave.Click += (_, _) => CommitGateEditors();
        CancelButton = buttonCancel;
        InitializeToolTips();
    }

    public double GateOffsetMs => (double)numericGateOffset.Value;
    public double LeftMs => (double)numericLeft.Value;
    public double PlateauMs => (double)numericPlateau.Value;
    public double RightMs => (double)numericRight.Value;
    public double DetrendMs => (double)numericTau.Value;

    /// <summary>
    /// Seeds the dialog: the processed channel IRs to preview (absolute
    /// timeline), the current gate values, and the offset the Fit button snaps
    /// to (the earliest processed arrival).
    /// </summary>
    public void Init(
        IReadOnlyList<IrPreviewTrace> previewTraces,
        int previewSampleRate,
        double gateOffsetMs,
        double leftMs,
        double plateauMs,
        double rightMs,
        double detrendMs,
        double fitToMs)
    {
        traces = previewTraces;
        sampleRate = previewSampleRate;
        fitOffsetMs = fitToMs;

        numericGateOffset.Value = Clamp(numericGateOffset, gateOffsetMs);
        numericLeft.Value = Clamp(numericLeft, leftMs);
        numericPlateau.Value = Clamp(numericPlateau, plateauMs);
        numericRight.Value = Clamp(numericRight, rightMs);
        numericTau.Value = Clamp(numericTau, detrendMs);

        initialized = true;
        OnGateChanged();
    }

    // Estimates τ with the current gate from the earliest-arriving trace (the
    // one that defines the shared phase reference). Slope flattens the average
    // excess-phase trend; peak references the dominant arrival.
    private void ApplyEstimatedTau(bool useSlope)
    {
        IrPreviewTrace? earliest = traces
            .OrderBy(trace => VirtualCrossoverAnalysis.FindPeakIndex(trace.Samples))
            .FirstOrDefault();
        if (earliest == null || sampleRate <= 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        var view = new ImpulseMeasurementView(
            earliest.Samples,
            VirtualCrossoverAnalysis.FindPeakIndex(earliest.Samples),
            sampleRate);
        (double slopeMs, double peakMs) = DataHelper.EstimatePhaseDetrend(
            view, GateOffsetMs, LeftMs, PlateauMs, RightMs);
        numericTau.Value = Clamp(numericTau, useSlope ? slopeMs : peakMs);
    }

    private void OnGateChanged()
    {
        if (!initialized)
        {
            return;
        }

        UpdateMinFrequencyLabel();
        UpdatePreview();
        PreviewChanged?.Invoke(GateOffsetMs, LeftMs, PlateauMs, RightMs, DetrendMs);
    }

    private void UpdateMinFrequencyLabel()
    {
        double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(
            LeftMs, PlateauMs, RightMs);
        labelMinFrequency.Text = hz > 0
            ? $"Reliable from ≈ {hz:0}+ Hz"
            : "Reliable from ≈ — Hz";
    }

    private void UpdatePreview()
    {
        ImpulseWindowPreview.UpdateGatedMulti(
            irPlotView,
            traces,
            sampleRate,
            GateOffsetMs,
            LeftMs,
            PlateauMs,
            RightMs);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        DarkNumericUpDown? input = keyData == Keys.Enter
            ? GetFocusedGateInput()
            : null;
        if (input != null)
        {
            input.CommitText();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private DarkNumericUpDown? GetFocusedGateInput() =>
        GateInputs().FirstOrDefault(control => control.ContainsFocus);

    private IEnumerable<DarkNumericUpDown> GateInputs()
    {
        yield return numericGateOffset;
        yield return numericLeft;
        yield return numericPlateau;
        yield return numericRight;
        yield return numericTau;
    }

    private void CommitGateEditors()
    {
        foreach (DarkNumericUpDown input in GateInputs())
        {
            input.CommitText();
        }
    }

    private static decimal Clamp(DarkNumericUpDown control, double value)
    {
        decimal candidate = double.IsFinite(value) ? (decimal)value : 0m;
        return Math.Clamp(candidate, control.Minimum, control.Maximum);
    }

    private void InitializeToolTips()
    {
        numericGateOffset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end\r\n" +
            "of the left Tukey shoulder.\r\n" +
            "Use Fit to snap it to the earliest channel arrival.");
        toolTip.SetToolTip(
            buttonFit,
            "Snap the gate offset to the earliest processed\r\n" +
            "channel arrival.");
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
            "and the gate window used for the phase view.");
    }
}
