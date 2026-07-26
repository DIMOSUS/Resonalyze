using System;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class PROpt : ImpulsePreviewOptionsForm
    {
        private Func<CompareAnalysisSource?>? getCompare;
        private double manualDetrendMilliseconds;
        private bool updatingDetrendDisplay;

        public PROpt()
        {
            InitializeComponent();
            comboWindowMode.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
            comboFdwCycles.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
            comboDetrendMode.SelectedIndexChanged += (_, _) => UpdatePhaseControlState();
            BindGateControls(
                numericGateOffset,
                checkAutoFit,
                numericLeftWindow,
                numericWindow,
                numericRightWindow,
                labelMinFrequency);
            buttonTauSlope.Click += (_, _) => ApplyEstimatedTau(useSlope: true);
            buttonTauPeak.Click += (_, _) => ApplyEstimatedTau(useSlope: false);
            numericOffset.ValueChanged += (_, _) =>
            {
                if (!updatingDetrendDisplay &&
                    comboDetrendMode.SelectedIndex == (int)PhaseDetrendMode.Manual)
                {
                    manualDetrendMilliseconds = (double)numericOffset.Value;
                }
            };
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
            manualDetrendMilliseconds = opt.PhaseDetrendMs;
            InitializeControls(() =>
            {
                numericGateOffset.Value = numericGateOffset.ClampValue(opt.PhaseGateOffsetMs);
                checkAutoFit.Checked = opt.PhaseGateAutoFit;
                numericWindow.Value = numericWindow.ClampValue(opt.PhasePlateauMs);
                numericLeftWindow.Value = numericLeftWindow.ClampValue(opt.PhaseLeftMs);
                numericRightWindow.Value = numericRightWindow.ClampValue(opt.PhaseRightMs);
                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(
                    opt.SmoothingInverseOctaves, includePsychoacoustic: false);
                numericOffset.Value = numericOffset.ClampValue(opt.PhaseDetrendMs);
                comboWindowMode.SelectedIndex = opt.PhaseWindowMode == PhaseWindowMode.Fixed ? 0 : 1;
                comboFdwCycles.SelectedItem = opt.PhaseFdwCycles is 4 or 6 or 8
                    ? opt.PhaseFdwCycles
                    : PhaseAnalysisSettings.DefaultFdwCycles;
                comboDetrendMode.SelectedIndex = (int)opt.PhaseDetrendMode;
                checkBoxUnwrap.Checked = opt.Unwrap;
                checkBoxShowMeasured.Checked = visibility.ShowMeasuredPhase;
                checkBoxShowMinimum.Checked = visibility.ShowMinimumPhase;
                checkBoxShowExcess.Checked = visibility.ShowExcessPhase;
                checkBoxShowCoherence.Checked = visibility.ShowCoherence;
            });
            UpdateMinFrequencyLabel();
            UpdatePhaseControlState();
            SyncGateOffsetEnabled();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions opt, CurveVisibilityOptions visibility)
        {
            opt.PhaseGateAutoFit = checkAutoFit.Checked;
            opt.PhaseGateOffsetMs = (double)numericGateOffset.Value;
            opt.PhasePlateauMs = (double)numericWindow.Value;
            opt.PhaseLeftMs = (double)numericLeftWindow.Value;
            opt.PhaseRightMs = (double)numericRightWindow.Value;
            opt.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            opt.PhaseDetrendMs = manualDetrendMilliseconds;
            opt.PhaseWindowMode = comboWindowMode.SelectedIndex == 0
                ? PhaseWindowMode.Fixed
                : PhaseWindowMode.FrequencyDependent;
            opt.PhaseFdwCycles = comboFdwCycles.SelectedItem is int cycles
                ? cycles
                : PhaseAnalysisSettings.DefaultFdwCycles;
            opt.PhaseDetrendMode = Enum.IsDefined(
                (PhaseDetrendMode)comboDetrendMode.SelectedIndex)
                    ? (PhaseDetrendMode)comboDetrendMode.SelectedIndex
                    : PhaseDetrendMode.Auto;
            opt.Unwrap = checkBoxUnwrap.Checked;
            visibility.ShowMeasuredPhase = checkBoxShowMeasured.Checked;
            visibility.ShowMinimumPhase = checkBoxShowMinimum.Checked;
            visibility.ShowExcessPhase = checkBoxShowExcess.Checked;
            visibility.ShowCoherence = checkBoxShowCoherence.Checked;
            UpdateIrPreview();
        }

        private void UpdatePhaseControlState()
        {
            comboFdwCycles.Enabled = comboWindowMode.SelectedIndex == 1;
            bool manual = comboDetrendMode.SelectedIndex == (int)PhaseDetrendMode.Manual;
            numericOffset.Enabled = manual;
            buttonTauSlope.Enabled = manual;
            buttonTauPeak.Enabled = manual;
            UpdateDetrendDisplay();
        }

        private void UpdateDetrendDisplay()
        {
            double displayed = comboDetrendMode.SelectedIndex switch
            {
                (int)PhaseDetrendMode.Off => 0.0,
                (int)PhaseDetrendMode.Auto when TryResolveAutoDetrend(out double resolved) =>
                    resolved,
                (int)PhaseDetrendMode.Manual => manualDetrendMilliseconds,
                _ => 0.0
            };

            updatingDetrendDisplay = true;
            try
            {
                numericOffset.Value = numericOffset.ClampValue(displayed);
            }
            finally
            {
                updatingDetrendDisplay = false;
            }
        }

        private bool TryResolveAutoDetrend(out double resolved)
        {
            resolved = 0.0;
            try
            {
                if (Measurement is not { } measurement ||
                    measurement.TransferImpulseResponse is not { Length: > 0 })
                {
                    return false;
                }
                IImpulseMeasurement impulse =
                    new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
                PhaseAnalysisSettings settings = CreateCurrentPhaseAnalysisSettings(
                    PhaseDetrendMode.Auto);
                resolved = DataHelper.ResolvePhaseDetrendMilliseconds(impulse, settings);
                return double.IsFinite(resolved);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private PhaseAnalysisSettings CreateCurrentPhaseAnalysisSettings(
            PhaseDetrendMode detrendMode) => new(
                comboWindowMode.SelectedIndex == 0
                    ? PhaseWindowMode.Fixed
                    : PhaseWindowMode.FrequencyDependent,
                comboFdwCycles.SelectedItem is int cycles
                    ? cycles
                    : PhaseAnalysisSettings.DefaultFdwCycles,
                detrendMode,
                manualDetrendMilliseconds,
                (double)numericGateOffset.Value,
                (double)numericLeftWindow.Value,
                (double)numericWindow.Value,
                (double)numericRightWindow.Value,
                checkBoxUnwrap.Checked,
                comboSmoothingInverseOctaves.SelectedItem is int smoothing
                    ? smoothing
                    : FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves);

        internal (double SlopeMilliseconds, double PeakMilliseconds)
            EstimateCurrentPhaseDetrend(IImpulseMeasurement impulse) =>
                DataHelper.EstimatePhaseDetrend(
                    impulse,
                    CreateCurrentPhaseAnalysisSettings(PhaseDetrendMode.Auto));

        // Points each field's "R" reset button at the built-in default values.
        private void ConfigureResetDefaults()
        {
            var defaults = new FrequencyResponseOptions();
            numericLeftWindow.DefaultValue = (decimal)defaults.PhaseLeftMs;
            numericWindow.DefaultValue = (decimal)defaults.PhasePlateauMs;
            numericRightWindow.DefaultValue = (decimal)defaults.PhaseRightMs;
            numericOffset.DefaultValue = (decimal)defaults.PhaseDetrendMs;
            comboSmoothingInverseOctaves.DefaultSelectedItem =
                SmoothingPresetOptions.Normalize(
                    FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves);
        }

        // Estimates τ from the current gate and writes it into the τ field. Slope flattens
        // the average excess-phase trend; peak references the dominant arrival.
        private void ApplyEstimatedTau(bool useSlope)
        {
            // Phase analysis (and therefore τ) only works with a transfer IR, so
            // gate the auto-estimate on the same condition as Fit and the plot.
            if (Measurement is not { } measurement ||
                measurement.TransferImpulseResponse is not { Length: > 0 } ||
                measurement.InProgress)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                IImpulseMeasurement impulse =
                    new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
                (double slopeMs, double peakMs) = EstimateCurrentPhaseDetrend(impulse);
                numericOffset.Value = numericOffset.ClampValue(
                    useSlope ? slopeMs : peakMs);
                manualDetrendMilliseconds = (double)numericOffset.Value;
            }
            catch (InvalidOperationException)
            {
                System.Media.SystemSounds.Beep.Play();
            }
        }

        // Auto detrend resolves tau from the gate, so the read-only tau display
        // has to be recomputed whenever the gate moves.
        protected override void OnGatePreviewRendering()
        {
            if (comboDetrendMode.SelectedIndex == (int)PhaseDetrendMode.Auto)
            {
                UpdateDetrendDisplay();
            }
        }

        protected override void RenderIrPreview() =>
            RenderGatedIrPreview(irPlotView, getCompare?.Invoke());

        private void InitializeToolTips()
        {
            ApplyGateToolTips();
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
                "Preview of the impulse response and the gate window used for phase calculation. FDW phase uses shorter high-frequency windows; Group Delay remains fixed-gate and is not its exact integral.");
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
