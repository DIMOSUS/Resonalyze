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
            AnalyzerDocument document,
            int configuredSampleRate,
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries)
        {
            AttachMeasurement(document, configuredSampleRate);
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
                checkBoxShowArrayAverage.Checked = visibility.ShowArrayAverage;
                checkBoxShowArrayMicrophones.Checked = visibility.ShowArrayMicrophones;
                checkBoxShowArraySpread.Checked = visibility.ShowArraySpread;
                checkBoxShowHd2.Checked = visibility.ShowHd2;
                checkBoxShowHd3.Checked = visibility.ShowHd3;
                checkBoxShowHd4.Checked = visibility.ShowHd4;
                checkBoxShowThdPlusNoise.Checked = visibility.ShowThdPlusNoise;
                checkBoxShowNoiseFloor.Checked = visibility.ShowNoiseFloor;
                // dB SPL stays selected without a calibration (view-only, amber); do not rewrite to relative.
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

        /// <summary>A selection the list no longer holds stays selected and marked missing.</summary>
        internal void RefreshCalibrationEntries(
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries) =>
            SelectCalibration(
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboCalibration),
                calibrationEntries);

        /// <summary>Shows the calibration a loaded measurement carries.</summary>
        internal void SelectCalibration(
            string? calibrationId,
            IReadOnlyList<MicrophoneCalibrationEntry> calibrationEntries) =>
            MicrophoneCalibrationComboHelper.Configure(
                comboCalibration,
                calibrationId,
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
            visibility.ShowArrayAverage = checkBoxShowArrayAverage.Checked;
            visibility.ShowArrayMicrophones = checkBoxShowArrayMicrophones.Checked;
            visibility.ShowArraySpread = checkBoxShowArraySpread.Checked;
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

        // In FDW mode the window fields still define the outer gate.
        private void UpdateMagnitudeWindowControlState() =>
            comboFdwCycles.Enabled = comboWindowMode.SelectedIndex == 1;

        // Mirrors MeasurementPlotContext.SplOffsetDb: the result's own anchor (loaded files carry theirs).
        private bool IsSplAvailable() => Measurement?.SplOffsetDb != null;

        /// <summary>Called after every run and file load; recolours without changing the selection.</summary>
        public void RefreshSplAvailability() => UpdateSplChoiceLook();

        /// <summary>Called when a run starts in view-only SPL, so the fresh measurement is not born hidden.</summary>
        public void ForceRelativeScale() => radioMagnitudeRelative.Checked = true;

        // Never locked (SPL axis still shows SPL overlays). Amber only when a measurement on screen cannot render in SPL.
        private void UpdateSplChoiceLook()
        {
            bool available = IsSplAvailable();
            bool measurementOnScreen = Measurement != null;
            bool viewOnlyConflict = !available && measurementOnScreen;
            radioMagnitudeSpl.ForeColor = viewOnlyConflict
                ? UiPalette.Warning
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
            if (Document == null)
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
                // FR magnitude is windowed at the IR's estimated start.
                IrPreviewSource.PrimaryAtStart);
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
            // radioMagnitudeSpl's tooltip is owned by UpdateSplChoiceLook.
            toolTip.SetToolTip(
                checkBoxShowPrimary,
                "Shows the primary frequency-response curve.");
            toolTip.SetToolTip(
                checkBoxShowCoherence,
                "Shows the measurement coherence (\u03B3\u00B2) curve when the IR was captured with 2+ averaged runs.");
            toolTip.SetToolTip(
                checkBoxShowArrayAverage,
                "Shows the spatial average of the microphone array this measurement " +
                "was recorded with. It is a steady-state curve and does not follow " +
                "the time window.");
            toolTip.SetToolTip(
                checkBoxShowArrayMicrophones,
                "Shows each array position behind the average, levelled onto the " +
                "measurement microphone.");
            toolTip.SetToolTip(
                checkBoxShowArraySpread,
                "Shows how far apart the array positions sit at each frequency, on " +
                "its own axis — where they disagree, a single-point measurement is " +
                "describing that spot rather than the seat.");
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
