using System;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

public partial class GDOpt : Form
{
    private readonly ToolTip toolTip = new();
    private ExpSweepMeasurement? expSweepMeasurement;
    private Func<CompareAnalysisSource?>? getCompare;
    private bool initializing;

    public GDOpt()
    {
        InitializeComponent();

        numericLeftWindow.ValueChanged += Gate_ValueChanged;
        numericRightWindow.ValueChanged += Gate_ValueChanged;
        numericGateOffset.ValueChanged += (_, _) => UpdateIrPreview();
        buttonFit.Click += buttonFit_Click;
        ConfigureResetDefaults();
        SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
        InitializeToolTips();
        // Disposed, not FormClosed: a dialog disposed without ever having been
        // shown (e.g. the docked host closing it while the owner is minimized)
        // never raises FormClosed, which leaked the measurement subscription.
        Disposed += GDOpt_Disposed;
    }

    public void Init(
        ExpSweepMeasurement expSweepMeasurement,
        FrequencyResponseOptions opt,
        Func<CompareAnalysisSource?>? getCompare = null)
    {
        if (!ReferenceEquals(this.expSweepMeasurement, expSweepMeasurement))
        {
            if (this.expSweepMeasurement != null)
            {
                this.expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }

            this.expSweepMeasurement = expSweepMeasurement;
            this.expSweepMeasurement.ImpulseResponseChanged += ExpSweepMeasurement_ImpulseResponseChanged;
        }

        this.getCompare = getCompare;
        initializing = true;
        try
        {
            numericGateOffset.Value = ClampToControl(numericGateOffset, opt.GroupDelayGateOffsetMs);
            numericWindow.Value = ClampToControl(numericWindow, opt.GroupDelayPlateauMs);
            numericLeftWindow.Value = ClampToControl(numericLeftWindow, opt.GroupDelayLeftMs);
            numericRightWindow.Value = ClampToControl(numericRightWindow, opt.GroupDelayRightMs);
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(opt.SmoothingInverseOctaves);
            checkBoxShowGroupDelay.Checked = opt.ShowGroupDelay;
            checkBoxShowCoherence.Checked = opt.ShowCoherence;
        }
        finally
        {
            initializing = false;
        }

        UpdateMinFrequencyLabel();
        UpdateIrPreview();
    }

    public void SetOptions(FrequencyResponseOptions opt)
    {
        opt.GroupDelayGateOffsetMs = (double)numericGateOffset.Value;
        opt.GroupDelayPlateauMs = (double)numericWindow.Value;
        opt.GroupDelayLeftMs = (double)numericLeftWindow.Value;
        opt.GroupDelayRightMs = (double)numericRightWindow.Value;
        opt.SmoothingInverseOctaves =
            comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                ? inverseOctaves
                : SmoothingPresetOptions.SupportedInverseOctaves[0];
        opt.ShowGroupDelay = checkBoxShowGroupDelay.Checked;
        opt.ShowCoherence = checkBoxShowCoherence.Checked;
        UpdateIrPreview();
    }

    // Points each field's "R" reset button at the built-in defaults.
    private void ConfigureResetDefaults()
    {
        var defaults = new FrequencyResponseOptions();
        numericLeftWindow.DefaultValue = (decimal)defaults.GroupDelayLeftMs;
        numericWindow.DefaultValue = (decimal)defaults.GroupDelayPlateauMs;
        numericRightWindow.DefaultValue = (decimal)defaults.GroupDelayRightMs;
        comboSmoothingInverseOctaves.DefaultSelectedItem =
            SmoothingPresetOptions.Normalize(
                FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves);
    }

    // Snap the gate offset to the transfer IR peak (deterministic, gate-independent).
    private void buttonFit_Click(object? sender, EventArgs e)
    {
        if (expSweepMeasurement is not { } measurement ||
            measurement.Transfer is not { ImpulseResponse.Length: > 0 } transfer ||
            measurement.SampleRate <= 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        double onsetMs = transfer.PeakIndex * 1000.0 / measurement.SampleRate;
        numericGateOffset.Value = ClampToControl(numericGateOffset, onsetMs);
    }

    private void numericWindow_ValueChanged(object sender, EventArgs e)
    {
        UpdateMinFrequencyLabel();
        UpdateIrPreview();
    }

    private void Gate_ValueChanged(object? sender, EventArgs e)
    {
        UpdateMinFrequencyLabel();
        UpdateIrPreview();
    }

    private void UpdateMinFrequencyLabel()
    {
        double hz = FrequencyResponseOptions.GateMinReliableFrequencyHz(
            (double)numericLeftWindow.Value,
            (double)numericWindow.Value,
            (double)numericRightWindow.Value);
        labelMinFrequency.Text = hz > 0
            ? $"Reliable from ≈ {hz:0}+ Hz"
            : "Reliable from ≈ — Hz";
    }

    private void ExpSweepMeasurement_ImpulseResponseChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke((MethodInvoker)UpdateIrPreview);
            return;
        }

        UpdateIrPreview();
    }

    private void GDOpt_Disposed(object? sender, EventArgs e)
    {
        if (expSweepMeasurement != null)
        {
            expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
        }

        toolTip.Dispose();
    }

    // Re-draws the preview after the Compare selection changes while docked.
    public void RefreshComparePreview() => UpdateIrPreview();

    private void UpdateIrPreview()
    {
        if (initializing || expSweepMeasurement == null || expSweepMeasurement.SampleRate <= 0)
        {
            return;
        }

        ImpulseWindowPreview.UpdateGated(
            irPlotView,
            expSweepMeasurement,
            (double)numericGateOffset.Value,
            (double)numericLeftWindow.Value,
            (double)numericWindow.Value,
            (double)numericRightWindow.Value,
            IrPreviewSource.Primary,
            getCompare?.Invoke());
    }

    private static decimal ClampToControl(DarkNumericUpDown control, double value)
    {
        decimal candidate = double.IsFinite(value) ? (decimal)value : 0m;
        return Math.Clamp(candidate, control.Minimum, control.Maximum);
    }

    private void InitializeToolTips()
    {
        numericGateOffset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end of the left Tukey shoulder. Use Fit to snap it to the transfer IR peak.");
        toolTip.SetToolTip(
            buttonFit,
            "Snap the gate offset to the transfer IR peak.");
        numericWindow.ApplyToolTip(
            toolTip,
            "Flat (weight 1) part of the gate after the peak, in milliseconds.");
        numericLeftWindow.ApplyToolTip(
            toolTip,
            "Tukey fade-in before the peak, in milliseconds. Keep short.");
        numericRightWindow.ApplyToolTip(
            toolTip,
            "Tukey fade-out gate after the plateau, in milliseconds. End it before the first reflection.");
        toolTip.SetToolTip(
            comboSmoothingInverseOctaves,
            "Applies octave smoothing to the resulting Group Delay curve.");
        toolTip.SetToolTip(
            checkBoxShowGroupDelay,
            "Shows the group-delay curve.");
        toolTip.SetToolTip(
            checkBoxShowCoherence,
            "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
        toolTip.SetToolTip(
            labelMinFrequency,
            "Lowest frequency the current gate can resolve (≈ 1 / gate length). Below it the curve is not reliable.");
        toolTip.SetToolTip(
            irPlotView,
            "Preview of the IR used for Group Delay together with the current gate window.");
    }
}
