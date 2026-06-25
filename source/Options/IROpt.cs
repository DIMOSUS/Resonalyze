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
    public partial class IROpt : Form
    {
        private readonly ToolTip toolTip = new();

        public IROpt()
        {
            InitializeComponent();
            InitializeToolTips();
            FormClosed += (_, _) => toolTip.Dispose();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, ImpulseResponseOptions opt)
        {
            numericLength.Value = opt.Length;
            checkLogarithmic.Checked = opt.Logarithmic;
        }

        public void SetOptions(ImpulseResponseOptions opt)
        {
            opt.Length = (int)numericLength.Value;
            opt.Logarithmic = checkLogarithmic.Checked;
        }

        private void InitializeToolTips()
        {
            toolTip.SetToolTip(
                numericLength,
                "Sets how many impulse-response samples are shown on the graph.");
            toolTip.SetToolTip(
                checkLogarithmic,
                "Displays the impulse-response amplitude on a logarithmic scale for easier inspection of low-level tails.");
        }
    }
}
