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
        public FROptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
        }

        public void Init(
            ExpSweepMeasurement expSweepMeasurement,
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility,
            bool hasZeroDegreeCalibration,
            bool hasNinetyDegreeCalibration)
        {
            AttachMeasurement(expSweepMeasurement);
            InitializeControls(() =>
            {
                numericWindow.Value = frequencyResponseOptions.Window;
                numericLeftWindow.Value = frequencyResponseOptions.LeftTukeyWindow;
                numericRightWindow.Value = frequencyResponseOptions.RightTukeyWindow;
                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(frequencyResponseOptions.SmoothingInverseOctaves);
                MicrophoneCalibrationComboHelper.Configure(
                    comboCalibration,
                    frequencyResponseOptions.CalibrationMode,
                    hasZeroDegreeCalibration,
                    hasNinetyDegreeCalibration);
                checkBoxShowPrimary.Checked = visibility.ShowPrimary;
                checkBoxShowCoherence.Checked = visibility.ShowCoherence;
                checkBoxShowHd2.Checked = visibility.ShowHd2;
                checkBoxShowHd3.Checked = visibility.ShowHd3;
                checkBoxShowHd4.Checked = visibility.ShowHd4;
                checkBoxShowThdPlusNoise.Checked = visibility.ShowThdPlusNoise;
                checkBoxShowNoiseFloor.Checked = visibility.ShowNoiseFloor;
                // Keep the radio Enabled and mute it instead, so a disabled SPL choice
                // renders in the theme's muted colour rather than the near-black system
                // grey that WinForms would paint on the dark background.
                bool splAvailable = IsSplAvailable();
                UiStyle.SetTextEnabledLook(radioMagnitudeSpl, splAvailable, interactive: true);
                bool spl = frequencyResponseOptions.MagnitudeScale == MagnitudeScale.SoundPressureLevel
                    && splAvailable;
                radioMagnitudeSpl.Checked = spl;
                radioMagnitudeRelative.Checked = !spl;
                UpdateTukeyWindowLimits();
            });
            UpdateIrPreview();
        }

        public void SetOptions(
            FrequencyResponseOptions frequencyResponseOptions,
            CurveVisibilityOptions visibility)
        {
            frequencyResponseOptions.Window = (int)numericWindow.Value;
            frequencyResponseOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            frequencyResponseOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            frequencyResponseOptions.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            frequencyResponseOptions.CalibrationMode =
                MicrophoneCalibrationComboHelper.GetSelectedMode(comboCalibration);
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
        /// Re-evaluates whether dB SPL can be offered, without disturbing the current
        /// selection. The panel can open before a measurement runs (no captured
        /// loopback level yet), so the choice starts disabled; the host calls this once
        /// a measurement or a loaded file provides the level, so the user can switch to
        /// SPL without reopening the panel.
        /// </summary>
        public void RefreshSplAvailability()
        {
            bool available = IsSplAvailable();
            UiStyle.SetTextEnabledLook(radioMagnitudeSpl, available, interactive: true);

            // If SPL was the chosen scale but is no longer available (a run failed, or a
            // file without a usable anchor was loaded), the plot already fell back to
            // relative — move the selection with it so the checked radio does not
            // contradict the axis.
            if (!available && radioMagnitudeSpl.Checked)
            {
                radioMagnitudeRelative.Checked = true;
            }
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        private void TukeyWindow_ValueChanged(object? sender, EventArgs e)
        {
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        private void UpdateTukeyWindowLimits()
        {
            TukeyWindowControlHelper.ClampAndUpdateLimits(
                numericWindow,
                numericLeftWindow,
                numericRightWindow);
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
            toolTip.SetToolTip(
                radioMagnitudeSpl,
                "Absolute dB SPL from the microphone SPL calibration. Available only when this " +
                "measurement has a valid calibration and a captured loopback level.");
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
                "for the primary curve. The harmonic curves window the sweep-deconvolution " +
                "IR with automatically derived windows.");
        }
    }
}
