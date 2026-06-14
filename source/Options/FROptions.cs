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
        public FROptions()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, FrequencyResponseOptions frequencyResponseOptions)
        {
            numericWindow.Value = frequencyResponseOptions.Window;
            numericLeftWindow.Value = frequencyResponseOptions.LeftTukeyWindow;
            numericRightWindow.Value = frequencyResponseOptions.RightTukeyWindow;
            numericSmoothingInverseOctaves.Value = (decimal)frequencyResponseOptions.SmoothingInverseOctaves;
            checkUseCalibration.Checked = frequencyResponseOptions.UseCalibration;
        }

        public void SetOptions(FrequencyResponseOptions frequencyResponseOptions)
        {
            frequencyResponseOptions.Window = (int)numericWindow.Value;
            frequencyResponseOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            frequencyResponseOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            frequencyResponseOptions.SmoothingInverseOctaves = (double)numericSmoothingInverseOctaves.Value;
            frequencyResponseOptions.UseCalibration = checkUseCalibration.Checked;
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            numericLeftWindow.Maximum = (int)numericWindow.Value / 2;
            numericRightWindow.Maximum = (int)numericWindow.Value / 2;
        }
    }
}
