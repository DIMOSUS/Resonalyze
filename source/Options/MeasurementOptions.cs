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
    public partial class MeasurementOptions : Form
    {
        // Sentinel for the "loopback shares the microphone device" entry in the Wave loopback
        // device list (distinct from -1, which is a real "default device" entry).
        private const int SharedLoopbackDeviceSentinel = int.MinValue;
        private readonly ToolTip deviceToolTip = new();
        private Font? normalStatusFont;
        private Font? warningStatusFont;
        private ExpSweepMeasurement? expSweepMeasurement;
        private IReadOnlyList<AudioDeviceInfo> playbackDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioDeviceInfo> recordingDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AsioDeviceInfo> asioDrivers = Array.Empty<AsioDeviceInfo>();
        private AsioDriverInfo asioDriverInfo = AsioDeviceCatalog.EmptyDriverInfo;
        private bool initializing;

        private DarkComboBox comboBoxPlaybackDevice => waveAudioBackendPanel.ComboBoxPlaybackDevice;

        private DarkComboBox comboBoxRecordingDevice => waveAudioBackendPanel.ComboBoxRecordingDevice;

        private DarkComboBox comboBoxWaveLoopbackDevice => waveAudioBackendPanel.ComboBoxWaveLoopbackDevice;

        private DarkComboBox comboBoxWaveInputChannel => waveAudioBackendPanel.ComboBoxWaveInputChannel;

        private DarkComboBox comboBoxWaveLoopbackChannel => waveAudioBackendPanel.ComboBoxWaveLoopbackChannel;

        private Label labelPlaybackDevice => waveAudioBackendPanel.LabelPlaybackDevice;

        private Label labelRecordingDevice => waveAudioBackendPanel.LabelRecordingDevice;

        private Label labelWaveLoopbackDevice => waveAudioBackendPanel.LabelWaveLoopbackDevice;

        private Label labelWaveInputChannel => waveAudioBackendPanel.LabelWaveInputChannel;

        private Label labelWaveLoopbackChannel => waveAudioBackendPanel.LabelWaveLoopbackChannel;

        private Label labelWaveLoopbackStatus => waveAudioBackendPanel.LabelWaveLoopbackStatus;

        private DarkComboBox comboBoxAsioDriver => asioAudioBackendPanel.ComboBoxAsioDriver;

        private DarkComboBox comboBoxAsioInputChannel => asioAudioBackendPanel.ComboBoxAsioInputChannel;

        private DarkComboBox comboBoxAsioOutputChannel => asioAudioBackendPanel.ComboBoxAsioOutputChannel;

        private DarkComboBox comboBoxAsioLoopbackChannel => asioAudioBackendPanel.ComboBoxAsioLoopbackChannel;

        private Button buttonAsioInputProbe => asioAudioBackendPanel.ButtonAsioInputProbe;

        private Button buttonAsioControlPanel => asioAudioBackendPanel.ButtonAsioControlPanel;

        private Label labelAsioDriver => asioAudioBackendPanel.LabelAsioDriver;

        private Label labelAsioInputChannel => asioAudioBackendPanel.LabelAsioInputChannel;

        private Label labelAsioOutputChannel => asioAudioBackendPanel.LabelAsioOutputChannel;

        private Label labelAsioLoopbackChannel => asioAudioBackendPanel.LabelAsioLoopbackChannel;

        private Label labelAsioSampleRate => asioAudioBackendPanel.LabelAsioSampleRate;

        private Label labelAsioSampleRateStatus => asioAudioBackendPanel.LabelAsioSampleRateStatus;

        private Label labelAsioPlaybackLatency => asioAudioBackendPanel.LabelAsioPlaybackLatency;

        private Label labelAsioPlaybackLatencyValue => asioAudioBackendPanel.LabelAsioPlaybackLatencyValue;

        public MeasurementOptions()
        {
            InitializeComponent();
            WireAudioBackendPanelEvents();
        }

        private void WireAudioBackendPanelEvents()
        {
            comboBoxPlaybackDevice.SelectedIndexChanged += comboBoxPlaybackDevice_SelectedIndexChanged;
            comboBoxRecordingDevice.SelectedIndexChanged += comboBoxRecordingDevice_SelectedIndexChanged;
            comboBoxWaveLoopbackDevice.SelectedIndexChanged += comboBoxWaveLoopbackDevice_SelectedIndexChanged;
            comboBoxWaveLoopbackChannel.SelectedIndexChanged += comboBoxWaveLoopbackChannel_SelectedIndexChanged;
            comboBoxAsioDriver.SelectedIndexChanged += comboBoxAsioDriver_SelectedIndexChanged;
            buttonAsioInputProbe.Click += buttonAsioInputProbe_Click;
            buttonAsioControlPanel.Click += buttonAsioControlPanel_Click;

            deviceToolTip.SetToolTip(
                comboBoxWaveLoopbackChannel,
                "Required. Channel carrying the loopback reference signal; every analysis " +
                "is derived from the transfer IR it produces.");
            deviceToolTip.SetToolTip(
                comboBoxAsioLoopbackChannel,
                "Required. ASIO input channel carrying the loopback reference signal; every " +
                "analysis is derived from the transfer IR it produces.");
        }

        internal void Init(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            initializing = true;
            this.expSweepMeasurement = expSweepMeasurement;
            ExponentialSineSweep sweep = expSweepMeasurement.Sweep
                ?? throw new InvalidOperationException("Sweep measurement is not initialized.");
            numericUpDownBits.Value = settings.Bits is 16 or 24 ? settings.Bits : 24;

            comboBoxChannel.Items.Clear();
            foreach (PlaybackChannel channel in Enum.GetValues<PlaybackChannel>())
            {
                comboBoxChannel.Items.Add(channel.ToString());
            }
            comboBoxChannel.SelectedIndex = GetPlaybackChannelIndex(
                settings.PlaybackChannel);

            comboBoxAudioBackend.Items.Clear();
            foreach (AudioBackend backend in Enum.GetValues<AudioBackend>())
            {
                comboBoxAudioBackend.Items.Add(backend.ToString());
            }
            comboBoxAudioBackend.SelectedIndex = Enum.IsDefined(settings.AudioBackend)
                ? (int)settings.AudioBackend
                : (int)AudioBackend.Wave;

            playbackDevices = AudioDeviceCatalog.GetPlaybackDevices();
            comboBoxPlaybackDevice.Items.Clear();
            comboBoxPlaybackDevice.Items.AddRange(playbackDevices.Cast<object>().ToArray());
            comboBoxPlaybackDevice.SelectedIndex = AudioDeviceCatalog.FindDeviceIndex(
                playbackDevices,
                settings.OutputDeviceNumber);
            ConfigureDropDownWidth(comboBoxPlaybackDevice);
            UpdateComboBoxToolTip(comboBoxPlaybackDevice);

            recordingDevices = AudioDeviceCatalog.GetRecordingDevices();
            comboBoxRecordingDevice.Items.Clear();
            comboBoxRecordingDevice.Items.AddRange(recordingDevices.Cast<object>().ToArray());
            comboBoxRecordingDevice.SelectedIndex = AudioDeviceCatalog.FindDeviceIndex(
                recordingDevices,
                settings.InputDeviceNumber);
            ConfigureDropDownWidth(comboBoxRecordingDevice);
            UpdateComboBoxToolTip(comboBoxRecordingDevice);

            comboBoxWaveLoopbackDevice.Items.Clear();
            comboBoxWaveLoopbackDevice.Items.Add(new AudioDeviceInfo(
                SharedLoopbackDeviceSentinel,
                "Same as microphone device"));
            comboBoxWaveLoopbackDevice.Items.AddRange(recordingDevices.Cast<object>().ToArray());
            comboBoxWaveLoopbackDevice.SelectedIndex = settings.WaveLoopbackDeviceNumber.HasValue
                ? 1 + AudioDeviceCatalog.FindDeviceIndex(
                    recordingDevices,
                    settings.WaveLoopbackDeviceNumber.Value)
                : 0;
            ConfigureDropDownWidth(comboBoxWaveLoopbackDevice);
            UpdateComboBoxToolTip(comboBoxWaveLoopbackDevice);

            FillWaveChannelControls(
                settings.WaveInputChannelOffset,
                settings.WaveLoopbackInputChannelOffset);

            asioDrivers = AsioDeviceCatalog.GetDrivers();
            comboBoxAsioDriver.Items.Clear();
            if (asioDrivers.Count > 0)
            {
                comboBoxAsioDriver.Items.AddRange(asioDrivers.Cast<object>().ToArray());
                comboBoxAsioDriver.SelectedIndex = AsioDeviceCatalog.FindDriverIndex(
                    asioDrivers,
                    settings.AsioDriverName);
                ConfigureDropDownWidth(comboBoxAsioDriver);
                UpdateComboBoxToolTip(comboBoxAsioDriver);
            }

            numericUpDownRequestedDuration.Value = (int)(settings.RequestedDurationSeconds * 1000.0);
            numericUpDownComputeDuration.Value =
                (int)(sweep.CalculateDuration(settings.RequestedDurationSeconds) * 1000.0);
            numericUpDownOctaves.Value = settings.Octaves;
            numericUpDownAverageRunCount.Value = Math.Clamp(settings.AverageRunCount, 1, 64);
            checkBoxConfirmEachAverageRun.Checked = settings.ConfirmEachAverageRun;
            RefreshSampleRateOptions(settings.SampleRate);
            initializing = false;
            RefreshAsioDriverInfo(
                settings.AsioInputChannelOffset,
                settings.AsioOutputChannelOffset,
                settings.AsioLoopbackInputChannelOffset);
            UpdateAudioBackendControls();
        }

        public void SetOptions(ExpSweepMeasurement expSweepMeasurement)
        {
            int sampleRate = GetSelectedSampleRate();
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
            if (audioBackend == AudioBackend.Wave)
            {
                ValidateSelectedWaveLoopback();
                ValidateSelectedWaveSampleRate(sampleRate);
            }
            int asioInputChannelOffset =
                comboBoxAsioInputChannel.SelectedItem is AsioChannelInfo inputChannel
                    ? inputChannel.Offset
                    : 0;
            int? asioLoopbackInputChannelOffset =
                comboBoxAsioLoopbackChannel.SelectedItem is InputChannelOption asioLoopbackChannel
                    ? asioLoopbackChannel.Offset
                    : null;
            if (audioBackend == AudioBackend.Asio &&
                asioLoopbackInputChannelOffset.HasValue &&
                asioLoopbackInputChannelOffset.Value == asioInputChannelOffset)
            {
                throw new InvalidOperationException(
                    "Microphone and loopback inputs must use different ASIO channels.");
            }
            int asioOutputChannelOffset =
                comboBoxAsioOutputChannel.SelectedItem is AsioChannelInfo outputChannel
                    ? outputChannel.Offset
                    : 0;
            int waveInputChannelOffset =
                comboBoxWaveInputChannel.SelectedItem is InputChannelOption waveInput
                    ? waveInput.Offset ?? 0
                    : 0;
            int? waveLoopbackInputChannelOffset =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption waveLoopback
                    ? waveLoopback.Offset
                    : null;
            int? waveLoopbackDeviceNumber = GetSelectedWaveLoopbackDeviceNumber();
            int averageRunCount = (int)numericUpDownAverageRunCount.Value;
            bool confirmEachAverageRun = checkBoxConfirmEachAverageRun.Checked;
            bool separateWaveLoopbackDevice =
                waveLoopbackDeviceNumber.HasValue &&
                waveLoopbackDeviceNumber.Value != inputDeviceNumber;
            // The same channel is only a conflict when both come from one device; separate
            // devices may legitimately both use, say, the left channel.
            if (audioBackend == AudioBackend.Wave &&
                !separateWaveLoopbackDevice &&
                waveLoopbackInputChannelOffset.HasValue &&
                waveLoopbackInputChannelOffset.Value == waveInputChannelOffset)
            {
                throw new InvalidOperationException(
                    "Microphone and loopback inputs must use different Wave channels.");
            }

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
                asioOutputChannelOffset,
                waveInputChannelOffset,
                waveLoopbackInputChannelOffset,
                asioLoopbackInputChannelOffset,
                waveLoopbackDeviceNumber,
                averageRunCount,
                confirmEachAverageRun);
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
            HandleAudioConfigurationChanged();

        private void comboBoxPlaybackDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxPlaybackDevice);
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxRecordingDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxRecordingDevice);
            UpdateWaveLoopbackControls();
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxWaveLoopbackChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateWaveLoopbackControls();
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxWaveLoopbackDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxWaveLoopbackDevice);
            UpdateWaveLoopbackControls();
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private int? GetSelectedWaveLoopbackDeviceNumber() =>
            comboBoxWaveLoopbackDevice.SelectedItem is AudioDeviceInfo device &&
            device.DeviceNumber != SharedLoopbackDeviceSentinel
                ? device.DeviceNumber
                : null;

        // The Wave loopback uses a different device than the microphone.
        private bool UsesSeparateWaveLoopbackDeviceSelected()
        {
            int? loopbackDevice = GetSelectedWaveLoopbackDeviceNumber();
            return loopbackDevice.HasValue &&
                loopbackDevice.Value != GetSelectedRecordingDeviceNumber();
        }

        private void comboBoxAsioDriver_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxAsioDriver);
            RefreshSampleRateOptions(GetSelectedSampleRate());
            RefreshAsioDriverInfo(
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            UpdateAudioBackendControls();
        }

        private void comboBoxChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxSampleRate_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing || comboBoxAudioBackend.SelectedIndex != (int)AudioBackend.Asio)
            {
                return;
            }

            RefreshAsioDriverInfo(
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            UpdateAudioBackendControls();
        }

        private void UpdateAudioBackendControls()
        {
            bool useAsio =
                comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio;
            waveAudioBackendPanel.Visible = !useAsio;
            asioAudioBackendPanel.Visible = useAsio;
            comboBoxPlaybackDevice.Enabled = !useAsio;
            comboBoxRecordingDevice.Enabled = !useAsio;
            comboBoxWaveLoopbackDevice.Enabled = !useAsio;
            comboBoxWaveInputChannel.Enabled = !useAsio;
            comboBoxWaveLoopbackChannel.Enabled = !useAsio &&
                SelectedRecordingDeviceSupportsWaveLoopback();
            comboBoxAsioDriver.Enabled = useAsio && asioDrivers.Count > 0;
            buttonAsioControlPanel.Enabled =
                useAsio && comboBoxAsioDriver.SelectedItem is AsioDeviceInfo;
            buttonAsioInputProbe.Enabled =
                useAsio &&
                comboBoxAsioDriver.SelectedItem is AsioDeviceInfo &&
                asioDriverInfo.InputChannels.Count > 0 &&
                asioDriverInfo.OutputChannels.Count > 0;
            comboBoxAsioInputChannel.Enabled =
                useAsio && asioDriverInfo.InputChannels.Count > 0;
            comboBoxAsioLoopbackChannel.Enabled =
                useAsio && asioDriverInfo.InputChannels.Count > 0;
            comboBoxAsioOutputChannel.Enabled =
                useAsio && asioDriverInfo.OutputChannels.Count > 0;
            labelAsioDriver.Enabled = useAsio;
            labelAsioInputChannel.Enabled = useAsio;
            labelAsioOutputChannel.Enabled = useAsio;
            labelAsioSampleRate.Enabled = useAsio;
            labelAsioSampleRateStatus.Enabled = useAsio;
            labelAsioPlaybackLatency.Enabled = useAsio;
            labelAsioPlaybackLatencyValue.Enabled = useAsio;
            labelPlaybackDevice.Enabled = !useAsio;
            labelRecordingDevice.Enabled = !useAsio;
            labelWaveLoopbackDevice.Enabled = !useAsio;
            labelWaveInputChannel.Enabled = !useAsio;
            labelWaveLoopbackChannel.Enabled = !useAsio;
            labelWaveLoopbackStatus.Enabled = !useAsio;
            labelAsioLoopbackChannel.Enabled = useAsio;
            UpdateWaveLoopbackControls();
        }

        private void ConfigureDropDownWidth(DarkComboBox comboBox)
        {
            int maxWidth = comboBox.Width;
            Font font = comboBox.Font ?? Font;
            using Graphics graphics = comboBox.CreateGraphics();
            foreach (object item in comboBox.Items)
            {
                string text = comboBox.GetItemText(item) ?? string.Empty;
                int width = TextRenderer.MeasureText(graphics, text, font).Width + SystemInformation.VerticalScrollBarWidth;
                maxWidth = Math.Max(maxWidth, width);
            }

            comboBox.DropDownWidth = maxWidth;
        }

        private void UpdateComboBoxToolTip(DarkComboBox comboBox)
        {
            string text = comboBox.SelectedItem != null
                ? comboBox.GetItemText(comboBox.SelectedItem) ?? string.Empty
                : string.Empty;
            deviceToolTip.SetToolTip(comboBox, text);
        }

        private void buttonAsioControlPanel_Click(object? sender, EventArgs e)
        {
            if (comboBoxAsioDriver.SelectedItem is not AsioDeviceInfo asioDriver)
            {
                return;
            }

            try
            {
                int preferredSampleRate = GetSelectedSampleRate();
                int preferredInputOffset = GetSelectedAsioInputChannelOffset();
                int preferredOutputOffset = GetSelectedAsioOutputChannelOffset();
                int? preferredLoopbackOffset = GetSelectedAsioLoopbackInputChannelOffset();
                AsioDeviceCatalog.ShowControlPanel(asioDriver.DriverName);
                RefreshSampleRateOptions(preferredSampleRate);
                RefreshAsioDriverInfo(
                    preferredInputOffset,
                    preferredOutputOffset,
                    preferredLoopbackOffset);
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
            int preferredOutputOffset,
            int? preferredLoopbackOffset)
        {
            string? driverName = comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver
                ? asioDriver.DriverName
                : null;
            asioDriverInfo = AsioDeviceCatalog.GetDriverInfo(
                driverName,
                GetSelectedSampleRate());

            comboBoxAsioInputChannel.Items.Clear();
            comboBoxAsioLoopbackChannel.Items.Clear();
            comboBoxAsioOutputChannel.Items.Clear();
            comboBoxAsioInputChannel.Items.AddRange(
                asioDriverInfo.InputChannels.Cast<object>().ToArray());
            comboBoxAsioLoopbackChannel.Items.Add(new InputChannelOption(null, "None"));
            comboBoxAsioLoopbackChannel.Items.AddRange(
                asioDriverInfo.InputChannels
                    .Select(channel => new InputChannelOption(channel.Offset, channel.ToString()))
                    .Cast<object>()
                    .ToArray());
            comboBoxAsioOutputChannel.Items.AddRange(
                asioDriverInfo.OutputChannels.Cast<object>().ToArray());
            comboBoxAsioInputChannel.SelectedIndex = AsioDeviceCatalog.FindChannelIndex(
                asioDriverInfo.InputChannels,
                preferredInputOffset);
            comboBoxAsioLoopbackChannel.SelectedIndex = FindInputChannelOptionIndex(
                comboBoxAsioLoopbackChannel,
                preferredLoopbackOffset);
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
                labelAsioPlaybackLatencyValue.Text = "-";
                return;
            }

            int sampleRate = GetSelectedSampleRate();
            labelAsioSampleRateStatus.Text = asioDriverInfo.SupportsSampleRate
                ? $"{sampleRate} Hz supported"
                : $"{sampleRate} Hz not supported";
            labelAsioSampleRateStatus.ForeColor = asioDriverInfo.SupportsSampleRate
                ? Color.LightGreen
                : Color.LightSalmon;
            labelAsioPlaybackLatencyValue.Text =
                asioDriverInfo.PlaybackLatency > 0
                    ? $"{asioDriverInfo.PlaybackLatency} samples"
                    : "-";
        }

        private static int GetPlaybackChannelIndex(PlaybackChannel channel)
        {
            return Enum.IsDefined(channel)
                ? (int)channel
                : (int)PlaybackChannel.Mono;
        }

        private void FillWaveChannelControls(
            int preferredInputOffset,
            int? preferredLoopbackOffset)
        {
            InputChannelOption[] requiredChannels =
            [
                new InputChannelOption(0, "Left"),
                new InputChannelOption(1, "Right")
            ];
            comboBoxWaveInputChannel.Items.Clear();
            comboBoxWaveInputChannel.Items.AddRange(requiredChannels);
            comboBoxWaveInputChannel.SelectedIndex =
                preferredInputOffset == 1 ? 1 : 0;

            comboBoxWaveLoopbackChannel.Items.Clear();
            comboBoxWaveLoopbackChannel.Items.Add(new InputChannelOption(null, "None"));
            comboBoxWaveLoopbackChannel.Items.AddRange(requiredChannels);
            comboBoxWaveLoopbackChannel.SelectedIndex =
                preferredLoopbackOffset.HasValue
                    ? preferredLoopbackOffset.Value == 1 ? 2 : 1
                    : 0;
            UpdateWaveLoopbackControls();
        }

        private void UpdateWaveLoopbackControls()
        {
            if (comboBoxWaveLoopbackChannel == null)
            {
                return;
            }

            bool loopbackSelected =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null };
            bool supportsLoopback = SelectedRecordingDeviceSupportsWaveLoopback();
            if (!supportsLoopback && comboBoxWaveLoopbackChannel.Items.Count > 0)
            {
                comboBoxWaveLoopbackChannel.SelectedIndex = 0;
                loopbackSelected = false;
            }
            comboBoxWaveLoopbackChannel.Enabled =
                comboBoxAudioBackend.SelectedIndex != (int)AudioBackend.Asio &&
                supportsLoopback;
            // A separate loopback device cannot be sample-accurately synchronised; make that
            // warning impossible to miss (bold, amber, with a warning marker).
            if (supportsLoopback && loopbackSelected && UsesSeparateWaveLoopbackDeviceSelected())
            {
                labelWaveLoopbackStatus.Text =
                    "⚠ Separate loopback device is NOT sample-accurate.\r\n" +
                    "Frequency response is usable, but phase, group delay and time " +
                    "alignment are degraded. Use the microphone device or ASIO for accurate timing.";
                labelWaveLoopbackStatus.ForeColor = Color.Gold;
                labelWaveLoopbackStatus.Font = WarningStatusFont;
                return;
            }

            // The loopback channel is mandatory: without it there is no transfer IR and no
            // measurement can run. Make an unset loopback impossible to overlook.
            if (!loopbackSelected)
            {
                labelWaveLoopbackStatus.Font = WarningStatusFont;
                labelWaveLoopbackStatus.Text = supportsLoopback
                    ? "⚠ Loopback channel is REQUIRED. Select the channel carrying the " +
                        "loopback reference; measurements cannot run without it."
                    : "⚠ Loopback channel is REQUIRED. Select a stereo recording device, " +
                        "or a separate loopback device, then choose its channel.";
                labelWaveLoopbackStatus.ForeColor = Color.Gold;
                return;
            }

            labelWaveLoopbackStatus.Font = NormalStatusFont;
            labelWaveLoopbackStatus.Text = supportsLoopback
                ? "Stereo input available for Wave loopback."
                : "Select a stereo recording device, or a separate loopback device.";
            labelWaveLoopbackStatus.ForeColor = supportsLoopback
                ? Color.LightGray
                : Color.LightSalmon;
        }

        private Font NormalStatusFont =>
            normalStatusFont ??= labelWaveLoopbackStatus.Font;

        private Font WarningStatusFont =>
            warningStatusFont ??= new Font(NormalStatusFont, FontStyle.Bold);

        private bool SelectedRecordingDeviceSupportsWaveLoopback()
        {
            // A separate loopback device provides the loopback on its own channel, so the
            // microphone device no longer needs to be stereo.
            if (UsesSeparateWaveLoopbackDeviceSelected())
            {
                return true;
            }

            return comboBoxRecordingDevice.SelectedItem is AudioDeviceInfo { Channels: >= 2 };
        }

        private void ValidateSelectedWaveLoopback()
        {
            bool loopbackSelected =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null };
            if (loopbackSelected && !SelectedRecordingDeviceSupportsWaveLoopback())
            {
                throw new InvalidOperationException(
                    "Wave loopback requires a selected stereo recording device.");
            }
        }

        private void ValidateSelectedWaveSampleRate(int sampleRate)
        {
            IReadOnlyList<int> supportedRates = GetSupportedSampleRates();
            if (supportedRates.Count > 0 && !supportedRates.Contains(sampleRate))
            {
                throw new InvalidOperationException(
                    $"Wave devices do not support {sampleRate} Hz for the current configuration.");
            }
        }

        private static int FindInputChannelOptionIndex(
            DarkComboBox comboBox,
            int? offset)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is InputChannelOption option &&
                    option.Offset == offset)
                {
                    return i;
                }
            }

            return 0;
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

        private async void buttonAsioInputProbe_Click(object? sender, EventArgs e)
        {
            if (comboBoxAsioDriver.SelectedItem is not AsioDeviceInfo driver)
            {
                return;
            }

            try
            {
                buttonAsioInputProbe.Enabled = false;
                buttonAsioInputProbe.Text = "Testing...";
                int outputChannelOffset =
                    comboBoxAsioOutputChannel.SelectedItem is AsioChannelInfo output
                        ? output.Offset
                        : 0;
                IReadOnlyList<AsioInputProbeChannelResult> results =
                    await AsioInputProbe.CaptureAsync(
                        driver.DriverName,
                        GetSelectedSampleRate(),
                        outputChannelOffset,
                        milliseconds: 1000,
                        CancellationToken.None);
                MessageBox.Show(
                    this,
                    FormatAsioInputProbeResults(results),
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                buttonAsioInputProbe.Text = "Test ASIO Inputs";
                UpdateAudioBackendControls();
            }
        }

        private static string FormatAsioInputProbeResults(
            IReadOnlyList<AsioInputProbeChannelResult> results)
        {
            if (results.Count == 0)
            {
                return "No ASIO input channels were recorded.";
            }

            return string.Join(
                Environment.NewLine,
                results.Select(result =>
                    $"{result.Offset + 1}: {result.Name}  " +
                    $"peak {result.PeakDbFs:0.0} dBFS, " +
                    $"RMS {result.RmsDbFs:0.0} dBFS, " +
                    $"corr ch1 {result.CorrelationToFirst:0.000}"));
        }

        private void HandleAudioConfigurationChanged()
        {
            if (initializing)
            {
                return;
            }

            RefreshSampleRateOptions(GetSelectedSampleRate());
            RefreshAsioDriverInfo(
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            UpdateAudioBackendControls();
        }

        private void RefreshSampleRateOptions(int preferredSampleRate)
        {
            int fallbackSampleRate = 44_100;
            IReadOnlyList<int> supportedRates = GetSupportedSampleRates();
            int[] availableRates = supportedRates.Count > 0
                ? supportedRates.ToArray()
                : [fallbackSampleRate];

            int selectedSampleRate = availableRates.Contains(preferredSampleRate)
                ? preferredSampleRate
                : availableRates[0];

            bool wasInitializing = initializing;
            initializing = true;
            comboBoxSampleRate.Items.Clear();
            comboBoxSampleRate.Items.AddRange(
                availableRates
                    .Select(rate => (object)rate)
                    .ToArray());
            comboBoxSampleRate.SelectedIndex = FindSampleRateIndex(
                availableRates,
                selectedSampleRate);
            initializing = wasInitializing;
        }

        private IReadOnlyList<int> GetSupportedSampleRates()
        {
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                return AsioDeviceCatalog.GetSupportedSampleRates(
                    comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver
                        ? asioDriver.DriverName
                        : null);
            }

            int playbackDevice = GetSelectedPlaybackDeviceNumber();
            int playbackChannels = GetSelectedPlaybackChannelCount();
            int bits = (int)numericUpDownBits.Value;

            if (!UsesSeparateWaveLoopbackDeviceSelected())
            {
                return AudioDeviceCatalog.GetSupportedWaveSampleRates(
                    playbackDevice,
                    GetSelectedRecordingDeviceNumber(),
                    playbackChannels,
                    GetSelectedWaveRecordingChannelCount(),
                    bits);
            }

            // Separate loopback device: a rate must be supported by the playback device, the
            // microphone device, and the loopback device. Each is captured on its own channel,
            // so only that channel's count is required from each input device.
            IReadOnlyList<int> microphoneRates = AudioDeviceCatalog.GetSupportedWaveSampleRates(
                playbackDevice,
                GetSelectedRecordingDeviceNumber(),
                playbackChannels,
                GetSelectedWaveInputChannelOffset() + 1,
                bits);
            IReadOnlyList<int> loopbackRates = AudioDeviceCatalog.GetSupportedWaveSampleRates(
                playbackDevice,
                GetSelectedWaveLoopbackDeviceNumber()!.Value,
                playbackChannels,
                (GetSelectedWaveLoopbackChannelOffset() ?? 0) + 1,
                bits);
            return microphoneRates.Intersect(loopbackRates).ToArray();
        }

        private int GetSelectedWaveInputChannelOffset() =>
            comboBoxWaveInputChannel.SelectedItem is InputChannelOption option
                ? option.Offset ?? 0
                : 0;

        private int? GetSelectedWaveLoopbackChannelOffset() =>
            comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption option
                ? option.Offset
                : null;

        private int GetSelectedSampleRate()
        {
            return comboBoxSampleRate.SelectedItem is int sampleRate
                ? sampleRate
                : 44_100;
        }

        private int GetSelectedPlaybackDeviceNumber()
        {
            return comboBoxPlaybackDevice.SelectedItem is AudioDeviceInfo device
                ? device.DeviceNumber
                : -1;
        }

        private int GetSelectedRecordingDeviceNumber()
        {
            return comboBoxRecordingDevice.SelectedItem is AudioDeviceInfo device
                ? device.DeviceNumber
                : -1;
        }

        private int GetSelectedPlaybackChannelCount()
        {
            PlaybackChannel channel = comboBoxChannel.SelectedIndex >= 0
                ? (PlaybackChannel)comboBoxChannel.SelectedIndex
                : PlaybackChannel.Mono;
            return channel == PlaybackChannel.Mono ? 1 : 2;
        }

        private int GetSelectedWaveRecordingChannelCount()
        {
            return comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null }
                ? 2
                : 1;
        }

        private int GetSelectedAsioInputChannelOffset()
        {
            return comboBoxAsioInputChannel.SelectedItem is AsioChannelInfo channel
                ? channel.Offset
                : 0;
        }

        private int GetSelectedAsioOutputChannelOffset()
        {
            return comboBoxAsioOutputChannel.SelectedItem is AsioChannelInfo channel
                ? channel.Offset
                : 0;
        }

        private int? GetSelectedAsioLoopbackInputChannelOffset()
        {
            return comboBoxAsioLoopbackChannel.SelectedItem is InputChannelOption option
                ? option.Offset
                : null;
        }

        private static int FindSampleRateIndex(
            IReadOnlyList<int> sampleRates,
            int sampleRate)
        {
            for (int i = 0; i < sampleRates.Count; i++)
            {
                if (sampleRates[i] == sampleRate)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
