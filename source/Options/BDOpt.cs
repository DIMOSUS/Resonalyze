using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Resonalyze.Options
{
    public partial class BDOpt : Form
    {
        private ExpSweepMeasurement? expSweepMeasurement;

        public BDOpt()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            FormClosed += BDOpt_FormClosed;
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, WaterfallGenerateOptions burstDecayGenOptions)
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

            numericSampleRate.Value = expSweepMeasurement.SampleRate;

            numericWindow.Value = burstDecayGenOptions.Window;
            numericCaptureTime.Value = (decimal)CalcCapturedTime;

            numericLeftWindow.Value = burstDecayGenOptions.LeftTukeyWindow;
            numericRightWindow.Value = burstDecayGenOptions.RightTukeyWindow;

            numericDbRange.Value = burstDecayGenOptions.DbRange;

            numericSmoothingInverseOctaves.Value = (decimal)burstDecayGenOptions.SmoothingInverseOctaves;

            numericOffset.Value = burstDecayGenOptions.Offset;

            numericPeriods.Value = (int)burstDecayGenOptions.Periods;
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        public void SetOptions(WaterfallGenerateOptions burstDecayGenOptions)
        {
            burstDecayGenOptions.Window = (int)numericWindow.Value;

            burstDecayGenOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            burstDecayGenOptions.RightTukeyWindow = (int)numericRightWindow.Value;

            burstDecayGenOptions.DbRange = (int)numericDbRange.Value;

            burstDecayGenOptions.SmoothingInverseOctaves = (double)numericSmoothingInverseOctaves.Value;

            burstDecayGenOptions.Offset = (int)numericOffset.Value;

            burstDecayGenOptions.Periods = (double)numericPeriods.Value;
            UpdateIrPreview();
        }

        private double CalcCapturedTime
        {
            get
            {
                int sampleRate = expSweepMeasurement?.SampleRate ?? 0;
                return sampleRate > 0
                    ? (double)numericWindow.Value / sampleRate * 1000.0
                    : 0;
            }
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            UpdateTukeyWindowLimits();
            numericCaptureTime.Value = (decimal)CalcCapturedTime;
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
                (int)numericOffset.Value,
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

        private void BDOpt_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (expSweepMeasurement != null)
            {
                expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }
        }
    }
}
