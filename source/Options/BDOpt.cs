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
        private readonly ToolTip toolTip = new();
        private ExpSweepMeasurement? expSweepMeasurement;

        public BDOpt()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
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

            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(burstDecayGenOptions.SmoothingInverseOctaves);

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

            toolTip.Dispose();
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                numericWindow,
                "Sets the impulse-response window length used for burst-decay analysis.");
            toolTip.SetToolTip(
                numericLeftWindow,
                "Controls the fade-in part of the Tukey window before the analyzed region.");
            toolTip.SetToolTip(
                numericRightWindow,
                "Controls the fade-out part of the Tukey window after the analyzed region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Sets the analysis bandwidth in octaves for each burst-decay slice. Narrower bands increase frequency resolution but make traces less stable.");
            toolTip.SetToolTip(
                numericDbRange,
                "Sets the lower display limit in decibels for the burst-decay plot.");
            toolTip.SetToolTip(
                numericOffset,
                "Shifts the burst-decay analysis window relative to the detected impulse-response peak.");
            toolTip.SetToolTip(
                numericPeriods,
                "Sets how many signal periods are shown on the horizontal axis for each frequency slice.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for burst-decay generation.");
        }
    }
}
