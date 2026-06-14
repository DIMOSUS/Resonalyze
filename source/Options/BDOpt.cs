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
        ExpSweepMeasurement expSweepMeasurement;

        public BDOpt()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement, WterfallGenerateOptions burstDecayGenOptions)
        {
            this.expSweepMeasurement = expSweepMeasurement;

            numericSampleRate.Value = expSweepMeasurement.SampleRate;

            numericWindow.Value = burstDecayGenOptions.Window;
            numericCaptureTime.Value = (decimal)CalcCapturedTime;

            numericLeftWindow.Value = burstDecayGenOptions.LeftTukeyWindow;
            numericRightWindow.Value = burstDecayGenOptions.RightTukeyWindow;

            numericdBRange.Value = burstDecayGenOptions.dBRange;

            numericSmothInvOctaves.Value = (decimal)burstDecayGenOptions.SmothInvOctaves;

            numericOffset.Value = burstDecayGenOptions.Offset;

            numericPeriods.Value = (int)burstDecayGenOptions.Periods;
        }

        public void SetOptions(WterfallGenerateOptions burstDecayGenOptions)
        {
            burstDecayGenOptions.Window = (int)numericWindow.Value;

            burstDecayGenOptions.LeftTukeyWindow = (int)numericLeftWindow.Value;
            burstDecayGenOptions.RightTukeyWindow = (int)numericRightWindow.Value;

            burstDecayGenOptions.dBRange = (int)numericdBRange.Value;

            burstDecayGenOptions.SmothInvOctaves = (double)numericSmothInvOctaves.Value;

            burstDecayGenOptions.Offset = (int)numericOffset.Value;

            burstDecayGenOptions.Periods = (double)numericPeriods.Value;
        }

        private double CalcCapturedTime => ((double)numericWindow.Value / (double)(expSweepMeasurement.SampleRate) * 1000.0);

        private void numericWindow_ValueChanged(object sender, EventArgs e)
        {
            numericLeftWindow.Maximum = (int)numericWindow.Value / 2;
            numericRightWindow.Maximum = (int)numericWindow.Value / 2;
            numericCaptureTime.Value = (decimal)CalcCapturedTime;
        }
    }
}
