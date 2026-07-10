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
            UpdateIrPreview();
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
                "Shows the total harmonic distortion + noise curve.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the transfer impulse response and the analysis window used " +
                "for the primary curve. The harmonic curves window the sweep-deconvolution " +
                "IR with automatically derived windows.");
        }
    }
}
