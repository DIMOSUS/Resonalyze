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
    public partial class WaterfallOptions : Form
    {
        private readonly ToolTip toolTip = new();
        private ExpSweepMeasurement? expSweepMeasurement;
        private decimal lastNonZeroStep = 4;

        public WaterfallOptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
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

            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(waterfallGenerateOptions.SmoothingInverseOctaves);

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

            waterfallGenerateOptions.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];

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

            toolTip.Dispose();
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                numericWindow,
                "Sets the FFT window length for each waterfall slice.");
            toolTip.SetToolTip(
                numericSlices,
                "Controls how many slices are drawn in depth.");
            toolTip.SetToolTip(
                numericStep,
                "Sets the shift in samples between neighboring slices. Larger values cover more time with fewer overlapping slices.");
            toolTip.SetToolTip(
                numericLeftWindow,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            toolTip.SetToolTip(
                numericRightWindow,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            toolTip.SetToolTip(
                numericDbRange,
                "Sets the lower display limit in decibels for the waterfall plot.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to each resampled frequency slice.");
            toolTip.SetToolTip(
                numericOffset,
                "Shifts the whole waterfall analysis window relative to the detected impulse-response peak.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for waterfall generation.");
        }
    }
}
