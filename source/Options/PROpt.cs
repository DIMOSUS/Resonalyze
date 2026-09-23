using System.Media;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    /// <summary>Binds the Phase settings to a <see cref="GatedAnalysisSettingsSession"/> with its detrend.</summary>
    public partial class PROpt : GatedAnalysisOptionsForm
    {
        private readonly PhaseDetrendEstimate detrendEstimate = new();

        public PROpt()
        {
            InitializeComponent();
            BindGated(
                GatedAnalysisSettingsSession.ForPhase(),
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
            numericOffset.ApplyFieldRange(ModeSettingsLimits.DetrendMs);
            numericOffset.DefaultValue = Session.Defaults.DetrendMs;
            BindIndex(comboDetrendMode, index => Detrend.ModeIndex = index);
            Bind(numericOffset, Detrend.Type);
            buttonTauSlope.Click += (_, _) => TakeEstimate(useSlope: true);
            buttonTauPeak.Click += (_, _) => TakeEstimate(useSlope: false);
            Bind(checkBoxUnwrap, on => Session.Unwrap = on);
            Bind(checkBoxShowMeasured, on => Session.Curves.ShowMeasuredPhase = on);
            Bind(checkBoxShowMinimum, on => Session.Curves.ShowMinimumPhase = on);
            Bind(checkBoxShowExcess, on => Session.Curves.ShowExcessPhase = on);
            InitializeToolTips();
        }

        /// <summary>The refusal sound of the τ buttons; a test listens for it.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Action PlayRefusal { get; set; } = SystemSounds.Beep.Play;

        private PhaseDetrendState Detrend => Session.Detrend!;

        internal void Init(
            AnalyzerDocument document,
            int configuredSampleRate,
            FrequencyResponseOptions opt,
            CurveVisibilityOptions visibility,
            Func<CompareAnalysisSource?>? getCompare = null) =>
            InitGated(document, configuredSampleRate, opt, visibility, getCompare);

        public void SetOptions(FrequencyResponseOptions opt, CurveVisibilityOptions visibility) => WriteGated(opt, visibility);

        private void TakeEstimate(bool useSlope)
        {
            if (Document is not { } document ||
                PhaseDetrendEstimate.Estimate(document, Session.DetrendReading()) is not { } estimate)
            {
                PlayRefusal();
                return;
            }

            Edit(() => Detrend.TakeEstimate(useSlope ? estimate.SlopeMs : estimate.PeakMs));
        }

        // Auto reads τ off the gate, so a moved gate or window moves it.
        private protected override void PresentControls()
        {
            if (Detrend.IsAuto)
            {
                if (Document is not { } document)
                {
                    Detrend.AutoMs = null;
                }
                else if (detrendEstimate.TryResolveAuto(document, Session.DetrendReading(), out double? autoMs))
                {
                    Detrend.AutoMs = autoMs;
                }
            }

            base.PresentControls();
            ShowIndex(comboDetrendMode, Detrend.ModeIndex);
            Show(numericOffset, Detrend.ShownMs);
            numericOffset.Enabled = Detrend.IsManual;
            buttonTauSlope.Enabled = Detrend.IsManual;
            buttonTauPeak.Enabled = Detrend.IsManual;
            checkBoxUnwrap.Checked = Session.Unwrap;
            checkBoxShowMeasured.Checked = Session.Curves.ShowMeasuredPhase;
            checkBoxShowMinimum.Checked = Session.Curves.ShowMinimumPhase;
            checkBoxShowExcess.Checked = Session.Curves.ShowExcessPhase;
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to the phase traces.");
            numericOffset.ApplyToolTip(
                toolTip,
                "τ: linear-phase reference in milliseconds. Auto shows the resolved read-only value here; Manual restores and edits the saved user value; Off shows zero.");
            toolTip.SetToolTip(
                buttonTauSlope,
                "Auto-find τ from the energy-weighted average group delay (flattens the excess-phase trend).");
            toolTip.SetToolTip(
                buttonTauPeak,
                "Auto-find τ from the dominant arrival (bulk delay).");
            toolTip.SetToolTip(
                checkBoxUnwrap,
                "Removes 360-degree phase wraps to display a continuous phase curve.");
            toolTip.SetToolTip(
                checkBoxShowMeasured,
                "Shows the measured phase (orange): the raw response including delay and reflections.");
            toolTip.SetToolTip(
                checkBoxShowMinimum,
                "Shows the minimum phase (cyan): the part tied to the magnitude, correctable with EQ.");
            toolTip.SetToolTip(
                checkBoxShowExcess,
                "Shows the excess phase (green): measured minus minimum — the part EQ cannot fix.");
            toolTip.SetToolTip(
                checkBoxShowCoherence,
                "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the gate window used for phase calculation. FDW phase uses shorter high-frequency windows; the Group Delay tab offers the same choice, and the two read as a pair when their window mode and cycles agree.");
            toolTip.SetToolTip(
                comboWindowMode,
                "Fixed uses one time gate for the entire spectrum. FDW shortens the analysis window as frequency rises to suppress late cabin reflections.");
            toolTip.SetToolTip(
                comboFdwCycles,
                "Periods retained by FDW: 4 suppresses reflections most, 6 is recommended, and 8 retains more reflected detail.");
            toolTip.SetToolTip(
                comboDetrendMode,
                "Removes a constant delay before unwrapping. Auto flattens excess phase, Manual uses the entered delay, and Off keeps the absolute phase slope.");
        }
    }
}
