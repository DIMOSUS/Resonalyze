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
        public IROpt()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, IRGenerateOptions opt)
        {
            numericLength.Value = opt.Length;
            checkLogarithmic.Checked = opt.Logarithmic;
        }

        public void SetOptions(IRGenerateOptions opt)
        {
            opt.Length = (int)numericLength.Value;
            opt.Logarithmic = checkLogarithmic.Checked;
        }
    }
}
