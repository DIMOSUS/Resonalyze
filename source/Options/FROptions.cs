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
    public partial class FROptions : Form
    {
        private readonly ToolTip toolTip = new();
        private ExpSweepMeasurement? expSweepMeasurement;

        public FROptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
            FormClosed += FROptions_FormClosed;
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, FrequencyResponseOptions frequencyResponseOptions)
        {
            if (!ReferenceEquals(this.expSweepMeasurement, expSweepMeasurement))
            {
                if (this.expSweepMeasurement != null)
                {
                    this.expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
                }

                expSweepMeasurement.ImpulseResponseChanged += ExpSweepMeasurement_ImpulseResponseChanged;
            }

            this.expSweepMeasurement = expSweepMeasurement;
            numericWindow.Value = frequencyResponseOptions.Window;
            numericLeftWindow.Value = frequencyResponseOptions.LeftTukeyWindow;
            numericRightWindow.Value = frequencyResponseOptions.RightTukeyWindow;
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(frequencyResponseOptions.SmoothingInverseOctaves);
            checkUseCalibration.Checked = frequencyResponseOptions.UseCalibration;
            checkBoxShowPrimary.Checked = frequencyResponseOptions.ShowPrimary;
            checkBoxShowCoherence.Checked = frequencyResponseOptions.ShowCoherence;
            checkBoxShowHd2.Checked = frequencyResponseOptions.ShowHd2;
            checkBoxShowHd3.Checked = frequencyResponseOptions.ShowHd3;
            checkBoxShowHd4.Checked = frequencyResponseOptions.ShowHd4;
            checkBoxShowThdPlusNoise.Checked = frequencyResponseOptions.ShowThdPlusNoise;
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions frequencyResponseOptions)
        {
            frequencyResponseOptions.Window = (int)numericWindow.Value;
            frequencyResponseOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            frequencyResponseOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            frequencyResponseOptions.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            frequencyResponseOptions.UseCalibration = checkUseCalibration.Checked;
            frequencyResponseOptions.ShowPrimary = checkBoxShowPrimary.Checked;
            frequencyResponseOptions.ShowCoherence = checkBoxShowCoherence.Checked;
            frequencyResponseOptions.ShowHd2 = checkBoxShowHd2.Checked;
            frequencyResponseOptions.ShowHd3 = checkBoxShowHd3.Checked;
            frequencyResponseOptions.ShowHd4 = checkBoxShowHd4.Checked;
            frequencyResponseOptions.ShowThdPlusNoise = checkBoxShowThdPlusNoise.Checked;
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

        private void UpdateIrPreview()
        {
            if (expSweepMeasurement == null)
            {
                return;
            }

            ImpulseWindowPreview.Update(
                irPlotView,
                expSweepMeasurement,
                (int)numericWindow.Value,
                (int)numericLeftWindow.Value,
                (int)numericRightWindow.Value,
                offset: 0,
                // FR magnitude is now windowed on the transfer IR, so preview that window.
                IrPreviewSource.Primary);
        }

        private void ExpSweepMeasurement_ImpulseResponseChanged()
        {
            if (IsDisposed)
            {
                return;
            }

            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((MethodInvoker)UpdateIrPreview);
                return;
            }

            UpdateIrPreview();
        }

        private void FROptions_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (expSweepMeasurement != null)
            {
                expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }

            toolTip.Dispose();
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
                checkUseCalibration,
                "Applies the loaded microphone calibration file to the displayed frequency response.");
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
                "Preview of the sweep-deconvolution impulse response and the analysis window used for this mode.");
        }
    }
}
