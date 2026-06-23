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
        private ExpSweepMeasurement? expSweepMeasurement;

        public FROptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
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
            numericSmoothingInverseOctaves.Value = (decimal)frequencyResponseOptions.SmoothingInverseOctaves;
            checkUseCalibration.Checked = frequencyResponseOptions.UseCalibration;
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions frequencyResponseOptions)
        {
            frequencyResponseOptions.Window = (int)numericWindow.Value;
            frequencyResponseOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            frequencyResponseOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            frequencyResponseOptions.SmoothingInverseOctaves = (double)numericSmoothingInverseOctaves.Value;
            frequencyResponseOptions.UseCalibration = checkUseCalibration.Checked;
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
                IrPreviewSource.SweepDeconvolution);
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
        }
    }
}
