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
    public partial class WaterfallOptions : ImpulsePreviewOptionsForm
    {
        private decimal lastNonZeroStep = 4;

        public WaterfallOptions()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(
                comboSmoothingInverseOctaves, includePsychoacoustic: true);
            InitializeToolTips();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, WaterfallGenerateOptions waterfallGenerateOptions)
        {
            AttachMeasurement(expSweepMeasurement);
            InitializeControls(() =>
            {
                numericSampleRate.Value = numericSampleRate.ClampValue(expSweepMeasurement.SampleRate);

                // The settings file clamps to wider ranges than the controls; an
                // out-of-range persisted value must not throw when the panel opens.
                numericWindow.Value = numericWindow.ClampValue(waterfallGenerateOptions.Window);
                numericSlices.Value = numericSlices.ClampValue(waterfallGenerateOptions.SliceCount);
                lastNonZeroStep = waterfallGenerateOptions.Step == 0 ? 1 : waterfallGenerateOptions.Step;
                numericStep.Value = numericStep.ClampValue((double)lastNonZeroStep);
                numericCaptureTime.Value = numericCaptureTime.ClampValue((double)CalcCapturedTime);

                numericLeftWindow.Value = numericLeftWindow.ClampValue(waterfallGenerateOptions.LeftTukeyWindow);
                numericRightWindow.Value = numericRightWindow.ClampValue(waterfallGenerateOptions.RightTukeyWindow);

                numericDbRange.Value = numericDbRange.ClampValue(waterfallGenerateOptions.DbRange);

                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(waterfallGenerateOptions.SmoothingInverseOctaves);

                numericOffset.Value = numericOffset.ClampValue(waterfallGenerateOptions.Offset);
                UpdateTukeyWindowLimits();
            });
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
                int sampleRate = Measurement?.SampleRate ?? 0;
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
                (int)numericOffset.Value,
                IrPreviewSource.Primary);
        }

        private void InitializeToolTips()
        {
            numericWindow.ApplyToolTip(
                toolTip,
                "Sets the FFT window length for each waterfall slice.");
            numericSlices.ApplyToolTip(
                toolTip,
                "Controls how many slices are drawn in depth.");
            numericStep.ApplyToolTip(
                toolTip,
                "Sets the shift in samples between neighboring slices. Larger values cover more time with fewer overlapping slices.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            numericDbRange.ApplyToolTip(
                toolTip,
                "Sets the lower display limit in decibels for the waterfall plot.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to each resampled frequency slice.");
            numericOffset.ApplyToolTip(
                toolTip,
                "Shifts the whole waterfall analysis window relative to the detected impulse-response peak.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for waterfall generation.");
        }
    }
}
