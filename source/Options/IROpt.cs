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
            Disposed += (_, _) => toolTip.Dispose();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, ImpulseResponseOptions opt)
        {
            // The settings file clamps to a wider range than the control; an
            // out-of-range persisted value must not throw when the panel opens.
            numericLength.Value = numericLength.ClampValue(opt.Length);
            checkLogarithmic.Checked = opt.Logarithmic;
            checkBoxShowImpulse.Checked = opt.ShowImpulse;
        }

        public void SetOptions(ImpulseResponseOptions opt)
        {
            opt.Length = (int)numericLength.Value;
            opt.Logarithmic = checkLogarithmic.Checked;
            opt.ShowImpulse = checkBoxShowImpulse.Checked;
        }

        private void InitializeToolTips()
        {
            numericLength.ApplyToolTip(
                toolTip,
                "Sets how many impulse-response samples are shown on the graph.");
            toolTip.SetToolTip(
                checkLogarithmic,
                "Displays the impulse-response amplitude on a logarithmic scale for easier inspection of low-level tails.");
            toolTip.SetToolTip(
                checkBoxShowImpulse,
                "Shows the impulse-response curve.");
        }
    }
}
