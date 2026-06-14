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
        public PROpt()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, FRGenerateOptions opt)
        {
            numericWindow.Value = opt.Window;
            numericLeftWindow.Value = opt.LeftTukeyWindow;
            numericRightWindow.Value = opt.RightTukeyWindow;
            numericSmothInvOctaves.Value = (decimal)opt.SmothInvOctaves;
            numericOffset.Value = (decimal)opt.Offset;
            checkBoxUnwrap.Checked = opt.Unwrap;
        }

        public void SetOptions(FRGenerateOptions opt)
        {
            opt.Window = (int)numericWindow.Value;
            opt.LeftTukeyWindow = (int)numericLeftWindow.Value;
            opt.RightTukeyWindow = (int)numericRightWindow.Value;
            opt.SmothInvOctaves = (double)numericSmothInvOctaves.Value;
            opt.Offset = (int)numericOffset.Value;
            opt.Unwrap = checkBoxUnwrap.Checked;
        }

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            numericLeftWindow.Maximum = (int)numericWindow.Value / 2;
            numericRightWindow.Maximum = (int)numericWindow.Value / 2;
        }
    }
}
