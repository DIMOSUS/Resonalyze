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

        public void Init(ExpSweepMeasurement expSweepMeasurement, FRGenerateOptions fRGenOptions)
        {
            numericWindow.Value = fRGenOptions.Window;
            numericLeftWindow.Value = fRGenOptions.LeftTukeyWindow;
            numericRightWindow.Value = fRGenOptions.RightTukeyWindow;
            numericSmothInvOctaves.Value = (decimal)fRGenOptions.SmothInvOctaves;
            checkUseCalibration.Checked = fRGenOptions.UseCalibration;
        }

        public void SetOptions(FRGenerateOptions fRGenOptions)
        {
            fRGenOptions.Window = (int)numericWindow.Value;
            fRGenOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            fRGenOptions.RightTukeyWindow = (int)numericRightWindow.Value;
            fRGenOptions.SmothInvOctaves = (double)numericSmothInvOctaves.Value;
            fRGenOptions.UseCalibration = checkUseCalibration.Checked;
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            numericLeftWindow.Maximum = (int)numericWindow.Value / 2;
            numericRightWindow.Maximum = (int)numericWindow.Value / 2;
        }
    }
}
