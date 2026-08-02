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

        BindGateControls(
            numericGateOffset,
            checkAutoFit,
            numericLeftWindow,
            numericWindow,
            numericRightWindow,
            labelMinFrequency);
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
            numericGateOffset.Value = numericGateOffset.ClampValue(opt.GroupDelayGateOffsetMs);
            checkAutoFit.Checked = opt.GroupDelayGateAutoFit;
            numericWindow.Value = numericWindow.ClampValue(opt.GroupDelayPlateauMs);
            numericLeftWindow.Value = numericLeftWindow.ClampValue(opt.GroupDelayLeftMs);
            numericRightWindow.Value = numericRightWindow.ClampValue(opt.GroupDelayRightMs);
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(
                    opt.SmoothingInverseOctaves, includePsychoacoustic: false);
            checkBoxShowGroupDelay.Checked = visibility.ShowGroupDelay;
            checkBoxShowMinimumPhaseGroupDelay.Checked =
                visibility.ShowMinimumPhaseGroupDelay;
            checkBoxShowExcessGroupDelay.Checked = visibility.ShowExcessGroupDelay;
            checkBoxShowCoherence.Checked = visibility.ShowCoherence;
        });

        UpdateMinFrequencyLabel();
        SyncGateOffsetEnabled();
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
        visibility.ShowMinimumPhaseGroupDelay =
            checkBoxShowMinimumPhaseGroupDelay.Checked;
        visibility.ShowExcessGroupDelay = checkBoxShowExcessGroupDelay.Checked;
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

    protected override void RenderIrPreview() =>
        RenderGatedIrPreview(irPlotView, getCompare?.Invoke());

    private void InitializeToolTips()
    {
        ApplyGateToolTips();
        toolTip.SetToolTip(
            comboSmoothingInverseOctaves,
            "Applies octave smoothing to the resulting Group Delay curve.");
        toolTip.SetToolTip(
            checkBoxShowGroupDelay,
            "Shows the group-delay curve.");
        toolTip.SetToolTip(
            checkBoxShowMinimumPhaseGroupDelay,
            "Shows the minimum-phase group delay implied by the gated magnitude " +
            "response alone (Bode relation) — the part a minimum-phase EQ could correct.");
        toolTip.SetToolTip(
            checkBoxShowExcessGroupDelay,
            "Shows measured minus minimum-phase group delay: the all-pass remainder " +
            "(bulk delay, crossovers, reflections) that magnitude EQ cannot move. " +
            "A flat excess curve is a pure delay.");
        toolTip.SetToolTip(
            checkBoxShowCoherence,
            "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
        toolTip.SetToolTip(
            irPlotView,
            "Preview of the IR used for Group Delay together with the current gate window.");
    }
}
