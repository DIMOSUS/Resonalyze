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
    public partial class BDOpt : ImpulsePreviewOptionsForm
    {
        public BDOpt()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, WaterfallGenerateOptions burstDecayGenOptions)
        {
            AttachMeasurement(expSweepMeasurement);
            InitializeControls(() =>
            {
                numericSampleRate.Value = expSweepMeasurement.SampleRate;

                numericWindow.Value = burstDecayGenOptions.Window;
                numericCaptureTime.Value = (decimal)CalcCapturedTime;

                numericLeftWindow.Value = burstDecayGenOptions.LeftTukeyWindow;
                numericRightWindow.Value = burstDecayGenOptions.RightTukeyWindow;

                numericDbRange.Value = burstDecayGenOptions.DbRange;

                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(burstDecayGenOptions.SmoothingInverseOctaves);

                numericOffset.Value = burstDecayGenOptions.Offset;

                numericPeriods.Value = (int)burstDecayGenOptions.Periods;
                UpdateTukeyWindowLimits();
            });
            UpdateIrPreview();
        }

        public void SetOptions(WaterfallGenerateOptions burstDecayGenOptions)
        {
            burstDecayGenOptions.Window = (int)numericWindow.Value;

            burstDecayGenOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            burstDecayGenOptions.RightTukeyWindow = (int)numericRightWindow.Value;

            burstDecayGenOptions.DbRange = (int)numericDbRange.Value;

            burstDecayGenOptions.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];

            burstDecayGenOptions.Offset = (int)numericOffset.Value;

            burstDecayGenOptions.Periods = (double)numericPeriods.Value;
            UpdateIrPreview();
        }

        private double CalcCapturedTime
        {
            get
            {
                int sampleRate = Measurement?.SampleRate ?? 0;
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
                "Sets the impulse-response window length used for burst-decay analysis.");
            numericLeftWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            numericRightWindow.ApplyToolTip(
                toolTip,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Sets the analysis bandwidth in octaves for each burst-decay slice. Narrower bands increase frequency resolution but make traces less stable.");
            numericDbRange.ApplyToolTip(
                toolTip,
                "Sets the lower display limit in decibels for the burst-decay plot.");
            numericOffset.ApplyToolTip(
                toolTip,
                "Shifts the burst-decay analysis window relative to the detected impulse-response peak.");
            numericPeriods.ApplyToolTip(
                toolTip,
                "Sets how many signal periods are shown on the horizontal axis for each frequency slice.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for burst-decay generation.");
        }
    }
}
