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
        private IReadOnlyList<AsioDeviceInfo> asioDrivers = Array.Empty<AsioDeviceInfo>();
        private AsioDriverInfo asioDriverInfo = AsioDeviceCatalog.EmptyDriverInfo;
        private bool initializing;

        public MeasurementOptions()
        {
            InitializeComponent();
        }

        public void Init(ExpSweepMeasurement expSweepMeasurement)
        {
            initializing = true;
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

            comboBoxAudioBackend.Items.Clear();
            foreach (AudioBackend backend in Enum.GetValues<AudioBackend>())
            {
                comboBoxAudioBackend.Items.Add(backend.ToString());
            }
            comboBoxAudioBackend.SelectedIndex = (int)expSweepMeasurement.AudioBackend;

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

            asioDrivers = AsioDeviceCatalog.GetDrivers();
            comboBoxAsioDriver.Items.Clear();
            if (asioDrivers.Count > 0)
            {
                comboBoxAsioDriver.Items.AddRange(asioDrivers.Cast<object>().ToArray());
                comboBoxAsioDriver.SelectedIndex = AsioDeviceCatalog.FindDriverIndex(
                    asioDrivers,
                    expSweepMeasurement.AsioDriverName);
            }

            numericUpDownRequestedDuration.Value = (int)(sweep.RequestedDuration * 1000.0);
            numericUpDownComputeDuration.Value = (int)(sweep.CalculateDuration(sweep.RequestedDuration) * 1000.0);
            numericUpDownOctaves.Value = expSweepMeasurement.Octaves;
            initializing = false;
            RefreshAsioDriverInfo(
                expSweepMeasurement.AsioInputChannelOffset,
                expSweepMeasurement.AsioOutputChannelOffset);
            UpdateAudioBackendControls();
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
            AudioBackend audioBackend = (AudioBackend)comboBoxAudioBackend.SelectedIndex;
            string? asioDriverName = comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver
                ? asioDriver.DriverName
                : null;
            if (audioBackend == AudioBackend.Asio && string.IsNullOrWhiteSpace(asioDriverName))
            {
                throw new InvalidOperationException("Select an ASIO driver before starting measurement.");
            }
            if (audioBackend == AudioBackend.Asio)
            {
                ValidateSelectedAsioDriver(sampleRate);
            }
            int asioInputChannelOffset =
                comboBoxAsioInputChannel.SelectedItem is AsioChannelInfo inputChannel
                    ? inputChannel.Offset
                    : 0;
            int asioOutputChannelOffset =
                comboBoxAsioOutputChannel.SelectedItem is AsioChannelInfo outputChannel
                    ? outputChannel.Offset
                    : 0;

            expSweepMeasurement.Init(
                octaves,
                sampleRate,
                bits,
                requestedDuration,
                playbackChannel,
                outputDeviceNumber,
                inputDeviceNumber,
                audioBackend,
                asioDriverName,
                asioInputChannelOffset,
                asioOutputChannelOffset);
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

        private void comboBoxAudioBackend_SelectedIndexChanged(object sender, EventArgs e) =>
            UpdateAudioBackendControls();

        private void comboBoxAsioDriver_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            RefreshAsioDriverInfo(0, 0);
            UpdateAudioBackendControls();
        }

        private void UpdateAudioBackendControls()
        {
            bool useAsio =
                comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio;
            comboBoxPlaybackDevice.Enabled = !useAsio;
            comboBoxRecordingDevice.Enabled = !useAsio;
            comboBoxAsioDriver.Enabled = useAsio && asioDrivers.Count > 0;
            buttonAsioControlPanel.Enabled =
                useAsio && comboBoxAsioDriver.SelectedItem is AsioDeviceInfo;
            comboBoxAsioInputChannel.Enabled =
                useAsio && asioDriverInfo.InputChannels.Count > 0;
            comboBoxAsioOutputChannel.Enabled =
                useAsio && asioDriverInfo.OutputChannels.Count > 0;
            labelAsioDriver.Enabled = useAsio;
            labelAsioInputChannel.Enabled = useAsio;
            labelAsioOutputChannel.Enabled = useAsio;
            labelAsioSampleRate.Enabled = useAsio;
            labelAsioSampleRateStatus.Enabled = useAsio;
            labelAsioFramesPerBuffer.Enabled = useAsio;
            labelAsioFramesPerBufferValue.Enabled = useAsio;
            labelAsioPlaybackLatency.Enabled = useAsio;
            labelAsioPlaybackLatencyValue.Enabled = useAsio;
            labelPlaybackDevice.Enabled = !useAsio;
            labelRecordingDevice.Enabled = !useAsio;
        }

        private void buttonAsioControlPanel_Click(object sender, EventArgs e)
        {
            if (comboBoxAsioDriver.SelectedItem is not AsioDeviceInfo asioDriver)
            {
                return;
            }

            try
            {
                AsioDeviceCatalog.ShowControlPanel(asioDriver.DriverName);
                RefreshAsioDriverInfo(
                    comboBoxAsioInputChannel.SelectedItem is AsioChannelInfo inputChannel
                        ? inputChannel.Offset
                        : 0,
                    comboBoxAsioOutputChannel.SelectedItem is AsioChannelInfo outputChannel
                        ? outputChannel.Offset
                        : 0);
                UpdateAudioBackendControls();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "ASIO Control Panel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RefreshAsioDriverInfo(
            int preferredInputOffset,
            int preferredOutputOffset)
        {
            string? driverName = comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver
                ? asioDriver.DriverName
                : null;
            asioDriverInfo = AsioDeviceCatalog.GetDriverInfo(
                driverName,
                (int)numericUpDownSampleRate.Value);

            comboBoxAsioInputChannel.Items.Clear();
            comboBoxAsioOutputChannel.Items.Clear();
            comboBoxAsioInputChannel.Items.AddRange(
                asioDriverInfo.InputChannels.Cast<object>().ToArray());
            comboBoxAsioOutputChannel.Items.AddRange(
                asioDriverInfo.OutputChannels.Cast<object>().ToArray());
            comboBoxAsioInputChannel.SelectedIndex = AsioDeviceCatalog.FindChannelIndex(
                asioDriverInfo.InputChannels,
                preferredInputOffset);
            comboBoxAsioOutputChannel.SelectedIndex = AsioDeviceCatalog.FindChannelIndex(
                asioDriverInfo.OutputChannels,
                preferredOutputOffset);

            UpdateAsioStatusLabels();
        }

        private void UpdateAsioStatusLabels()
        {
            if (!string.IsNullOrWhiteSpace(asioDriverInfo.ErrorMessage))
            {
                labelAsioSampleRateStatus.Text = asioDriverInfo.ErrorMessage;
                labelAsioSampleRateStatus.ForeColor = Color.LightSalmon;
                labelAsioFramesPerBufferValue.Text = "-";
                labelAsioPlaybackLatencyValue.Text = "-";
                return;
            }

            int sampleRate = (int)numericUpDownSampleRate.Value;
            labelAsioSampleRateStatus.Text = asioDriverInfo.SupportsSampleRate
                ? $"{sampleRate} Hz supported"
                : $"{sampleRate} Hz not supported";
            labelAsioSampleRateStatus.ForeColor = asioDriverInfo.SupportsSampleRate
                ? Color.LightGreen
                : Color.LightSalmon;
            labelAsioFramesPerBufferValue.Text =
                asioDriverInfo.FramesPerBuffer > 0
                    ? $"{asioDriverInfo.FramesPerBuffer} frames"
                    : "-";
            labelAsioPlaybackLatencyValue.Text =
                asioDriverInfo.PlaybackLatency > 0
                    ? $"{asioDriverInfo.PlaybackLatency} samples"
                    : "-";
        }

        private void ValidateSelectedAsioDriver(int sampleRate)
        {
            if (!string.IsNullOrWhiteSpace(asioDriverInfo.ErrorMessage))
            {
                throw new InvalidOperationException(asioDriverInfo.ErrorMessage);
            }
            if (!asioDriverInfo.SupportsSampleRate)
            {
                throw new InvalidOperationException(
                    $"ASIO driver '{asioDriverInfo.DriverName}' does not support {sampleRate} Hz.");
            }
            if (asioDriverInfo.InputChannels.Count == 0)
            {
                throw new InvalidOperationException(
                    $"ASIO driver '{asioDriverInfo.DriverName}' has no input channels.");
            }
            if (asioDriverInfo.OutputChannels.Count == 0)
            {
                throw new InvalidOperationException(
                    $"ASIO driver '{asioDriverInfo.DriverName}' needs at least two output channels.");
            }
        }
    }
}
