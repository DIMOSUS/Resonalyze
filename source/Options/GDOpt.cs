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
        comboWindowMode.SelectedIndexChanged += (_, _) => UpdateWindowControlState();

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
            comboWindowMode.SelectedIndex =
                opt.GroupDelayWindowMode == PhaseWindowMode.Fixed ? 0 : 1;
            comboFdwCycles.SelectedItem = opt.GroupDelayFdwCycles is 4 or 6 or 8
                ? opt.GroupDelayFdwCycles
                : PhaseAnalysisSettings.DefaultFdwCycles;
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
        UpdateWindowControlState();
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
        opt.GroupDelayWindowMode = comboWindowMode.SelectedIndex == 0
            ? PhaseWindowMode.Fixed
            : PhaseWindowMode.FrequencyDependent;
        opt.GroupDelayFdwCycles = comboFdwCycles.SelectedItem is int cycles
            ? cycles
            : PhaseAnalysisSettings.DefaultFdwCycles;
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

    // The cycle count only means something under FDW.
    private void UpdateWindowControlState() =>
        comboFdwCycles.Enabled = comboWindowMode.SelectedIndex == 1;

    internal bool FdwCyclesEnabled => comboFdwCycles.Enabled;

    // Points each field's "R" reset button at the built-in defaults.
    private void ConfigureResetDefaults()
    {
        var defaults = new FrequencyResponseOptions();
        comboWindowMode.DefaultSelectedItem =
            defaults.GroupDelayWindowMode == PhaseWindowMode.Fixed ? "Fixed" : "FDW";
        comboFdwCycles.DefaultSelectedItem = defaults.GroupDelayFdwCycles;
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
            comboWindowMode,
            "Fixed reads through one time gate for the entire spectrum. FDW shortens " +
            "the window as frequency rises, so the treble reads the direct arrival " +
            "without the late reflections. With the Phase tab's gate, mode and cycles the two read as a pair.");
        toolTip.SetToolTip(
            comboFdwCycles,
            "Periods kept by FDW after the gate offset: 4 suppresses reflections most, " +
            "6 is recommended, and 8 retains more reflected detail. The window sets " +
            "the resolution, so finer smoothing changes little where FDW takes over.");
        toolTip.SetToolTip(
            irPlotView,
            "Preview of the IR used for Group Delay together with the current gate " +
            "window. Under FDW the gate is the outer limit of a window that shortens with frequency.");
    }
}
