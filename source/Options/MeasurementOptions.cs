using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions : Form
    {
        private readonly ToolTip deviceToolTip = new();
        private WindowsAudioEndpointService? endpointService;
        private Font? normalStatusFont;
        private Font? warningStatusFont;
        private ExpSweepMeasurement? expSweepMeasurement;
        private IReadOnlyList<AudioDeviceInfo> playbackDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioDeviceInfo> recordingDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioEndpointInfo> wasapiCaptureEndpoints = Array.Empty<AudioEndpointInfo>();
        private IReadOnlyList<AudioEndpointInfo> wasapiRenderEndpoints = Array.Empty<AudioEndpointInfo>();
        private IReadOnlyList<AsioDeviceInfo> asioDrivers = Array.Empty<AsioDeviceInfo>();
        private AsioDriverInfo asioDriverInfo = AsioDeviceCatalog.EmptyDriverInfo;
        private bool initializing;
        private string? microphoneCalibration0DegreesPath;
        private string? microphoneCalibration90DegreesPath;
        // Remembers the loopback channel choice while a mono or missing
        // recording device forces the combo to "None", so selecting a stereo
        // device again restores it instead of losing it on the next apply.
        private int? preferredWaveLoopbackChannelOffset;
        private bool updatingWaveLoopbackSelection;
        private string? preferredWasapiCaptureEndpointId;
        private string? preferredWasapiRenderEndpointId;
        private string? preferredWasapiCaptureEndpointName;
        private string? preferredWasapiRenderEndpointName;
        private int preferredWavePlaybackDeviceNumber = -1;
        private int preferredWaveRecordingDeviceNumber = -1;

        private DarkComboBox comboBoxPlaybackDevice => waveAudioBackendPanel.ComboBoxPlaybackDevice;

        private DarkComboBox comboBoxRecordingDevice => waveAudioBackendPanel.ComboBoxRecordingDevice;

        private DarkComboBox comboBoxWaveInputChannel => waveAudioBackendPanel.ComboBoxWaveInputChannel;

        private DarkComboBox comboBoxWaveLoopbackChannel => waveAudioBackendPanel.ComboBoxWaveLoopbackChannel;

        private Label labelPlaybackDevice => waveAudioBackendPanel.LabelPlaybackDevice;

        private Label labelRecordingDevice => waveAudioBackendPanel.LabelRecordingDevice;

        private Label labelWaveInputChannel => waveAudioBackendPanel.LabelWaveInputChannel;

        private Label labelWaveLoopbackChannel => waveAudioBackendPanel.LabelWaveLoopbackChannel;

        private Label labelWaveLoopbackStatus => waveAudioBackendPanel.LabelWaveLoopbackStatus;

        private Label labelDeviceSettings => waveAudioBackendPanel.LabelDeviceSettings;

        private Button buttonDeviceSettings => waveAudioBackendPanel.ButtonDeviceSettings;

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
            TryStartEndpointMonitoring();
            Disposed += (_, _) => DisposeEndpointMonitoring();
        }

        private void TryStartEndpointMonitoring()
        {
            try
            {
                endpointService = new WindowsAudioEndpointService();
                endpointService.EndpointsChanged += HandleEndpointsChanged;
            }
            catch
            {
                endpointService = null;
            }
        }

        private void DisposeEndpointMonitoring()
        {
            if (endpointService == null)
            {
                return;
            }
            endpointService.EndpointsChanged -= HandleEndpointsChanged;
            endpointService.Dispose();
            endpointService = null;
        }

        private void HandleEndpointsChanged()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed)
                    {
                        return;
                    }
                    int inputOffset = GetSelectedWaveInputChannelOffset();
                    int? loopbackOffset = GetSelectedWaveLoopbackChannelOffset();
                    LoadWasapiEndpoints();
                    if (IsSelectedWasapiBackend())
                    {
                        PopulateDeviceControlsForSelectedBackend(inputOffset, loopbackOffset);
                        RefreshSampleRateOptions(GetSelectedSampleRate());
                        UpdateAudioBackendControls();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void WireAudioBackendPanelEvents()
        {
            comboBoxPlaybackDevice.SelectedIndexChanged += comboBoxPlaybackDevice_SelectedIndexChanged;
            comboBoxRecordingDevice.SelectedIndexChanged += comboBoxRecordingDevice_SelectedIndexChanged;
            comboBoxWaveLoopbackChannel.SelectedIndexChanged += comboBoxWaveLoopbackChannel_SelectedIndexChanged;
            comboBoxWaveInputChannel.SelectedIndexChanged += comboBoxWaveInputChannel_SelectedIndexChanged;
            comboBoxAsioDriver.SelectedIndexChanged += comboBoxAsioDriver_SelectedIndexChanged;
            buttonAsioInputProbe.Click += buttonAsioInputProbe_Click;
            buttonAsioControlPanel.Click += buttonAsioControlPanel_Click;
            buttonDeviceSettings.Click += buttonDeviceSettings_Click;

            deviceToolTip.SetToolTip(
                comboBoxWaveLoopbackChannel,
                "Required. Channel carrying the loopback reference signal; every analysis " +
                "is derived from the transfer IR it produces.");
            deviceToolTip.SetToolTip(
                comboBoxAsioLoopbackChannel,
                "Required. ASIO input channel carrying the loopback reference signal; every " +
                "analysis is derived from the transfer IR it produces.");
            deviceToolTip.SetToolTip(
                buttonDeviceSettings,
                "Opens the selected driver's control panel to change the hardware sample " +
                "rate. Falls back to Windows Sound settings when no ASIO driver is selected.");
        }

        internal void Init(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            initializing = true;
            this.expSweepMeasurement = expSweepMeasurement;
            if (expSweepMeasurement.Sweep == null)
            {
                throw new InvalidOperationException("Sweep measurement is not initialized.");
            }
            numericUpDownBits.Value = settings.Bits is 16 or 24 ? settings.Bits : 24;
            preferredWasapiCaptureEndpointId = settings.WasapiCaptureEndpointId;
            preferredWasapiRenderEndpointId = settings.WasapiRenderEndpointId;
            preferredWasapiCaptureEndpointName = settings.WasapiCaptureEndpointName;
            preferredWasapiRenderEndpointName = settings.WasapiRenderEndpointName;
            preferredWavePlaybackDeviceNumber = settings.OutputDeviceNumber;
            preferredWaveRecordingDeviceNumber = settings.InputDeviceNumber;

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
                comboBoxAudioBackend.Items.Add(backend switch
                {
                    AudioBackend.Wave => "MME Compatibility",
                    AudioBackend.WasapiShared => "WASAPI Shared",
                    AudioBackend.WasapiExclusive => "WASAPI Exclusive",
                    _ => backend.ToString()
                });
            }
            comboBoxAudioBackend.SelectedIndex = Enum.IsDefined(settings.AudioBackend)
                ? (int)settings.AudioBackend
                : (int)AudioBackend.Wave;

            playbackDevices = AudioDeviceCatalog.GetPlaybackDevices();
            comboBoxPlaybackDevice.Items.Clear();
            comboBoxPlaybackDevice.Items.AddRange(playbackDevices.Cast<object>().ToArray());
            SelectDeviceOrShowMissing(
                comboBoxPlaybackDevice,
                playbackDevices,
                settings.OutputDeviceNumber);
            ConfigureDropDownWidth(comboBoxPlaybackDevice);
            UpdateComboBoxToolTip(comboBoxPlaybackDevice);

            recordingDevices = AudioDeviceCatalog.GetRecordingDevices();
            LoadWasapiEndpoints();
            PopulateDeviceControlsForSelectedBackend(
                settings.WaveInputChannelOffset,
                settings.WaveLoopbackInputChannelOffset);

            asioDrivers = AsioDeviceCatalog.GetDrivers();
            comboBoxAsioDriver.Items.Clear();
            comboBoxAsioDriver.Items.AddRange(asioDrivers.Cast<object>().ToArray());
            int asioDriverIndex = AsioDeviceCatalog.FindDriverIndex(
                asioDrivers,
                settings.AsioDriverName);
            if (asioDriverIndex < 0 && !string.IsNullOrWhiteSpace(settings.AsioDriverName))
            {
                // The saved driver is currently absent (uninstalled, or the ASIO
                // subsystem is unavailable). Keep it selectable so an apply
                // re-persists the same name instead of another driver or null.
                comboBoxAsioDriver.Items.Add(
                    new AsioDeviceInfo(settings.AsioDriverName, Missing: true));
                asioDriverIndex = comboBoxAsioDriver.Items.Count - 1;
            }
            if (asioDriverIndex >= 0)
            {
                comboBoxAsioDriver.SelectedIndex = asioDriverIndex;
            }
            if (comboBoxAsioDriver.Items.Count > 0)
            {
                ConfigureDropDownWidth(comboBoxAsioDriver);
                UpdateComboBoxToolTip(comboBoxAsioDriver);
            }

            // Clamped: the settings file is not normalized against the control
            // ranges, and (int) truncation used to shave a millisecond off
            // durations that are not exactly representable in binary.
            numericUpDownRequestedDuration.Value = numericUpDownRequestedDuration.ClampValue(
                Math.Round(settings.RequestedDurationSeconds * 1000.0));
            numericUpDownOctaves.Value = numericUpDownOctaves.ClampValue(settings.Octaves);
            numericUpDownComputeDuration.Value = numericUpDownComputeDuration.ClampValue(
                Math.Round(ExponentialSineSweep.CalculateDuration(
                    settings.Octaves,
                    settings.RequestedDurationSeconds,
                    settings.SampleRate) * 1000.0));
            numericUpDownAverageRunCount.Value = Math.Clamp(settings.AverageRunCount, 1, 64);
            checkBoxConfirmEachAverageRun.Checked = settings.ConfirmEachAverageRun;
            microphoneCalibration0DegreesPath = settings.MicrophoneCalibration0DegreesPath;
            microphoneCalibration90DegreesPath = settings.MicrophoneCalibration90DegreesPath;
            UpdateCalibrationButtons();
            RefreshSampleRateOptions(settings.SampleRate);
            initializing = false;
            RefreshAsioDriverInfo(
                settings.AsioInputChannelOffset,
                settings.AsioOutputChannelOffset,
                settings.AsioLoopbackInputChannelOffset);
            UpdateAudioBackendControls();
        }

        internal void SetOptions(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            settings.MicrophoneCalibration0DegreesPath =
                NormalizeCalibrationPath(microphoneCalibration0DegreesPath);
            settings.MicrophoneCalibration90DegreesPath =
                NormalizeCalibrationPath(microphoneCalibration90DegreesPath);

            int sampleRate = GetSelectedSampleRate();
            // Read the bit depth from the control, the single UI source of truth,
            // matching GetSupportedSampleRates. Equal to expSweepMeasurement.Bits
            // while the control is read-only, so this is a no-op today; it stops
            // silently ignoring the control the day it becomes editable.
            int bits = (int)numericUpDownBits.Value;
            PlaybackChannel playbackChannel = (PlaybackChannel)comboBoxChannel.SelectedIndex;
            double requestedDuration = (double)numericUpDownRequestedDuration.Value * 0.001;
            int octaves = (int)numericUpDownOctaves.Value;
            AudioBackend audioBackend = (AudioBackend)comboBoxAudioBackend.SelectedIndex;
            int outputDeviceNumber = comboBoxPlaybackDevice.SelectedItem is AudioDeviceInfo playbackDevice
                ? playbackDevice.DeviceNumber
                : preferredWavePlaybackDeviceNumber;
            int inputDeviceNumber = comboBoxRecordingDevice.SelectedItem is AudioDeviceInfo recordingDevice
                ? recordingDevice.DeviceNumber
                : preferredWaveRecordingDeviceNumber;
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
            int averageRunCount = (int)numericUpDownAverageRunCount.Value;
            bool confirmEachAverageRun = checkBoxConfirmEachAverageRun.Checked;
            string? wasapiCaptureEndpointId =
                comboBoxRecordingDevice.SelectedItem is AudioEndpointInfo captureSelection
                    ? captureSelection.Id
                    : preferredWasapiCaptureEndpointId;
            string? wasapiRenderEndpointId =
                comboBoxPlaybackDevice.SelectedItem is AudioEndpointInfo renderSelection
                    ? renderSelection.Id
                    : preferredWasapiRenderEndpointId;
            if (IsWasapiBackend(audioBackend))
            {
                using var endpointService = new WindowsAudioEndpointService();
                AudioEndpointInfo captureEndpoint = SelectWasapiEndpoint(
                    endpointService.GetCaptureEndpoints(),
                    wasapiCaptureEndpointId,
                    "capture");
                AudioEndpointInfo renderEndpoint = SelectWasapiEndpoint(
                    endpointService.GetRenderEndpoints(),
                    wasapiRenderEndpointId,
                    "render");
                if (!captureEndpoint.IsAvailable || !renderEndpoint.IsAvailable)
                {
                    throw new InvalidOperationException(
                        "A selected WASAPI endpoint is unavailable. Reconnect it or select a replacement.");
                }
                if (audioBackend == AudioBackend.WasapiShared &&
                    captureEndpoint.MixFormat.SampleRate != renderEndpoint.MixFormat.SampleRate)
                {
                    throw new InvalidOperationException(
                        "The default WASAPI capture and render endpoints use different mix rates. " +
                        "Choose endpoints with the same Windows audio format.");
                }
                wasapiCaptureEndpointId = captureEndpoint.Id;
                wasapiRenderEndpointId = renderEndpoint.Id;
                if (audioBackend == AudioBackend.WasapiShared)
                {
                    sampleRate = captureEndpoint.MixFormat.SampleRate;
                }
            }
            if (audioBackend != AudioBackend.Asio &&
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
                averageRunCount,
                confirmEachAverageRun,
                wasapiCaptureEndpointId,
                wasapiRenderEndpointId,
                settings.WasapiBufferMilliseconds,
                comboBoxRecordingDevice.SelectedItem is AudioEndpointInfo captureInfo
                    ? captureInfo.FriendlyName
                    : preferredWasapiCaptureEndpointName,
                comboBoxPlaybackDevice.SelectedItem is AudioEndpointInfo renderInfo
                    ? renderInfo.FriendlyName
                    : preferredWasapiRenderEndpointName);

            settings.WasapiCaptureEndpointId = wasapiCaptureEndpointId;
            settings.WasapiRenderEndpointId = wasapiRenderEndpointId;
            settings.WasapiCaptureEndpointName =
                comboBoxRecordingDevice.SelectedItem is AudioEndpointInfo selectedCapture
                    ? selectedCapture.FriendlyName
                    : preferredWasapiCaptureEndpointName;
            settings.WasapiRenderEndpointName =
                comboBoxPlaybackDevice.SelectedItem is AudioEndpointInfo selectedRender
                    ? selectedRender.FriendlyName
                    : preferredWasapiRenderEndpointName;
            preferredWasapiCaptureEndpointId = wasapiCaptureEndpointId;
            preferredWasapiRenderEndpointId = wasapiRenderEndpointId;
            preferredWasapiCaptureEndpointName = settings.WasapiCaptureEndpointName;
            preferredWasapiRenderEndpointName = settings.WasapiRenderEndpointName;
        }

        private void buttonCalibration0_Click(object? sender, EventArgs e)
        {
            microphoneCalibration0DegreesPath =
                SelectCalibrationFile(microphoneCalibration0DegreesPath);
            UpdateCalibrationButtons();
        }

        private void buttonCalibration90_Click(object? sender, EventArgs e)
        {
            microphoneCalibration90DegreesPath =
                SelectCalibrationFile(microphoneCalibration90DegreesPath);
            UpdateCalibrationButtons();
        }

        private void buttonClearCalibration0_Click(object? sender, EventArgs e)
        {
            microphoneCalibration0DegreesPath = null;
            UpdateCalibrationButtons();
        }

        private void buttonClearCalibration90_Click(object? sender, EventArgs e)
        {
            microphoneCalibration90DegreesPath = null;
            UpdateCalibrationButtons();
        }

        private string? SelectCalibrationFile(string? currentPath)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select microphone calibration file",
                Filter =
                    "Microphone calibration files (*.txt;*.cal;*.frd;*.csv)|*.txt;*.cal;*.frd;*.csv|" +
                    "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                dialog.FileName = currentPath;
                string? directory = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return currentPath;
            }

            // Probe the pick immediately: a file that cannot be parsed would
            // otherwise fail silently at plot time and leave every measurement
            // uncalibrated. The selection is kept so the user can fix the file.
            var probe = new CalibrationFile(dialog.FileName);
            if (!probe.HasData)
            {
                MessageBox.Show(
                    this,
                    "The selected calibration file could not be loaded; measurements " +
                    $"will be shown uncalibrated until it is fixed.\r\n\r\n{probe.LoadError}",
                    "Microphone calibration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return dialog.FileName;
        }

        private void UpdateCalibrationButtons()
        {
            UpdateCalibrationButton(
                buttonCalibration0,
                buttonClearCalibration0,
                microphoneCalibration0DegreesPath);
            UpdateCalibrationButton(
                buttonCalibration90,
                buttonClearCalibration90,
                microphoneCalibration90DegreesPath);
        }

        private void UpdateCalibrationButton(
            Button selectButton,
            Button clearButton,
            string? path)
        {
            string? normalized = NormalizeCalibrationPath(path);
            // Covers a deleted file and an existing-but-unparsable one; both
            // silently disable the correction at plot time otherwise.
            string? problem = normalized == null
                ? null
                : new CalibrationFile(normalized).LoadError;
            selectButton.Text = normalized == null
                ? "Select file..."
                : Path.GetFileName(normalized);
            selectButton.ForeColor = problem != null ? Color.LightSalmon : Color.White;
            clearButton.Enabled = normalized != null;
            deviceToolTip.SetToolTip(
                selectButton,
                normalized == null
                    ? "No calibration file selected."
                    : problem ?? normalized);
            deviceToolTip.SetToolTip(
                clearButton,
                normalized == null
                    ? "No calibration file selected."
                    : "Clear selected calibration file.");
        }

        private static string? NormalizeCalibrationPath(string? path) =>
            string.IsNullOrWhiteSpace(path) ? null : path;

        private void numericUpDownRequestedDuration_ValueChanged(object sender, EventArgs e)
        {
            // Computed from the values shown in the panel, not from the last
            // generated sweep's state (which is stale until the next run).
            numericUpDownComputeDuration.Value = numericUpDownComputeDuration.ClampValue(
                Math.Round(ExponentialSineSweep.CalculateDuration(
                    (int)numericUpDownOctaves.Value,
                    (double)numericUpDownRequestedDuration.Value * 0.001,
                    GetSelectedSampleRate()) * 1000.0));
        }

        private void comboBoxAudioBackend_SelectedIndexChanged(object sender, EventArgs e) =>
            HandleAudioConfigurationChanged();

        private void comboBoxWaveInputChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            // The microphone channel choice changes how many channels the
            // device must open, and therefore which sample rates it supports.
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxPlaybackDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxPlaybackDevice);
            if (comboBoxPlaybackDevice.SelectedItem is AudioEndpointInfo endpoint)
            {
                preferredWasapiRenderEndpointId = endpoint.Id;
            }
            else if (comboBoxPlaybackDevice.SelectedItem is AudioDeviceInfo device)
            {
                preferredWavePlaybackDeviceNumber = device.DeviceNumber;
            }
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxRecordingDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxRecordingDevice);
            if (comboBoxRecordingDevice.SelectedItem is AudioEndpointInfo endpoint)
            {
                preferredWasapiCaptureEndpointId = endpoint.Id;
                FillWasapiChannelControls(
                    GetSelectedWaveInputChannelOffset(),
                    GetSelectedWaveLoopbackChannelOffset());
            }
            else if (comboBoxRecordingDevice.SelectedItem is AudioDeviceInfo device)
            {
                preferredWaveRecordingDeviceNumber = device.DeviceNumber;
            }
            UpdateWaveLoopbackControls();
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxWaveLoopbackChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            if (!updatingWaveLoopbackSelection)
            {
                preferredWaveLoopbackChannelOffset = GetSelectedWaveLoopbackChannelOffset();
            }
            UpdateWaveLoopbackControls();
            RefreshSampleRateOptions(GetSelectedSampleRate());
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
            bool useWasapi = IsSelectedWasapiBackend();
            waveAudioBackendPanel.Visible = !useAsio;
            asioAudioBackendPanel.Visible = useAsio;
            comboBoxPlaybackDevice.Enabled = !useAsio;
            comboBoxRecordingDevice.Enabled = !useAsio;
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
            labelWaveInputChannel.Enabled = !useAsio;
            labelWaveLoopbackChannel.Enabled = !useAsio;
            labelWaveLoopbackStatus.Enabled = !useAsio;
            labelDeviceSettings.Visible = useWasapi;
            buttonDeviceSettings.Visible = useWasapi;
            buttonDeviceSettings.Enabled = useWasapi;
            labelAsioLoopbackChannel.Enabled = useAsio;
            if (useWasapi)
            {
                labelPlaybackDevice.Text = "Output endpoint";
                labelRecordingDevice.Text = "Input endpoint";
                labelWaveInputChannel.Text = "Microphone channel";
                labelWaveLoopbackChannel.Text = "Loopback channel";
            }
            else
            {
                labelPlaybackDevice.Text = "Playback device";
                labelRecordingDevice.Text = "Recording device";
                labelWaveInputChannel.Text = "Wave input channel";
                labelWaveLoopbackChannel.Text = "Wave loopback channel";
            }
            UpdateWaveLoopbackControls();
        }

        private static AudioEndpointInfo SelectWasapiEndpoint(
            IReadOnlyList<AudioEndpointInfo> endpoints,
            string? preferredId,
            string direction)
        {
            AudioEndpointInfo? endpoint = endpoints.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, preferredId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(preferredId) && endpoint == null)
            {
                throw new InvalidOperationException(
                    $"The saved WASAPI {direction} endpoint is unavailable. " +
                    "Reconnect it or choose a replacement before applying settings.");
            }
            endpoint ??= endpoints.FirstOrDefault(candidate => candidate.IsDefault);
            return endpoint ?? throw new InvalidOperationException(
                $"No active WASAPI {direction} endpoint is available.");
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

        private void buttonDeviceSettings_Click(object? sender, EventArgs e)
        {
            try
            {
                int preferredSampleRate = GetSelectedSampleRate();
                int preferredInputOffset = GetSelectedWaveInputChannelOffset();
                int? preferredLoopbackOffset = GetSelectedWaveLoopbackChannelOffset();
                if (comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver)
                {
                    AsioDeviceCatalog.ShowControlPanel(asioDriver.DriverName);
                    LoadWasapiEndpoints();
                    PopulateDeviceControlsForSelectedBackend(
                        preferredInputOffset,
                        preferredLoopbackOffset);
                    RefreshSampleRateOptions(preferredSampleRate);
                    UpdateAudioBackendControls();
                    return;
                }

                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Device Settings",
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
            // With no driver selected at all there is nothing to preserve; with a
            // named driver that cannot be opened (busy/uninstalled) the saved
            // channel routing must not collapse to channel 1 / "None" and get
            // re-persisted by the next apply.
            bool preserveOffsets = !string.IsNullOrWhiteSpace(asioDriverInfo.DriverName);
            comboBoxAsioInputChannel.SelectedIndex = SelectAsioChannelIndex(
                comboBoxAsioInputChannel,
                asioDriverInfo.InputChannels,
                preferredInputOffset,
                preserveOffsets);
            int loopbackIndex = FindInputChannelOptionIndex(
                comboBoxAsioLoopbackChannel,
                preferredLoopbackOffset);
            if (loopbackIndex < 0 &&
                preserveOffsets &&
                preferredLoopbackOffset is int missingLoopbackOffset)
            {
                comboBoxAsioLoopbackChannel.Items.Add(new InputChannelOption(
                    missingLoopbackOffset,
                    $"{missingLoopbackOffset + 1}: (missing)"));
                loopbackIndex = comboBoxAsioLoopbackChannel.Items.Count - 1;
            }
            comboBoxAsioLoopbackChannel.SelectedIndex = Math.Max(0, loopbackIndex);
            comboBoxAsioOutputChannel.SelectedIndex = SelectAsioChannelIndex(
                comboBoxAsioOutputChannel,
                asioDriverInfo.OutputChannels,
                preferredOutputOffset,
                preserveOffsets);

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

        private void LoadWasapiEndpoints()
        {
            try
            {
                if (endpointService != null)
                {
                    wasapiCaptureEndpoints = endpointService.GetCaptureEndpoints();
                    wasapiRenderEndpoints = endpointService.GetRenderEndpoints();
                }
                else
                {
                    using var temporaryService = new WindowsAudioEndpointService();
                    wasapiCaptureEndpoints = temporaryService.GetCaptureEndpoints();
                    wasapiRenderEndpoints = temporaryService.GetRenderEndpoints();
                }
            }
            catch
            {
                wasapiCaptureEndpoints = Array.Empty<AudioEndpointInfo>();
                wasapiRenderEndpoints = Array.Empty<AudioEndpointInfo>();
            }
        }

        private void PopulateDeviceControlsForSelectedBackend(
            int preferredInputOffset,
            int? preferredLoopbackOffset)
        {
            bool wasInitializing = initializing;
            initializing = true;
            try
            {
                if (IsSelectedWasapiBackend())
                {
                    PopulateWasapiEndpointCombo(
                        comboBoxPlaybackDevice,
                        wasapiRenderEndpoints,
                        preferredWasapiRenderEndpointId,
                        preferredWasapiRenderEndpointName,
                        NAudio.CoreAudioApi.DataFlow.Render);
                    PopulateWasapiEndpointCombo(
                        comboBoxRecordingDevice,
                        wasapiCaptureEndpoints,
                        preferredWasapiCaptureEndpointId,
                        preferredWasapiCaptureEndpointName,
                        NAudio.CoreAudioApi.DataFlow.Capture);
                    FillWasapiChannelControls(preferredInputOffset, preferredLoopbackOffset);
                }
                else
                {
                    comboBoxPlaybackDevice.Items.Clear();
                    comboBoxPlaybackDevice.Items.AddRange(playbackDevices.Cast<object>().ToArray());
                    SelectDeviceOrShowMissing(
                        comboBoxPlaybackDevice,
                        playbackDevices,
                        preferredWavePlaybackDeviceNumber);
                    comboBoxRecordingDevice.Items.Clear();
                    comboBoxRecordingDevice.Items.AddRange(recordingDevices.Cast<object>().ToArray());
                    SelectDeviceOrShowMissing(
                        comboBoxRecordingDevice,
                        recordingDevices,
                        preferredWaveRecordingDeviceNumber);
                    FillWaveChannelControls(preferredInputOffset, preferredLoopbackOffset);
                }

                ConfigureDropDownWidth(comboBoxPlaybackDevice);
                ConfigureDropDownWidth(comboBoxRecordingDevice);
                UpdateComboBoxToolTip(comboBoxPlaybackDevice);
                UpdateComboBoxToolTip(comboBoxRecordingDevice);
            }
            finally
            {
                initializing = wasInitializing;
            }
        }

        private static void PopulateWasapiEndpointCombo(
            DarkComboBox comboBox,
            IReadOnlyList<AudioEndpointInfo> endpoints,
            string? preferredId,
            string? preferredName,
            NAudio.CoreAudioApi.DataFlow direction)
        {
            comboBox.Items.Clear();
            comboBox.Items.AddRange(endpoints.Cast<object>().ToArray());
            int index = FindWasapiEndpointIndex(endpoints, preferredId);
            if (index < 0 && !string.IsNullOrWhiteSpace(preferredId))
            {
                comboBox.Items.Add(CreateUnavailableEndpoint(
                    preferredId,
                    preferredName,
                    direction));
                index = comboBox.Items.Count - 1;
            }
            if (index < 0)
            {
                index = endpoints.ToList().FindIndex(endpoint => endpoint.IsDefault);
            }
            if (index < 0 && comboBox.Items.Count > 0)
            {
                index = 0;
            }
            comboBox.SelectedIndex = index;
        }

        private static int FindWasapiEndpointIndex(
            IReadOnlyList<AudioEndpointInfo> endpoints,
            string? endpointId)
        {
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (string.Equals(endpoints[i].Id, endpointId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        private static AudioEndpointInfo CreateUnavailableEndpoint(
            string endpointId,
            string? friendlyName,
            NAudio.CoreAudioApi.DataFlow direction) =>
            new(
                string.IsNullOrWhiteSpace(friendlyName) ? endpointId : friendlyName,
                endpointId,
                direction,
                NAudio.CoreAudioApi.DeviceState.NotPresent,
                new NAudio.Wave.WaveFormat(44_100, 16, 1),
                0,
                false);

        private void FillWasapiChannelControls(
            int preferredInputOffset,
            int? preferredLoopbackOffset)
        {
            int channelCount = comboBoxRecordingDevice.SelectedItem is AudioEndpointInfo endpoint
                ? endpoint.Channels
                : 0;
            int preservedChannelCount = Math.Max(
                preferredInputOffset + 1,
                preferredLoopbackOffset.GetValueOrDefault(-1) + 1);
            channelCount = Math.Max(channelCount, preservedChannelCount);
            InputChannelOption[] channels = Enumerable.Range(0, channelCount)
                .Select(index => new InputChannelOption(index, $"Input {index + 1}"))
                .ToArray();

            comboBoxWaveInputChannel.Items.Clear();
            comboBoxWaveInputChannel.Items.AddRange(channels);
            comboBoxWaveInputChannel.SelectedIndex = channelCount > 0
                ? Math.Clamp(preferredInputOffset, 0, channelCount - 1)
                : -1;
            comboBoxWaveLoopbackChannel.Items.Clear();
            comboBoxWaveLoopbackChannel.Items.Add(new InputChannelOption(null, "None"));
            comboBoxWaveLoopbackChannel.Items.AddRange(channels);
            comboBoxWaveLoopbackChannel.SelectedIndex = preferredLoopbackOffset is int offset &&
                offset >= 0 && offset < channelCount
                    ? offset + 1
                    : 0;
            preferredWaveLoopbackChannelOffset = preferredLoopbackOffset;
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
            preferredWaveLoopbackChannelOffset = preferredLoopbackOffset;
            UpdateWaveLoopbackControls();
        }

        private void UpdateWaveLoopbackControls()
        {
            if (comboBoxWaveLoopbackChannel == null)
            {
                return;
            }

            if (IsSelectedWasapiBackend())
            {
                comboBoxWaveLoopbackChannel.Enabled = true;
                labelWaveLoopbackStatus.Font = NormalStatusFont;
                AudioEndpointInfo? capture = comboBoxRecordingDevice.SelectedItem as AudioEndpointInfo;
                AudioEndpointInfo? render = comboBoxPlaybackDevice.SelectedItem as AudioEndpointInfo;
                if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
                {
                    labelWaveLoopbackStatus.Text =
                        "⚠ A saved endpoint is unavailable. Reconnect it or select a replacement.";
                    labelWaveLoopbackStatus.ForeColor = Color.Gold;
                    return;
                }
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.WasapiExclusive)
                {
                    int selectedRate = GetSelectedSampleRate();
                    int bits = (int)numericUpDownBits.Value;
                    bool supported = IsExclusiveFormatSupported(
                        capture.Id,
                        render.Id,
                        selectedRate,
                        bits,
                        Math.Max(
                            GetSelectedWaveInputChannelOffset(),
                            GetSelectedWaveLoopbackChannelOffset() ?? 0) + 1,
                        GetSelectedPlaybackChannelCount());
                    labelWaveLoopbackStatus.Text = supported
                        ? $"Exclusive: {selectedRate:N0} Hz / {bits}-bit opens directly " +
                            "on both endpoints."
                        : $"⚠ Exclusive format {selectedRate:N0} Hz / {bits}-bit is not supported by both endpoints.";
                    labelWaveLoopbackStatus.ForeColor = supported
                        ? Color.LightGray
                        : Color.LightSalmon;
                    return;
                }
                string compatibility = capture.MixFormat.SampleRate == render.MixFormat.SampleRate
                    ? ""
                    : " — sample rates do not match";
                labelWaveLoopbackStatus.Text =
                    $"Shared mix format: {capture.MixFormat.SampleRate:N0} Hz / " +
                    $"{capture.MixFormat.BitsPerSample}-bit capture, " +
                    $"{render.MixFormat.BitsPerSample}-bit render{compatibility}. " +
                    "Windows may convert render audio; timing remains loopback-referenced.";
                labelWaveLoopbackStatus.ForeColor = compatibility.Length == 0
                    ? Color.LightGray
                    : Color.LightSalmon;
                return;
            }

            bool loopbackSelected =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null };
            bool supportsLoopback = SelectedRecordingDeviceSupportsWaveLoopback();
            if (!supportsLoopback && comboBoxWaveLoopbackChannel.Items.Count > 0)
            {
                // Forced, not a user choice: the preferred offset is kept so a
                // stereo device restores it below.
                SetWaveLoopbackSelection(0);
                loopbackSelected = false;
            }
            else if (supportsLoopback &&
                !loopbackSelected &&
                preferredWaveLoopbackChannelOffset is int rememberedOffset)
            {
                int rememberedIndex = FindInputChannelOptionIndex(
                    comboBoxWaveLoopbackChannel,
                    rememberedOffset);
                if (rememberedIndex >= 0)
                {
                    SetWaveLoopbackSelection(rememberedIndex);
                    loopbackSelected = true;
                }
            }
            comboBoxWaveLoopbackChannel.Enabled =
                comboBoxAudioBackend.SelectedIndex != (int)AudioBackend.Asio &&
                supportsLoopback;
            // The loopback channel is mandatory: without it there is no transfer IR and no
            // measurement can run. Make an unset loopback impossible to overlook.
            if (!loopbackSelected)
            {
                labelWaveLoopbackStatus.Font = WarningStatusFont;
                labelWaveLoopbackStatus.Text = supportsLoopback
                    ? "⚠ Loopback channel is REQUIRED. Select the channel carrying the " +
                        "loopback reference; measurements cannot run without it."
                    : "⚠ Loopback channel is REQUIRED. Select a stereo recording device, " +
                        "then choose its channel.";
                labelWaveLoopbackStatus.ForeColor = Color.Gold;
                return;
            }

            labelWaveLoopbackStatus.Font = NormalStatusFont;
            labelWaveLoopbackStatus.Text = supportsLoopback
                ? "Stereo input available for Wave loopback."
                : "Select a stereo recording device.";
            labelWaveLoopbackStatus.ForeColor = supportsLoopback
                ? Color.LightGray
                : Color.LightSalmon;
        }

        private void SetWaveLoopbackSelection(int index)
        {
            if (comboBoxWaveLoopbackChannel.SelectedIndex == index)
            {
                return;
            }

            bool wasUpdating = updatingWaveLoopbackSelection;
            updatingWaveLoopbackSelection = true;
            try
            {
                comboBoxWaveLoopbackChannel.SelectedIndex = index;
            }
            finally
            {
                updatingWaveLoopbackSelection = wasUpdating;
            }
        }

        private Font NormalStatusFont =>
            normalStatusFont ??= labelWaveLoopbackStatus.Font;

        private Font WarningStatusFont =>
            warningStatusFont ??= new Font(NormalStatusFont, FontStyle.Bold);

        private bool SelectedRecordingDeviceSupportsWaveLoopback() =>
            comboBoxRecordingDevice.SelectedItem is AudioDeviceInfo { Channels: >= 2 } or
                AudioEndpointInfo { Channels: >= 2, IsAvailable: true };

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

            return -1;
        }

        // A persisted device that is not currently present stays visible as a
        // "(missing)" entry with its original number, so an apply cannot
        // silently re-target the configuration to another device.
        private static void SelectDeviceOrShowMissing(
            DarkComboBox comboBox,
            IReadOnlyList<AudioDeviceInfo> devices,
            int deviceNumber)
        {
            int index = AudioDeviceCatalog.FindDeviceIndex(devices, deviceNumber);
            if (index >= 0)
            {
                comboBox.SelectedIndex = index;
                return;
            }

            comboBox.Items.Add(AudioDeviceCatalog.CreateMissingDevice(deviceNumber));
            comboBox.SelectedIndex = comboBox.Items.Count - 1;
        }

        // Same idea for ASIO channels: an offset the driver does not currently
        // report (fewer channels, or the driver failed to open — e.g. it is in
        // use by another application) must survive the panel round-trip.
        private static int SelectAsioChannelIndex(
            DarkComboBox comboBox,
            IReadOnlyList<AsioChannelInfo> channels,
            int preferredOffset,
            bool preserveMissingOffset)
        {
            int index = AsioDeviceCatalog.FindChannelIndex(channels, preferredOffset);
            if (index >= 0 || !preserveMissingOffset)
            {
                return index;
            }

            comboBox.Items.Add(new AsioChannelInfo(preferredOffset, "(missing)"));
            return comboBox.Items.Count - 1;
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
                // The docked panel can be closed while the ~1 s capture runs;
                // touching the disposed form would throw out of an async void
                // handler and kill the process.
                if (IsDisposed)
                {
                    return;
                }
                MessageBox.Show(
                    this,
                    FormatAsioInputProbeResults(results),
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                if (IsDisposed)
                {
                    return;
                }
                MessageBox.Show(
                    this,
                    exception.Message,
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed)
                {
                    buttonAsioInputProbe.Text = "Test ASIO Inputs";
                    UpdateAudioBackendControls();
                }
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

            int preferredSampleRate = GetSelectedSampleRate();
            PopulateDeviceControlsForSelectedBackend(
                GetSelectedWaveInputChannelOffset(),
                GetSelectedWaveLoopbackChannelOffset());
            RefreshSampleRateOptions(preferredSampleRate);
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                // Opening the ASIO driver is a synchronous COM instantiation
                // that can take seconds; don't pay it for Wave-side changes.
                RefreshAsioDriverInfo(
                    GetSelectedAsioInputChannelOffset(),
                    GetSelectedAsioOutputChannelOffset(),
                    GetSelectedAsioLoopbackInputChannelOffset());
            }
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

            if (IsSelectedWasapiBackend())
            {
                AudioEndpointInfo? capture = comboBoxRecordingDevice.SelectedItem as AudioEndpointInfo;
                AudioEndpointInfo? render = comboBoxPlaybackDevice.SelectedItem as AudioEndpointInfo;
                if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
                {
                    return [];
                }
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.WasapiShared)
                {
                    return capture.MixFormat.SampleRate == render.MixFormat.SampleRate
                        ? [capture.MixFormat.SampleRate]
                        : [];
                }

                int captureChannels = Math.Max(
                    GetSelectedWaveInputChannelOffset(),
                    GetSelectedWaveLoopbackChannelOffset() ?? 0) + 1;
                int renderChannels = GetSelectedPlaybackChannelCount();
                int bits = (int)numericUpDownBits.Value;
                return SampleRateCatalog.GetCandidateRates()
                    .Where(rate => IsExclusiveFormatSupported(
                        capture.Id,
                        render.Id,
                        rate,
                        bits,
                        captureChannels,
                        renderChannels))
                    .ToArray();
            }

            return AudioDeviceCatalog.GetSupportedWaveSampleRates(
                GetSelectedPlaybackDeviceNumber(),
                GetSelectedRecordingDeviceNumber(),
                GetSelectedPlaybackChannelCount(),
                GetSelectedWaveRecordingChannelCount(),
                (int)numericUpDownBits.Value);
        }

        private int GetSelectedWaveInputChannelOffset() =>
            comboBoxWaveInputChannel.SelectedItem is InputChannelOption option
                ? option.Offset ?? 0
                : 0;

        private int? GetSelectedWaveLoopbackChannelOffset() =>
            comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption option
                ? option.Offset
                : null;

        private static bool IsExclusiveFormatSupported(
            string captureEndpointId,
            string renderEndpointId,
            int sampleRate,
            int bits,
            int captureChannels,
            int renderChannels)
        {
            try
            {
                return WasapiFormatSupport.CheckExclusive(
                    captureEndpointId,
                    renderEndpointId,
                    sampleRate,
                    bits,
                    captureChannels,
                    renderChannels).Supported;
            }
            catch
            {
                return false;
            }
        }

        private bool IsSelectedWasapiBackend() =>
            comboBoxAudioBackend.SelectedIndex is
                (int)AudioBackend.WasapiShared or (int)AudioBackend.WasapiExclusive;

        private static bool IsWasapiBackend(AudioBackend backend) =>
            backend is AudioBackend.WasapiShared or AudioBackend.WasapiExclusive;

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
            // The microphone on channel 2 (offset 1) needs a 2-channel format
            // even without a loopback selection.
            int loopbackChannels =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null }
                    ? 2
                    : 1;
            return Math.Max(GetSelectedWaveInputChannelOffset() + 1, loopbackChannels);
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
