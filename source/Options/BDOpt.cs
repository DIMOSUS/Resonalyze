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
            BindTukeyWindowControls(
                numericWindow,
                numericLeftWindow,
                numericRightWindow,
                afterWindowChanged: () =>
                    numericCaptureTime.Value = (decimal)CalcCapturedTime);
            // Width presets only: burst decay has no magnitude grid, so psychoacoustic would silently alias 1/6.
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
        }

        internal void Init(
            AnalyzerDocument document,
            int configuredSampleRate,
            WaterfallGenerateOptions burstDecayGenOptions)
        {
            AttachMeasurement(document, configuredSampleRate);
            InitializeControls(() =>
            {
                numericSampleRate.Value = SampleRate;

                numericWindow.Value = burstDecayGenOptions.Window;
                numericCaptureTime.Value = (decimal)CalcCapturedTime;

                numericLeftWindow.Value = burstDecayGenOptions.LeftTukeyWindow;
                numericRightWindow.Value = burstDecayGenOptions.RightTukeyWindow;

                numericDbRange.Value = burstDecayGenOptions.DbRange;

                comboSmoothingInverseOctaves.SelectedItem =
                    SmoothingPresetOptions.Normalize(
                    burstDecayGenOptions.SmoothingInverseOctaves, includePsychoacoustic: false);

                numericOffset.Value = burstDecayGenOptions.Offset;

                numericPeriods.Value = (int)burstDecayGenOptions.Periods;
                RefreshTukeyWindowLimits();
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
                int sampleRate = Document == null ? 0 : SampleRate;
                return sampleRate > 0
                    ? (double)numericWindow.Value / sampleRate * 1000.0
                    : 0;
            }
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
