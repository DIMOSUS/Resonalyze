using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Binds the Group Delay settings to a <see cref="GatedAnalysisSettingsSession"/>.</summary>
public partial class GDOpt : GatedAnalysisOptionsForm
{
    public GDOpt()
    {
        InitializeComponent();
        BindGated(
            GatedAnalysisSettingsSession.ForGroupDelay(),
            numericGateOffset,
            checkAutoFit,
            numericLeftWindow,
            numericWindow,
            numericRightWindow,
            labelMinFrequency,
            comboWindowMode,
            comboFdwCycles,
            comboSmoothingInverseOctaves,
            checkBoxShowCoherence,
            irPlotView);
        Bind(checkBoxShowGroupDelay, on => Session.Curves.ShowGroupDelay = on);
        Bind(checkBoxShowMinimumPhaseGroupDelay, on => Session.Curves.ShowMinimumPhaseGroupDelay = on);
        Bind(checkBoxShowExcessGroupDelay, on => Session.Curves.ShowExcessGroupDelay = on);
        InitializeToolTips();
    }

    internal void Init(
        AnalyzerDocument document,
        int configuredSampleRate,
        FrequencyResponseOptions opt,
        CurveVisibilityOptions visibility,
        Func<CompareAnalysisSource?>? getCompare = null) =>
        InitGated(document, configuredSampleRate, opt, visibility, getCompare);

    public void SetOptions(FrequencyResponseOptions opt, CurveVisibilityOptions visibility) => WriteGated(opt, visibility);

    private protected override void PresentControls()
    {
        base.PresentControls();
        checkBoxShowGroupDelay.Checked = Session.Curves.ShowGroupDelay;
        checkBoxShowMinimumPhaseGroupDelay.Checked = Session.Curves.ShowMinimumPhaseGroupDelay;
        checkBoxShowExcessGroupDelay.Checked = Session.Curves.ShowExcessGroupDelay;
    }

    private void InitializeToolTips()
    {
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
