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
            ExponentialSineSweep sweep = expSweepMeasurement.Sweep
                ?? throw new InvalidOperationException("Sweep measurement is not initialized.");
            numericUpDownSampleRate.Value = expSweepMeasurement.SampleRate;
            numericUpDownBits.Value = expSweepMeasurement.Bits;

            comboBoxChannel.Items.Clear();
            foreach (PlaybackChannel channel in Enum.GetValues<PlaybackChannel>())
            {
                comboBoxChannel.Items.Add(channel.ToString());
            }
            comboBoxChannel.SelectedIndex = (int)expSweepMeasurement.PlaybackChannel;

            numericUpDownRequestedDuration.Value = (int)(sweep.RequestedDuration * 1000.0);
            numericUpDownComputeDuration.Value = (int)(sweep.CalculateDuration(sweep.RequestedDuration) * 1000.0);
            numericUpDownOctaves.Value = expSweepMeasurement.Octaves;
        }

        public void SetOptions(ExpSweepMeasurement expSweepMeasurement)
        {
            int sampleRate = (int)numericUpDownSampleRate.Value;
            int bits = expSweepMeasurement.Bits;
            PlaybackChannel playbackChannel = (PlaybackChannel)comboBoxChannel.SelectedIndex;
            double requestedDuration = (double)numericUpDownRequestedDuration.Value * 0.001;
            int octaves = (int)numericUpDownOctaves.Value;

            expSweepMeasurement.Init(octaves, sampleRate, bits, requestedDuration, playbackChannel);
        }

        private void numericUpDownRequestedDuration_ValueChanged(object sender, EventArgs e)
        {
            ExponentialSineSweep? sweep = expSweepMeasurement?.Sweep;
            if (sweep == null)
            {
                return;
            }

            numericUpDownComputeDuration.Value =
                (int)(sweep.CalculateDuration((int)numericUpDownRequestedDuration.Value * 0.001) * 1000.0);
        }
    }
}
