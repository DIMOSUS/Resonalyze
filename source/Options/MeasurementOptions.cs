using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions : Form
    {
        private ExpSweepMeasurement? expSweepMeasurement;

        public MeasurementOptions()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement)
        {
            this.expSweepMeasurement = expSweepMeasurement;
            ExponentialSineSweep sweep = expSweepMeasurement.exponentialSineSweep
                ?? throw new InvalidOperationException("Sweep measurement is not initialized.");
            numericUpDownSampleRate.Value = expSweepMeasurement.SampleRate;
            numericUpDownBits.Value = expSweepMeasurement.Bits;
            
            comboBoxChanel.Items.Clear();
            for(int i = 0; i < (int)Chanels.Count; i++)
            {
                comboBoxChanel.Items.Add(((Chanels)i).ToString());
            }
            comboBoxChanel.SelectedIndex = (int)expSweepMeasurement.PlayCanels;

            numericUpDownDesireDuration.Value = (int)(sweep.DesireDuration * 1000.0);
            numericUpDownComputeDuration.Value = (int)(sweep.CalcDuration(sweep.DesireDuration) * 1000.0);
            numericUpDownOctaves.Value = expSweepMeasurement.Octaves;
        }

        public void SetOptions(ExpSweepMeasurement expSweepMeasurement)
        {
            int sampleRate = (int)numericUpDownSampleRate.Value;
            int bits = expSweepMeasurement.Bits;
            Chanels chanels = (Chanels)comboBoxChanel.SelectedIndex;
            double desireDuration = (double)numericUpDownDesireDuration.Value * 0.001;
            int octaves = (int)numericUpDownOctaves.Value;

            expSweepMeasurement.Init(octaves, sampleRate, bits, desireDuration, chanels);
        }

        private void numericUpDownDesireDuration_ValueChanged(object sender, EventArgs e)
        {
            ExponentialSineSweep? sweep = expSweepMeasurement?.exponentialSineSweep;
            if (sweep == null)
            {
                return;
            }

            numericUpDownComputeDuration.Value =
                (int)(sweep.CalcDuration((int)numericUpDownDesireDuration.Value * 0.001) * 1000.0);
        }
    }
}
