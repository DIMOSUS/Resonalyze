using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Resonalyze.Options
{
    public partial class WaterfallOptions : Form
    {
        private ExpSweepMeasurement? expSweepMeasurement;
        private decimal lastNonZeroStep = 4;

        public WaterfallOptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            FormClosed += WaterfallOptions_FormClosed;
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, WaterfallGenerateOptions waterfallGenerateOptions)
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

            numericWindow.Value = waterfallGenerateOptions.Window;
            numericSlices.Value = waterfallGenerateOptions.SliceCount;
            lastNonZeroStep = waterfallGenerateOptions.Step == 0 ? 1 : waterfallGenerateOptions.Step;
            numericStep.Value = lastNonZeroStep;
            numericCaptureTime.Value = (decimal)CalcCapturedTime;

            numericLeftWindow.Value = waterfallGenerateOptions.LeftTukeyWindow;
            numericRightWindow.Value = waterfallGenerateOptions.RightTukeyWindow;

            numericDbRange.Value = waterfallGenerateOptions.DbRange;

            numericSmoothingInverseOctaves.Value = (decimal)waterfallGenerateOptions.SmoothingInverseOctaves;

            numericOffset.Value = waterfallGenerateOptions.Offset;
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        public void SetOptions(WaterfallGenerateOptions waterfallGenerateOptions)
        {
            waterfallGenerateOptions.Window = (int)numericWindow.Value;
            waterfallGenerateOptions.SliceCount = (int)numericSlices.Value;
            waterfallGenerateOptions.Step = (int)numericStep.Value;

            waterfallGenerateOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            waterfallGenerateOptions.RightTukeyWindow = (int)numericRightWindow.Value;

            waterfallGenerateOptions.DbRange = (int)numericDbRange.Value;

            waterfallGenerateOptions.SmoothingInverseOctaves = (double)numericSmoothingInverseOctaves.Value;

            waterfallGenerateOptions.Offset = (int)numericOffset.Value;
            UpdateIrPreview();
        }

        private double CalcCapturedTime
        {
            get
            {
                int sampleRate = expSweepMeasurement?.SampleRate ?? 0;
                return sampleRate > 0
                    ? (double)numericSlices.Value * (double)numericStep.Value / sampleRate * 1000.0
                    : 0;
            }
        }

        private void numericSlices_ValueChanged(object sender, EventArgs e)
        {
            numericCaptureTime.Value = (decimal)CalcCapturedTime;
        }

        private void numericStep_ValueChanged(object sender, EventArgs e)
        {
            if (numericStep.Value == 0)
            {
                numericStep.Value = lastNonZeroStep > 0 ? -1 : 1;
                return;
            }

            lastNonZeroStep = numericStep.Value;
            numericCaptureTime.Value = (decimal)CalcCapturedTime;
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

        private void WaterfallOptions_FormClosed(object? sender, FormClosedEventArgs e)
        {
            if (expSweepMeasurement != null)
            {
                expSweepMeasurement.ImpulseResponseChanged -= ExpSweepMeasurement_ImpulseResponseChanged;
            }
        }
    }
}
