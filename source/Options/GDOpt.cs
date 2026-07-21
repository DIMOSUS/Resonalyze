using System;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

public partial class GDOpt : ImpulsePreviewOptionsForm
{
    private Func<CompareAnalysisSource?>? getCompare;

    public GDOpt()
    {
        InitializeComponent();

        numericLeftWindow.ValueChanged += Gate_ValueChanged;
        numericRightWindow.ValueChanged += Gate_ValueChanged;
        numericGateOffset.ValueChanged += (_, _) => UpdateIrPreview();
        checkAutoFit.CheckedChanged += (_, _) => AutoFitChanged();
        ConfigureResetDefaults();
        SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
        InitializeToolTips();
    }

    public void Init(
        ExpSweepMeasurement expSweepMeasurement,
        FrequencyResponseOptions opt,
        CurveVisibilityOptions visibility,
        Func<CompareAnalysisSource?>? getCompare = null)
    {
        AttachMeasurement(expSweepMeasurement);
        this.getCompare = getCompare;
        InitializeControls(() =>
        {
            numericGateOffset.Value = ClampToControl(numericGateOffset, opt.GroupDelayGateOffsetMs);
            checkAutoFit.Checked = opt.GroupDelayGateAutoFit;
            numericWindow.Value = ClampToControl(numericWindow, opt.GroupDelayPlateauMs);
            numericLeftWindow.Value = ClampToControl(numericLeftWindow, opt.GroupDelayLeftMs);
            numericRightWindow.Value = ClampToControl(numericRightWindow, opt.GroupDelayRightMs);
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(
                    opt.SmoothingInverseOctaves, includePsychoacoustic: false);
            checkBoxShowGroupDelay.Checked = visibility.ShowGroupDelay;
            checkBoxShowCoherence.Checked = visibility.ShowCoherence;
        });

        UpdateMinFrequencyLabel();
        // CheckedChanged only fires on a transition, so sync the offset
        // field's enabled state for a false -> false init too.
        numericGateOffset.Enabled = !checkAutoFit.Checked;
        UpdateIrPreview();
    }

    public void SetOptions(FrequencyResponseOptions opt, CurveVisibilityOptions visibility)
    {
        opt.GroupDelayGateAutoFit = checkAutoFit.Checked;
        opt.GroupDelayGateOffsetMs = (double)numericGateOffset.Value;
        opt.GroupDelayPlateauMs = (double)numericWindow.Value;
        opt.GroupDelayLeftMs = (double)numericLeftWindow.Value;
        opt.GroupDelayRightMs = (double)numericRightWindow.Value;
        opt.SmoothingInverseOctaves =
            comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                ? inverseOctaves
                : SmoothingPresetOptions.SupportedInverseOctaves[0];
        visibility.ShowGroupDelay = checkBoxShowGroupDelay.Checked;
        visibility.ShowCoherence = checkBoxShowCoherence.Checked;
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

    // Auto pressed: the offset field turns read-only and follows the
    // estimated IR start; released: the field unlocks keeping the last
    // value as the manual starting point.
    private void AutoFitChanged()
    {
        numericGateOffset.Enabled = !checkAutoFit.Checked;
        if (checkAutoFit.Checked)
        {
            ApplyAutoGateOffset();
        }
    }

    // Snaps the gate offset to the estimated IR start (band-limited
    // first-arrival front, memoized per IR in TransferIrStartCache).
    private void ApplyAutoGateOffset()
    {
        if (Measurement is { } measurement &&
            TransferIrStartCache.ResolveStartMs(measurement) is { } startMs)
        {
            numericGateOffset.Value = ClampToControl(numericGateOffset, startMs);
        }
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

    // Re-draws the preview after the Compare selection changes while docked.
    public void RefreshComparePreview() => UpdateIrPreview();

    protected override void RenderIrPreview()
    {
        // Auto mode re-snaps the offset whenever the preview refreshes —
        // which includes every ImpulseResponseChanged of the measurement.
        if (checkAutoFit.Checked)
        {
            ApplyAutoGateOffset();
        }

        if (Measurement == null || Measurement.SampleRate <= 0)
        {
            return;
        }

        ImpulseWindowPreview.UpdateGated(
            irPlotView,
            Measurement,
            (double)numericGateOffset.Value,
            (double)numericLeftWindow.Value,
            (double)numericWindow.Value,
            (double)numericRightWindow.Value,
            IrPreviewSource.Primary,
            getCompare?.Invoke());
    }

    private void InitializeToolTips()
    {
        numericGateOffset.ApplyToolTip(
            toolTip,
            "Gate position: time from the IR start to the end of the left Tukey shoulder. Auto keeps it snapped to the detected IR start.");
        toolTip.SetToolTip(
            checkAutoFit,
            "Keep the gate offset snapped to the detected IR start (band-limited first-arrival front), following every new measurement. Release to set the offset manually.");
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
