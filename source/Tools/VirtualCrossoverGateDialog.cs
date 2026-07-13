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
    public Action<double, double, double, double, PhaseWindowMode, int,
        PhaseDetrendMode, double, bool>? PreviewChanged { get; set; }

    private readonly DarkComboBox comboWindowMode = new();
    private readonly DarkComboBox comboFdwCycles = new();
    private readonly DarkComboBox comboDetrendMode = new();
    private readonly CheckBox checkBoxUnwrap = new();
    private readonly Label labelAutoDetrend = new();

    public VirtualCrossoverGateDialog()
    {
        InitializeComponent();
        InitializePhaseControls();
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
        // The designer file owns Dispose; the manually created tooltip is not in
        // its components container, so release it here.
        Disposed += (_, _) => toolTip.Dispose();
    }

    public double GateOffsetMs => (double)numericGateOffset.Value;
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
    public bool Unwrap => checkBoxUnwrap.Checked;

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
        PhaseWindowMode windowMode,
        int fdwCycles,
        PhaseDetrendMode detrendMode,
        bool unwrap,
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
        comboWindowMode.SelectedIndex = windowMode == PhaseWindowMode.Fixed ? 0 : 1;
        comboFdwCycles.SelectedItem = fdwCycles is 4 or 6 or 8
            ? fdwCycles
            : PhaseAnalysisSettings.DefaultFdwCycles;
        comboDetrendMode.SelectedIndex = (int)detrendMode;
        checkBoxUnwrap.Checked = unwrap;

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
            GateOffsetMs, LeftMs, PlateauMs, RightMs, Unwrap, 0.0);
        (double slopeMs, double peakMs) = DataHelper.EstimatePhaseDetrend(view, settings);
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
        UpdatePhaseControlState();
        PreviewChanged?.Invoke(
            GateOffsetMs, LeftMs, PlateauMs, RightMs, WindowMode, FdwCycles,
            DetrendMode, DetrendMs, Unwrap);
    }

    private void InitializePhaseControls()
    {
        const int addedHeight = 62;
        irPlotView.Top += addedHeight;
        buttonSave.Top += addedHeight;
        buttonCancel.Top += addedHeight;
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + addedHeight);

        AddCombo("Window", comboWindowMode, 102, 12, 112, 126);
        comboWindowMode.Items.AddRange(["Fixed", "FDW"]);
        AddCombo("FDW cycles", comboFdwCycles, 102, 262, 342, 74);
        comboFdwCycles.Items.AddRange([4, 6, 8]);
        AddCombo("Detrend", comboDetrendMode, 132, 12, 112, 126);
        comboDetrendMode.Items.AddRange(["Off", "Auto", "Manual"]);

        checkBoxUnwrap.AutoSize = true;
        checkBoxUnwrap.ForeColor = Color.White;
        checkBoxUnwrap.Location = new Point(452, 104);
        checkBoxUnwrap.Text = "Unwrap";
        Controls.Add(checkBoxUnwrap);
        labelAutoDetrend.AutoSize = true;
        labelAutoDetrend.ForeColor = Color.FromArgb(210, 214, 222);
        labelAutoDetrend.Location = new Point(262, 134);
        Controls.Add(labelAutoDetrend);

        comboWindowMode.SelectedIndexChanged += (_, _) => OnGateChanged();
        comboFdwCycles.SelectedIndexChanged += (_, _) => OnGateChanged();
        comboDetrendMode.SelectedIndexChanged += (_, _) => OnGateChanged();
        checkBoxUnwrap.CheckedChanged += (_, _) => OnGateChanged();
    }

    private void AddCombo(
        string text,
        DarkComboBox combo,
        int top,
        int labelLeft,
        int controlLeft,
        int width)
    {
        var label = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(210, 214, 222),
            Location = new Point(labelLeft, top + 2),
            Text = text
        };
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Location = new Point(controlLeft, top);
        combo.Size = new Size(width, 19);
        Controls.Add(label);
        Controls.Add(combo);
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
            GateOffsetMs, LeftMs, PlateauMs, RightMs, Unwrap, 0.0);
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

    private static decimal Clamp(DarkNumericUpDown control, double value) =>
        control.ClampValue(value);

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
        toolTip.SetToolTip(comboWindowMode,
            "Fixed uses one gate. FDW shortens the window as frequency rises.");
        toolTip.SetToolTip(comboFdwCycles,
            "4 cycles suppresses reflections most; 6 is recommended; 8 retains more detail.");
        toolTip.SetToolTip(comboDetrendMode,
            "Auto uses one common reference for every curve, preserving relative timing.");
        toolTip.SetToolTip(checkBoxUnwrap,
            "Uses the reliability-aware segmented unwrap and preserves unreliable gaps.");
    }
}
