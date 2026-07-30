using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>
/// Manual gate settings for the Virtual DSP magnitude, phase and impulse
/// views, mirroring the Phase mode gate: offset + left/plateau/right Tukey
/// shoulders in milliseconds, with a live preview of every channel's processed
/// impulse response and the window shape, so reflections can be gated out
/// visually. Nothing is committed until Save; the caller reads the properties
/// afterward.
/// </summary>
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

    /// <summary>
    /// Live preview: fired with the candidate gate values (offset, left,
    /// plateau, right, τ — all ms; plus whether the offset is unpinned) on
    /// every control change, so the host can redraw the gated plots
    /// immediately. The Auto flag must travel with the preview: an unpinned
    /// gate places each curve's window on its own arrival, and the preview has
    /// to show exactly what Save will produce. Nothing is committed until
    /// Save; the caller reverts to its stored values on Cancel.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<double, bool, double, double, double, PhaseWindowMode, int,
        PhaseDetrendMode, double>? PreviewChanged { get; set; }

    public VirtualCrossoverGateDialog()
    {
        InitializeComponent();
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

            // The snap above only fires ValueChanged when the value actually
            // moves; the Auto flag itself changes the gating (per-curve vs
            // pinned), so the preview must always hear about it.
            OnGateChanged();
        };
        buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
        buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
        buttonSave.Click += (_, _) => CommitGateEditors();
        CancelButton = buttonCancel;
        InitializeToolTips();
        // The designer file owns Dispose; the manually created tooltip is not in
        // its components container, so release it here.
        Disposed += (_, _) => toolTip.Dispose();
    }

    public double GateOffsetMs => (double)numericGateOffset.Value;

    /// <summary>
    /// Auto pressed: the offset is not pinned — the caller stores null and the
    /// gate keeps following the earliest estimated channel IR start.
    /// </summary>
    public bool AutoOffset => checkAutoOffset.Checked;
    public double LeftMs => (double)numericLeft.Value;
    public double PlateauMs => (double)numericPlateau.Value;
    public double RightMs => (double)numericRight.Value;
    public double DetrendMs => (double)numericTau.Value;
    public PhaseWindowMode WindowMode => comboWindowMode.SelectedIndex == 0
        ? PhaseWindowMode.Fixed
        : PhaseWindowMode.FrequencyDependent;
    public int FdwCycles => comboFdwCycles.SelectedItem is int cycles
        ? cycles
        : PhaseAnalysisSettings.DefaultFdwCycles;
    public PhaseDetrendMode DetrendMode =>
        Enum.IsDefined((PhaseDetrendMode)comboDetrendMode.SelectedIndex)
            ? (PhaseDetrendMode)comboDetrendMode.SelectedIndex
            : PhaseDetrendMode.Auto;
    /// <summary>
    /// Seeds the dialog: the processed channel IRs to preview (absolute
    /// timeline), the current gate values, the offset Auto snaps to (the
    /// earliest estimated channel IR start) and whether the offset is
    /// currently unpinned (Auto pressed).
    /// </summary>
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
        // After the offset: a false -> true transition re-snaps the value to
        // fitOffsetMs (already seeded) and disables the field; false -> false
        // never fires CheckedChanged, so sync the enabled state explicitly.
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
        var settings = new PhaseAnalysisSettings(
            WindowMode, FdwCycles, PhaseDetrendMode.Auto, DetrendMs,
            GateOffsetMs, LeftMs, PlateauMs, RightMs, Unwrap: false, 0.0);
        (double slopeMs, double peakMs) = DataHelper.EstimatePhaseDetrend(view, settings);
        numericTau.Value = numericTau.ClampValue(useSlope ? slopeMs : peakMs);
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
        PreviewChanged?.Invoke(
            GateOffsetMs, AutoOffset, LeftMs, PlateauMs, RightMs, WindowMode,
            FdwCycles, DetrendMode, DetrendMs);
    }

    private void UpdatePhaseControlState()
    {
        comboFdwCycles.Enabled = WindowMode == PhaseWindowMode.FrequencyDependent;
        bool manual = DetrendMode == PhaseDetrendMode.Manual;
        numericTau.Enabled = manual;
        buttonTauSlope.Enabled = manual;
        buttonTauPeak.Enabled = manual;
        labelAutoDetrend.Text = DetrendMode == PhaseDetrendMode.Auto
            ? ResolveAutoDetrendLabel()
            : string.Empty;
    }

    private string ResolveAutoDetrendLabel()
    {
        IrPreviewTrace? reference = traces
            .OrderBy(trace => VirtualCrossoverAnalysis.FindPeakIndex(trace.Samples))
            .FirstOrDefault();
        if (reference == null || sampleRate <= 0)
        {
            return "Auto detrend: —";
        }

        var view = new ImpulseMeasurementView(
            reference.Samples,
            VirtualCrossoverAnalysis.FindPeakIndex(reference.Samples),
            sampleRate);
        var settings = new PhaseAnalysisSettings(
            WindowMode, FdwCycles, PhaseDetrendMode.Auto, DetrendMs,
            GateOffsetMs, LeftMs, PlateauMs, RightMs, Unwrap: false, 0.0);
        double resolved = DataHelper.ResolveCommonPhaseDetrendMilliseconds(view, settings);
        return $"Auto detrend: {resolved:0.00} ms, reference: {reference.Title}";
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
            "Auto places the gate automatically, following source and\r\n" +
            "delay changes: the magnitude uses ONE window at the earliest\r\n" +
            "arrival (so the Sum stays the exact sum of the drawn curves),\r\n" +
            "while each phase curve is gated at its own arrival (so FDW\r\n" +
            "keeps every channel's treble) with one common time reference.\r\n" +
            "Release to pin one absolute window for everything instead.");
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
