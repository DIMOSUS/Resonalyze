using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class FROptions : ImpulsePreviewOptionsForm
    {
        // The designer's normal dB SPL text colour, restored when the choice leaves
        // the amber view-only state.
        private readonly Color splChoiceReadyForeColor;

        public FROptions()
        {
            InitializeComponent();
            splChoiceReadyForeColor = radioMagnitudeSpl.ForeColor;
            BindTukeyWindowControls(numericWindow, numericLeftWindow, numericRightWindow);
            comboWindowMode.SelectedIndexChanged +=
                (_, _) => UpdateMagnitudeWindowControlState();
            SmoothingPresetOptions.Configure(
                comboSmoothingInverseOctaves, includePsychoacoustic: true);
            InitializeToolTips();
        }

        internal void Init(
            ExpSweepMeasurement expSweepMeasurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
        {
            AttachMeasurement(expSweepMeasurement);
            InitializeControls(() =>
            {
                comboWindowMode.SelectedIndex =
                    frequencyResponseOptions.MagnitudeWindowMode == PhaseWindowMode.Fixed
                        ? 0
                        : 1;
                comboFdwCycles.SelectedItem =
                    frequencyResponseOptions.MagnitudeFdwCycles is 4 or 6 or 8
                        ? frequencyResponseOptions.MagnitudeFdwCycles
                        : PhaseAnalysisSettings.DefaultFdwCycles;
                numericWindow.Value = frequencyResponseOptions.Window;
                numericLeftWindow.Value = frequencyResponseOptions.LeftTukeyWindow;
                numericRightWindow.Value = frequencyResponseOptions.RightTukeyWindow;
                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(frequencyResponseOptions.SmoothingInverseOctaves);
                MicrophoneCalibrationComboHelper.Configure(
                    comboCalibration,
                    frequencyResponseOptions.CalibrationId,
                    calibrationEntries);
                checkBoxShowPrimary.Checked = visibility.ShowPrimary;
                checkBoxShowCoherence.Checked = visibility.ShowCoherence;
                checkBoxShowHd2.Checked = visibility.ShowHd2;
                checkBoxShowHd3.Checked = visibility.ShowHd3;
                checkBoxShowHd4.Checked = visibility.ShowHd4;
                checkBoxShowThdPlusNoise.Checked = visibility.ShowThdPlusNoise;
                checkBoxShowNoiseFloor.Checked = visibility.ShowNoiseFloor;
                // The selection follows the options verbatim: dB SPL is choosable even
                // without a valid calibration (view-only, amber), so it must not be
                // silently rewritten to relative here.
                UpdateSplChoiceLook();
                bool spl = frequencyResponseOptions.MagnitudeScale ==
                    MagnitudeScale.SoundPressureLevel;
                radioMagnitudeSpl.Checked = spl;
                radioMagnitudeRelative.Checked = !spl;
                RefreshTukeyWindowLimits();
            });
            UpdateMagnitudeWindowControlState();
            UpdateIrPreview();
        }

        /// <summary>
        /// Rebuilds the calibration list without disturbing the selection — the
        /// host calls this when the configured calibrations change while the
        /// panel is open. A selection the list no longer holds stays selected
        /// and marked missing rather than being silently rewritten.
        /// </summary>
        internal void RefreshCalibrationEntries(
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries) =>
            MicrophoneCalibrationComboHelper.Configure(
                comboCalibration,
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboCalibration),
                calibrationEntries);

        public void SetOptions(
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility)
        {
            frequencyResponseOptions.MagnitudeWindowMode = comboWindowMode.SelectedIndex == 0
                ? PhaseWindowMode.Fixed
                : PhaseWindowMode.FrequencyDependent;
            frequencyResponseOptions.MagnitudeFdwCycles =
                comboFdwCycles.SelectedItem is int cycles
                    ? cycles
                    : PhaseAnalysisSettings.DefaultFdwCycles;
            frequencyResponseOptions.Window = (int)numericWindow.Value;
            frequencyResponseOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            frequencyResponseOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            frequencyResponseOptions.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            frequencyResponseOptions.CalibrationId =
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboCalibration);
            visibility.ShowPrimary = checkBoxShowPrimary.Checked;
            visibility.ShowCoherence = checkBoxShowCoherence.Checked;
            visibility.ShowHd2 = checkBoxShowHd2.Checked;
            visibility.ShowHd3 = checkBoxShowHd3.Checked;
            visibility.ShowHd4 = checkBoxShowHd4.Checked;
            visibility.ShowThdPlusNoise = checkBoxShowThdPlusNoise.Checked;
            visibility.ShowNoiseFloor = checkBoxShowNoiseFloor.Checked;
            frequencyResponseOptions.MagnitudeScale = radioMagnitudeSpl.Checked
                ? MagnitudeScale.SoundPressureLevel
                : MagnitudeScale.Relative;
            UpdateIrPreview();
        }

        // The cycles choice only participates in FDW mode; the window fields stay
        // active either way because in FDW mode they define the outer gate that
        // the frequency-dependent windows never exceed.
        private void UpdateMagnitudeWindowControlState() =>
            comboFdwCycles.Enabled = comboWindowMode.SelectedIndex == 1;

        // SPL is offerable exactly when the plot can render it — mirror
        // MeasurementPlotContext.SplOffsetDb: this measurement's own (snapshot)
        // calibration, a captured loopback level, and an input that matches the anchor.
        // Using the snapshot rather than the configured calibration keeps the panel in
        // step with the plot for a completed run and for a loaded file (whose anchor is
        // its own, not the app's currently configured one).
        private bool IsSplAvailable() =>
            Measurement is { } measurement &&
            measurement.MeasurementSplCalibration is { } calibration &&
            measurement.CurrentLevels.Loopback.Available &&
            measurement.InputMatches(calibration);

        /// <summary>
        /// Re-evaluates whether this measurement can supply dB SPL and recolours the
        /// choice accordingly, in both directions, without disturbing the selection:
        /// the scale stays selectable either way and is merely view-only (overlays,
        /// no measurement curves) until a valid calibration and loopback level exist.
        /// The host calls this after every run completion and file load.
        /// </summary>
        public void RefreshSplAvailability() => UpdateSplChoiceLook();

        /// <summary>
        /// Drops the scale selection back to dBr/dBc. The host calls this when a run
        /// starts while the display is view-only SPL, so the fresh measurement is not
        /// born hidden; switching back to SPL afterwards stays available.
        /// </summary>
        public void ForceRelativeScale() => radioMagnitudeRelative.Checked = true;

        // The scale choice is never locked: without a valid SPL calibration the dB SPL
        // axis is still useful for VIEWING overlays captured in SPL, so the choice
        // stays clickable. Amber flags a REAL conflict only — a measurement is on
        // screen whose curves cannot be rendered in SPL, so choosing SPL hides them.
        // Before any measurement there is nothing to hide and nothing to warn about:
        // the choice keeps its normal colour, and with an SPL calibration configured
        // the first run simply comes up in dB SPL.
        private void UpdateSplChoiceLook()
        {
            bool available = IsSplAvailable();
            bool measurementOnScreen = Measurement is { HasImpulseResponse: true };
            bool viewOnlyConflict = !available && measurementOnScreen;
            radioMagnitudeSpl.ForeColor = viewOnlyConflict
                ? UiPalette.WarningAmber
                : splChoiceReadyForeColor;
            toolTip.SetToolTip(radioMagnitudeSpl, DescribeSplChoice(available, viewOnlyConflict));
        }

        private static string DescribeSplChoice(bool available, bool viewOnlyConflict)
        {
            const string Base = "Absolute dB SPL from the microphone SPL calibration.";
            if (available)
            {
                return Base;
            }

            if (viewOnlyConflict)
            {
                return Base + "\r\n" +
                    "View-only: the measurement on screen carries no SPL anchor (it " +
                    "is stamped at run time from the configured calibration plus the " +
                    "run's loopback level), so its curves cannot be shown in dB SPL — " +
                    "only overlays captured in dB SPL are. A new measurement with an " +
                    "SPL calibration configured comes up in dB SPL; starting one " +
                    "without returns the display to dBr/dBc.";
            }

            return Base + "\r\n" +
                "No measurement yet. With an SPL calibration configured in " +
                "Measurement Options, the first run comes up in dB SPL; without one, " +
                "starting a run switches the display back to dBr/dBc. Overlays " +
                "captured in dB SPL are shown either way.";
        }

        protected override void RenderIrPreview()
        {
            if (Measurement == null)
            {
                return;
            }

            ImpulseWindowPreview.Update(
                irPlotView,
                Measurement,
                (int)numericWindow.Value,
                (int)numericLeftWindow.Value,
                (int)numericRightWindow.Value,
                offset: 0,
                // FR magnitude is now windowed on the transfer IR, so preview that window.
                IrPreviewSource.Primary);
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                comboWindowMode,
                "Fixed uses one time window for the entire spectrum (the steady-state " +
                "in-room response). FDW shortens the analysis window as frequency rises " +
                "to suppress late cabin reflections (a quasi-anechoic response).");
            toolTip.SetToolTip(
                comboFdwCycles,
                "Periods retained by FDW: 4 suppresses reflections most, 6 is " +
                "recommended, and 8 retains more reflected detail. The Tukey window " +
                "below remains the outer gate FDW never exceeds.");
            numericWindow.ApplyToolTip(
                toolTip,
                "Sets the FFT window length used to calculate the frequency response.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the main impulse region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the main impulse region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to the resulting frequency-response curve.");
            toolTip.SetToolTip(
                comboCalibration,
                "Applies the selected microphone calibration file to the displayed frequency response.");
            toolTip.SetToolTip(
                labelScale,
                "Vertical scale of the magnitude plot.");
            toolTip.SetToolTip(
                radioMagnitudeRelative,
                "Native scale: the response in dBr (relative to the loopback reference), " +
                "distortion and noise in dBc (relative to the fundamental).");
            // radioMagnitudeSpl's tooltip is owned by UpdateSplChoiceLook: it names
            // the current availability state, which a static line here cannot.
            toolTip.SetToolTip(
                checkBoxShowPrimary,
                "Shows the primary frequency-response curve.");
            toolTip.SetToolTip(
                checkBoxShowCoherence,
                "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
            toolTip.SetToolTip(
                checkBoxShowHd2,
                "Shows the 2nd harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowHd3,
                "Shows the 3rd harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowHd4,
                "Shows the 4th harmonic distortion curve.");
            toolTip.SetToolTip(
                checkBoxShowThdPlusNoise,
                "Shows the total harmonic distortion (THD) curve — harmonics only.");
            toolTip.SetToolTip(
                checkBoxShowNoiseFloor,
                "Shows the measurement noise floor as its own trace; its label states the "
                + "analysis bandwidth the level is measured at.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the transfer impulse response and the analysis window used " +
                "for the primary curve. In FDW mode this window is the outer gate; " +
                "higher frequencies use shorter windows inside it. The harmonic curves " +
                "window the sweep-deconvolution IR with automatically derived windows.");
        }
    }
}
