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
    public partial class PROpt : Form
    {
        private readonly ToolTip toolTip = new();
        private ExpSweepMeasurement? expSweepMeasurement;

        public PROpt()
        {
            InitializeComponent();
            numericLeftWindow.ValueChanged += TukeyWindow_ValueChanged;
            numericRightWindow.ValueChanged += TukeyWindow_ValueChanged;
            SmoothingPresetOptions.Configure(comboSmoothingInverseOctaves);
            InitializeToolTips();
            FormClosed += PROpt_FormClosed;
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, FrequencyResponseOptions opt)
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
            numericWindow.Value = opt.Window;
            numericLeftWindow.Value = opt.LeftTukeyWindow;
            numericRightWindow.Value = opt.RightTukeyWindow;
            comboSmoothingInverseOctaves.SelectedItem =
                SmoothingPresetOptions.Normalize(opt.SmoothingInverseOctaves);
            numericOffset.Value = (decimal)opt.Offset;
            checkBoxUnwrap.Checked = opt.Unwrap;
            UpdateTukeyWindowLimits();
            UpdateIrPreview();
        }

        public void SetOptions(FrequencyResponseOptions opt)
        {
            opt.Window = (int)numericWindow.Value;
            opt.LeftTukeyWindow = (int)numericLeftWindow.Value;
            opt.RightTukeyWindow = (int)numericRightWindow.Value;
            opt.SmoothingInverseOctaves =
                comboSmoothingInverseOctaves.SelectedItem is int inverseOctaves
                    ? inverseOctaves
                    : SmoothingPresetOptions.SupportedInverseOctaves[0];
            opt.Offset = (int)numericOffset.Value;
            opt.Unwrap = checkBoxUnwrap.Checked;
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

        private void PROpt_FormClosed(object? sender, FormClosedEventArgs e)
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
                "Sets the analysis window length used to calculate phase.");
            toolTip.SetToolTip(
                numericLeftWindow,
                "Controls the fade-in part of the Tukey window before the selected impulse region.");
            toolTip.SetToolTip(
                numericRightWindow,
                "Controls the fade-out part of the Tukey window after the selected impulse region.");
            toolTip.SetToolTip(
                comboSmoothingInverseOctaves,
                "Applies octave smoothing to the phase trace.");
            toolTip.SetToolTip(
                numericOffset,
                "Shifts the analysis window relative to the detected impulse-response peak.");
            toolTip.SetToolTip(
                checkBoxUnwrap,
                "Removes 360-degree phase wraps to display a continuous phase curve.");
            toolTip.SetToolTip(
                irPlotView,
                "Preview of the impulse response and the analysis window used for phase calculation.");
        }
    }
}
