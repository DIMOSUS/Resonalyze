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
        private IReadOnlyList<AudioDeviceInfo> playbackDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioDeviceInfo> recordingDevices = Array.Empty<AudioDeviceInfo>();

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

            playbackDevices = AudioDeviceCatalog.GetPlaybackDevices();
            comboBoxPlaybackDevice.Items.Clear();
            comboBoxPlaybackDevice.Items.AddRange(playbackDevices.Cast<object>().ToArray());
            comboBoxPlaybackDevice.SelectedIndex = AudioDeviceCatalog.FindDeviceIndex(
                playbackDevices,
                expSweepMeasurement.OutputDeviceNumber);

            recordingDevices = AudioDeviceCatalog.GetRecordingDevices();
            comboBoxRecordingDevice.Items.Clear();
            comboBoxRecordingDevice.Items.AddRange(recordingDevices.Cast<object>().ToArray());
            comboBoxRecordingDevice.SelectedIndex = AudioDeviceCatalog.FindDeviceIndex(
                recordingDevices,
                expSweepMeasurement.InputDeviceNumber);

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
            int outputDeviceNumber = ((AudioDeviceInfo)comboBoxPlaybackDevice.SelectedItem!).DeviceNumber;
            int inputDeviceNumber = ((AudioDeviceInfo)comboBoxRecordingDevice.SelectedItem!).DeviceNumber;

            expSweepMeasurement.Init(
                octaves,
                sampleRate,
                bits,
                requestedDuration,
                playbackChannel,
                outputDeviceNumber,
                inputDeviceNumber);
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
