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

using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions : Form
    {
        private readonly WrappingToolTip deviceToolTip = new();
        // Raised on any calibration change so the host persists it immediately, not on Apply.
        internal event Action<CalibrationSelection>? CalibrationChanged;
        // Only the audio-backend group (backend, rate, bit depth, device panel) waits for Apply; everything else raises this.
        public event Action? SweepSettingsChanged;

        internal sealed record CalibrationSelection(
            string? MicrophoneCalibration0DegreesPath,
            IReadOnlyList<MicrophoneCalibrationDefinition> AdditionalMicrophoneCalibrations,
            SplCalibration? SplCalibration);
        private WindowsAudioEndpointService? endpointService;
        private Font? normalStatusFont;
        private Font? warningStatusFont;
        private ExpSweepMeasurement? expSweepMeasurement;
        private IReadOnlyList<AudioDeviceInfo> playbackDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioDeviceInfo> recordingDevices = Array.Empty<AudioDeviceInfo>();
        private IReadOnlyList<AudioEndpointDescriptor> wasapiCaptureEndpoints = Array.Empty<AudioEndpointDescriptor>();
        private IReadOnlyList<AudioEndpointDescriptor> wasapiRenderEndpoints = Array.Empty<AudioEndpointDescriptor>();
        private IReadOnlyList<AsioDeviceInfo> asioDrivers = Array.Empty<AsioDeviceInfo>();
        private AsioDriverInfo asioDriverInfo = AsioDeviceCatalog.EmptyDriverInfo;
        private bool sampleRateProbeFailed;
        private int? sampleRateFellBackFrom;
        private bool initializing;
        private string? microphoneCalibration0DegreesPath;
        private List<MicrophoneCalibrationDefinition> additionalMicrophoneCalibrations = [];
        // The rig's choice, frozen into the file by the run.
        private string? microphoneCalibrationId = MicrophoneCalibrationIds.ZeroDegrees;
        private List<ArrayMicrophoneDefinition> waveArrayMicrophones = [];
        private List<ArrayMicrophoneDefinition> asioArrayMicrophones = [];
        // Null until the array is next edited; see MeasurementSettingsFile.ArrayMatchesDevice.
        private string? waveArrayDeviceId;
        private string? asioArrayDeviceId;
        // Null under the designer constructor, which disables the Calibrate button.
        private readonly IAudioSessionFactory? audioSessionFactory;
        private SplCalibration? splCalibration;
        // Remembered while a mono/missing device forces "None", so a stereo device restores it.
        private int? preferredWaveLoopbackChannelOffset;
        private bool updatingWaveLoopbackSelection;
        private bool updatingSweepBand;
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
            InitializeProtectiveHighPassControls();
            WireAudioBackendPanelEvents();
            TryStartEndpointMonitoring();
            Disposed += (_, _) => DisposeEndpointMonitoring();
        }

        public MeasurementOptions(IAudioSessionFactory audioSessionFactory)
            : this()
        {
            this.audioSessionFactory = audioSessionFactory ??
                throw new ArgumentNullException(nameof(audioSessionFactory));
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
            // Moving the mic or loopback onto an array input makes a position unrecordable; the button must show it.
            comboBoxWaveInputChannel.SelectedIndexChanged +=
                (_, _) => UpdateArrayMicrophoneButton();
            comboBoxWaveLoopbackChannel.SelectedIndexChanged +=
                (_, _) => UpdateArrayMicrophoneButton();
            comboBoxAsioInputChannel.SelectedIndexChanged +=
                (_, _) => UpdateArrayMicrophoneButton();
            comboBoxAsioLoopbackChannel.SelectedIndexChanged +=
                (_, _) => UpdateArrayMicrophoneButton();
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
                "Opens Windows Sound settings for the selected WASAPI endpoints.");
            deviceToolTip.SetToolTip(
                buttonSaveSweepFile,
                "Writes the sweep above to a 24-bit WAV file, exactly as a " +
                "measurement would play it: same band, pace, sample rate and " +
                "playback channel, and the same 6 dB of headroom (-6 dBFS peak), " +
                "with a second of silence before and after it.");
            deviceToolTip.SetToolTip(
                comboBoxProtectiveHighPassKind,
                "Protective high-pass configured in the external DSP between the " +
                "sound-card output and the loudspeaker. Resonalyze removes its " +
                "magnitude and phase from the loopback-referenced transfer IR.");
            deviceToolTip.SetToolTip(
                labelProtectiveHighPass,
                "Optional protective high-pass configured in the external DSP.");
            numericUpDownProtectiveHighPassFrequency.ApplyToolTip(
                deviceToolTip,
                "Protective high-pass corner frequency (Hz). The loopback must be " +
                "captured before the external DSP, directly from the sound-card output.");
            deviceToolTip.SetToolTip(
                comboBoxProtectiveHighPassSlope,
                "Protective high-pass slope. Compensation is limited to 40 dB; " +
                "deeper in the stop band the measurement cannot recover signal " +
                "that the protection filter buried in noise.");
        }

        private void InitializeProtectiveHighPassControls()
        {
            comboBoxProtectiveHighPassKind.Items.AddRange(
            [
                ProtectiveHighPassKind.Off,
                ProtectiveHighPassKind.Butterworth,
                ProtectiveHighPassKind.LinkwitzRiley
            ]);
            comboBoxProtectiveHighPassKind.Format += (_, args) =>
            {
                if (args.ListItem is ProtectiveHighPassKind kind)
                {
                    args.Value = kind switch
                    {
                        ProtectiveHighPassKind.LinkwitzRiley => "Linkwitz-Riley",
                        _ => kind.ToString()
                    };
                }
            };
            comboBoxProtectiveHighPassSlope.Format += (_, args) =>
            {
                if (args.ListItem is int slope)
                {
                    args.Value = $"{slope} dB/oct";
                }
            };
            comboBoxProtectiveHighPassKind.SelectedIndexChanged += (_, _) =>
            {
                PopulateProtectiveHighPassSlopes();
                UpdateProtectiveHighPassAvailability();
                RaiseSweepSettingsChanged();
            };
            numericUpDownProtectiveHighPassFrequency.ValueChanged += (_, _) =>
                RaiseSweepSettingsChanged();
            comboBoxProtectiveHighPassSlope.SelectedIndexChanged += (_, _) =>
                RaiseSweepSettingsChanged();

            comboBoxProtectiveHighPassKind.SelectedItem = ProtectiveHighPassKind.Off;
            PopulateProtectiveHighPassSlopes(preferredSlope: 24);
            UpdateProtectiveHighPassAvailability();
        }

        private ProtectiveHighPassKind SelectedProtectiveHighPassKind =>
            comboBoxProtectiveHighPassKind.SelectedItem is ProtectiveHighPassKind kind
                ? kind
                : ProtectiveHighPassKind.Off;

        private void PopulateProtectiveHighPassSlopes(int? preferredSlope = null)
        {
            int? previousSlope = preferredSlope ??
                (comboBoxProtectiveHighPassSlope.SelectedItem is int slope ? slope : null);
            comboBoxProtectiveHighPassSlope.Items.Clear();
            foreach (int supportedSlope in ProtectiveHighPassConfiguration.SupportedSlopes(
                SelectedProtectiveHighPassKind))
            {
                comboBoxProtectiveHighPassSlope.Items.Add(supportedSlope);
            }

            int index = previousSlope.HasValue
                ? comboBoxProtectiveHighPassSlope.Items.IndexOf(previousSlope.Value)
                : -1;
            comboBoxProtectiveHighPassSlope.SelectedIndex = index >= 0
                ? index
                : comboBoxProtectiveHighPassSlope.Items.IndexOf(24);
        }

        private void UpdateProtectiveHighPassAvailability()
        {
            bool enabled = SelectedProtectiveHighPassKind != ProtectiveHighPassKind.Off;
            numericUpDownProtectiveHighPassFrequency.Enabled = enabled;
            comboBoxProtectiveHighPassSlope.Enabled = enabled;
        }

        private ProtectiveHighPassConfiguration ReadProtectiveHighPass() =>
            ProtectiveHighPassConfiguration.Normalize(
                new ProtectiveHighPassConfiguration(
                    SelectedProtectiveHighPassKind,
                    (double)numericUpDownProtectiveHighPassFrequency.Value,
                    comboBoxProtectiveHighPassSlope.SelectedItem is int slope ? slope : 24));

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
                // Keep an absent saved driver selectable so Apply re-persists the same name.
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

            // Clamped and rounded: the file is not normalized to control ranges, and (int) truncation loses a millisecond.
            (double lowFrequencyHz, double highFrequencyHz) = settings.ResolveBand(settings.SampleRate);
            numericUpDownLowFrequency.Value = numericUpDownLowFrequency.ClampValue(
                Math.Round(lowFrequencyHz));
            numericUpDownHighFrequency.Value = numericUpDownHighFrequency.ClampValue(
                Math.Max((double)numericUpDownLowFrequency.Value + 1.0, Math.Round(highFrequencyHz)));
            double perOctaveMs = ExponentialSineSweep.OctavePaceForTotalDuration(
                lowFrequencyHz,
                highFrequencyHz,
                settings.RequestedDurationSeconds,
                settings.SampleRate) * 1000.0;
            numericUpDownRequestedDuration.Value = numericUpDownRequestedDuration.ClampValue(
                perOctaveMs > 0 ? Math.Round(perOctaveMs) : 100.0);
            ProtectiveHighPassConfiguration protectiveHighPass =
                ProtectiveHighPassConfiguration.Normalize(
                    new ProtectiveHighPassConfiguration(
                        settings.ProtectiveHighPassKind,
                        settings.ProtectiveHighPassFrequencyHz,
                        settings.ProtectiveHighPassSlopeDbPerOctave));
            comboBoxProtectiveHighPassKind.SelectedItem = protectiveHighPass.Kind;
            numericUpDownProtectiveHighPassFrequency.Value =
                numericUpDownProtectiveHighPassFrequency.ClampValue(
                    protectiveHighPass.FrequencyHz);
            PopulateProtectiveHighPassSlopes(protectiveHighPass.SlopeDbPerOctave);
            UpdateProtectiveHighPassAvailability();
            numericUpDownAverageRunCount.Value = Math.Clamp(settings.AverageRunCount, 1, 64);
            microphoneCalibration0DegreesPath = settings.MicrophoneCalibration0DegreesPath;
            additionalMicrophoneCalibrations = settings.AdditionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList();
            microphoneCalibrationId = settings.MicrophoneCalibrationId;
            RefreshMicrophoneCalibrationCombo();
            waveArrayMicrophones = settings.WaveArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            asioArrayMicrophones = settings.AsioArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            waveArrayDeviceId = settings.WaveArrayDeviceId;
            asioArrayDeviceId = settings.AsioArrayDeviceId;
            splCalibration = settings.SplCalibration;
            UpdateCalibrationButtons();
            // With ASIO the driver probe supplies the rate list, so it must run before the rate control is filled.
            RefreshAsioDriverInfo(
                settings.SampleRate,
                settings.AsioInputChannelOffset,
                settings.AsioOutputChannelOffset,
                settings.AsioLoopbackInputChannelOffset);
            RefreshSampleRateOptions(settings.SampleRate);
            initializing = false;
            UpdateAudioBackendControls();
            RefreshSweepBandPreview();
        }

        internal void SetOptions(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            settings.MicrophoneCalibration0DegreesPath =
                NormalizeCalibrationPath(microphoneCalibration0DegreesPath);
            settings.MicrophoneCalibrationId = microphoneCalibrationId;
            settings.AdditionalMicrophoneCalibrations = additionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList();
            settings.WaveArrayMicrophones = waveArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.AsioArrayMicrophones = asioArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.WaveArrayDeviceId = waveArrayDeviceId;
            settings.AsioArrayDeviceId = asioArrayDeviceId;
            settings.SplCalibration = splCalibration;

            int sampleRate = GetSelectedSampleRate();
            // The control is the UI source of truth (read-only today, equal to expSweepMeasurement.Bits).
            int bits = (int)numericUpDownBits.Value;
            PlaybackChannel playbackChannel = GetSelectedPlaybackChannel();
            double lowFrequencyHz = (double)numericUpDownLowFrequency.Value;
            double highFrequencyHz = (double)numericUpDownHighFrequency.Value;
            double requestedDuration = GetRequestedDurationSeconds(sampleRate);
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
            if (audioBackend != AudioBackend.Asio)
            {
                ValidateSelectedWaveLoopback();
            }
            if (audioBackend == AudioBackend.Wave)
            {
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
            string? wasapiCaptureEndpointId =
                comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor captureSelection
                    ? captureSelection.Id
                    : preferredWasapiCaptureEndpointId;
            string? wasapiRenderEndpointId =
                comboBoxPlaybackDevice.SelectedItem is AudioEndpointDescriptor renderSelection
                    ? renderSelection.Id
                    : preferredWasapiRenderEndpointId;
            if (audioBackend.IsWasapi())
            {
                using var endpointService = new WindowsAudioEndpointService();
                AudioEndpointDescriptor captureEndpoint = SelectWasapiEndpoint(
                    endpointService.GetCaptureEndpoints(),
                    wasapiCaptureEndpointId,
                    "capture");
                AudioEndpointDescriptor renderEndpoint = SelectWasapiEndpoint(
                    endpointService.GetRenderEndpoints(),
                    wasapiRenderEndpointId,
                    "render");
                if (!captureEndpoint.IsAvailable || !renderEndpoint.IsAvailable)
                {
                    throw new InvalidOperationException(
                        "A selected WASAPI endpoint is unavailable. Reconnect it or select a replacement.");
                }
                if (audioBackend == AudioBackend.WasapiShared &&
                    captureEndpoint.PreferredFormat.SampleRate != renderEndpoint.PreferredFormat.SampleRate)
                {
                    throw new InvalidOperationException(
                        "The default WASAPI capture and render endpoints use different mix rates. " +
                        "Choose endpoints with the same Windows audio format.");
                }
                wasapiCaptureEndpointId = captureEndpoint.Id;
                wasapiRenderEndpointId = renderEndpoint.Id;
                if (audioBackend == AudioBackend.WasapiShared)
                {
                    sampleRate = captureEndpoint.PreferredFormat.SampleRate;
                }
                else if (audioBackend == AudioBackend.WasapiExclusive)
                {
                    // Exclusive reads the combo, which can be empty (no common rate) and then falls back to 44.1 kHz;
                    // persisting that would persist a refused format. Checked after availability so a gone endpoint keeps its message.
                    SampleRateOptions.ValidateSelectedRate(
                        GetSupportedSampleRates(),
                        sampleRate,
                        "The WASAPI Exclusive endpoints");
                }
            }
            if (audioBackend != AudioBackend.Asio &&
                waveLoopbackInputChannelOffset.HasValue &&
                waveLoopbackInputChannelOffset.Value == waveInputChannelOffset)
            {
                throw new InvalidOperationException(
                    "Microphone and loopback inputs must use different Wave channels.");
            }

            expSweepMeasurement.Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(
                    lowFrequencyHz,
                    highFrequencyHz,
                    sampleRate,
                    bits,
                    requestedDuration,
                    playbackChannel),
                new SweepAudioConfiguration(
                    Backend: audioBackend,
                    OutputDeviceNumber: outputDeviceNumber,
                    InputDeviceNumber: inputDeviceNumber,
                    WaveInputChannelOffset: waveInputChannelOffset,
                    WaveLoopbackInputChannelOffset: waveLoopbackInputChannelOffset,
                    AsioDriverName: asioDriverName,
                    AsioInputChannelOffset: asioInputChannelOffset,
                    AsioLoopbackInputChannelOffset: asioLoopbackInputChannelOffset,
                    AsioOutputChannelOffset: asioOutputChannelOffset,
                    WasapiCaptureEndpointId: wasapiCaptureEndpointId,
                    WasapiRenderEndpointId: wasapiRenderEndpointId,
                    WasapiCaptureEndpointName:
                        comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor captureInfo
                            ? captureInfo.DisplayName
                            : preferredWasapiCaptureEndpointName,
                    WasapiRenderEndpointName:
                        comboBoxPlaybackDevice.SelectedItem is AudioEndpointDescriptor renderInfo
                            ? renderInfo.DisplayName
                            : preferredWasapiRenderEndpointName,
                    WasapiBufferMilliseconds: settings.WasapiBufferMilliseconds,
                    // Include the array, or the applied configuration differs from what the settings build for the next run.
                    WaveArrayInputChannelOffsets: audioBackend == AudioBackend.Asio
                        ? []
                        : SelectedReachableArrayChannels(),
                    AsioArrayInputChannelOffsets: audioBackend == AudioBackend.Asio
                        ? SelectedReachableArrayChannels()
                        : []),
                new SweepAveragingConfiguration(averageRunCount),
                ReadProtectiveHighPass()));

            settings.LowFrequencyHz = lowFrequencyHz;
            settings.HighFrequencyHz = highFrequencyHz;
            settings.WasapiCaptureEndpointId = wasapiCaptureEndpointId;
            settings.WasapiRenderEndpointId = wasapiRenderEndpointId;
            settings.WasapiCaptureEndpointName =
                comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor selectedCapture
                    ? selectedCapture.DisplayName
                    : preferredWasapiCaptureEndpointName;
            settings.WasapiRenderEndpointName =
                comboBoxPlaybackDevice.SelectedItem is AudioEndpointDescriptor selectedRender
                    ? selectedRender.DisplayName
                    : preferredWasapiRenderEndpointName;
            preferredWasapiCaptureEndpointId = wasapiCaptureEndpointId;
            preferredWasapiRenderEndpointId = wasapiRenderEndpointId;
            preferredWasapiCaptureEndpointName = settings.WasapiCaptureEndpointName;
            preferredWasapiRenderEndpointName = settings.WasapiRenderEndpointName;

            expSweepMeasurement.SplCalibration = splCalibration;
        }

        /// <summary>Writes the immediately-applied half of the panel; the backend group waits for <see cref="SetOptions"/>.</summary>
        /// <remarks>Nothing is pushed into <see cref="ExpSweepMeasurement"/>: its <c>Init</c> discards the measured result.</remarks>
        internal void ApplySweepSettings(
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            settings.LowFrequencyHz = (double)numericUpDownLowFrequency.Value;
            settings.HighFrequencyHz = (double)numericUpDownHighFrequency.Value;
            // Paced against the applied rate: an uncommitted rate must not leak into the sweep.
            settings.RequestedDurationSeconds = GetRequestedDurationSeconds(settings.SampleRate);
            settings.PlaybackChannel = GetSelectedPlaybackChannel();
            settings.AverageRunCount = (int)numericUpDownAverageRunCount.Value;
            ProtectiveHighPassConfiguration protectiveHighPass = ReadProtectiveHighPass();
            settings.ProtectiveHighPassKind = protectiveHighPass.Kind;
            settings.ProtectiveHighPassFrequencyHz = protectiveHighPass.FrequencyHz;
            settings.ProtectiveHighPassSlopeDbPerOctave =
                protectiveHighPass.SlopeDbPerOctave;
            // Array is capture routing, so it reaches the settings on the same apply that reopens the device.
            settings.WaveArrayMicrophones = waveArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.AsioArrayMicrophones = asioArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.WaveArrayDeviceId = waveArrayDeviceId;
            settings.AsioArrayDeviceId = asioArrayDeviceId;
            settings.MicrophoneCalibrationId = microphoneCalibrationId;
        }

        private double GetRequestedDurationSeconds(int sampleRate)
        {
            double perOctaveSeconds = (double)numericUpDownRequestedDuration.Value * 0.001;
            double requestedDuration = ExponentialSineSweep.TotalDurationForOctavePace(
                (double)numericUpDownLowFrequency.Value,
                (double)numericUpDownHighFrequency.Value,
                perOctaveSeconds,
                sampleRate);
            return requestedDuration > 0 ? requestedDuration : perOctaveSeconds;
        }

        private PlaybackChannel GetSelectedPlaybackChannel() =>
            comboBoxChannel.SelectedIndex >= 0
                ? (PlaybackChannel)comboBoxChannel.SelectedIndex
                : PlaybackChannel.Mono;

        private void RaiseSweepSettingsChanged()
        {
            if (initializing)
            {
                return;
            }

            SweepSettingsChanged?.Invoke();
        }

        private void buttonCalibration0_Click(object? sender, EventArgs e)
        {
            microphoneCalibration0DegreesPath =
                SelectCalibrationFile(microphoneCalibration0DegreesPath);
            UpdateCalibrationButtons();
            RaiseCalibrationChanged();
        }

        /// <summary>Adopts a list the shell changed behind this panel, so the next Apply does not write back the stale one.</summary>
        internal void AdoptAdditionalCalibrations(
            IReadOnlyList<MicrophoneCalibrationDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            additionalMicrophoneCalibrations = definitions
                .Select(definition => definition.Clone())
                .ToList();
            UpdateCalibrationButtons();
        }

        private void buttonCalibrationExtra_Click(object? sender, EventArgs e)
        {
            using var dialog = new MicrophoneCalibrationsDialog(
                additionalMicrophoneCalibrations,
                NormalizeCalibrationPath(microphoneCalibration0DegreesPath),
                SelectCalibrationFile);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            additionalMicrophoneCalibrations = dialog.Definitions.ToList();
            UpdateCalibrationButtons();
            RaiseCalibrationChanged();
        }

        private AudioBackend SelectedAudioBackend =>
            comboBoxAudioBackend.SelectedIndex >= 0
                ? (AudioBackend)comboBoxAudioBackend.SelectedIndex
                : AudioBackend.Wave;

        /// <summary>From the working copy, so a just-added calibration is assignable before Apply.</summary>
        private IReadOnlyList<MicrophoneCalibrationEntry> BuildCalibrationEntries()
        {
            string? zeroDegreePath = NormalizeCalibrationPath(microphoneCalibration0DegreesPath);
            var entries = new List<MicrophoneCalibrationEntry>(
                additionalMicrophoneCalibrations.Count + 1)
            {
                new(
                    MicrophoneCalibrationIds.ZeroDegrees,
                    "0°",
                    !string.IsNullOrWhiteSpace(zeroDegreePath))
            };
            foreach (MicrophoneCalibrationDefinition definition in additionalMicrophoneCalibrations)
            {
                entries.Add(new MicrophoneCalibrationEntry(definition.Id, definition.Name, true));
            }

            return entries;
        }

        // A channel number names a different input on each backend (and each device).
        private List<ArrayMicrophoneDefinition> SelectedArrayMicrophones =>
            SelectedAudioBackend == AudioBackend.Asio
                ? asioArrayMicrophones
                : waveArrayMicrophones;

        private string? SelectedArrayDeviceId =>
            SelectedAudioBackend == AudioBackend.Asio
                ? asioArrayDeviceId
                : waveArrayDeviceId;

        private string? CurrentCaptureDeviceId =>
            SelectedAudioBackend == AudioBackend.Asio
                ? (comboBoxAsioDriver.SelectedItem as AsioDeviceInfo)?.DriverName
                : (comboBoxRecordingDevice.SelectedItem as AudioEndpointDescriptor)?.Id
                    ?? preferredWasapiCaptureEndpointId;

        // Same verdict as the settings, so the panel is not a second opinion.
        private bool SelectedArrayMatchesDevice =>
            MeasurementSettingsFile.SweepMeasurementSettings.ArrayMatchesDevice(
                SelectedArrayDeviceId,
                CurrentCaptureDeviceId);

        // An ASIO stamp is the driver name; a WASAPI stamp is an unreadable endpoint id, so look up its name.
        private string DescribeArrayDevice()
        {
            string? id = SelectedArrayDeviceId;
            if (string.IsNullOrWhiteSpace(id))
            {
                return "another device";
            }
            if (SelectedAudioBackend == AudioBackend.Asio)
            {
                return id;
            }

            foreach (object? item in comboBoxRecordingDevice.Items)
            {
                if (item is AudioEndpointDescriptor endpoint &&
                    string.Equals(endpoint.Id, id, StringComparison.Ordinal))
                {
                    return endpoint.DisplayName;
                }
            }

            return "another device";
        }

        /// <summary>Recordable inputs and where the list came from, since "no room for an array" has several causes.</summary>
        private (IReadOnlyList<int> Channels, string Source) GetArrayInputChannels()
        {
            AudioBackend backend = SelectedAudioBackend;
            if (backend == AudioBackend.Asio)
            {
                int[] asio = asioDriverInfo.InputChannels
                    .Select(channel => channel.Offset)
                    .ToArray();
                return (asio, ArrayInputSources.Describe(backend, asio.Length));
            }
            if (backend.IsWasapi())
            {
                int count = comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor endpoint
                    ? endpoint.ChannelCount
                    : 0;
                return (
                    Enumerable.Range(0, count).ToArray(),
                    ArrayInputSources.Describe(backend, count));
            }

            return ([0, 1], ArrayInputSources.Describe(backend, 2));
        }

        private void comboBoxMicrophoneCalibration_SelectedIndexChanged(
            object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            microphoneCalibrationId =
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(
                    comboBoxMicrophoneCalibration);
            RaiseSweepSettingsChanged();
        }

        private void RefreshMicrophoneCalibrationCombo()
        {
            bool wasInitializing = initializing;
            initializing = true;
            MicrophoneCalibrationComboHelper.Configure(
                comboBoxMicrophoneCalibration,
                microphoneCalibrationId,
                BuildCalibrationEntries());
            microphoneCalibrationId =
                MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(
                    comboBoxMicrophoneCalibration);
            initializing = wasInitializing;
        }

        private void buttonArrayMicrophones_Click(object? sender, EventArgs e)
        {
            (IReadOnlyList<int> channels, string source) = GetArrayInputChannels();
            bool asio = SelectedAudioBackend == AudioBackend.Asio;
            using var dialog = new ArrayMicrophonesDialog(
                SelectedArrayMicrophones,
                BuildCalibrationEntries(),
                channels,
                asio ? GetSelectedAsioInputChannelOffset() : GetSelectedWaveInputChannelOffset(),
                asio
                    ? GetSelectedAsioLoopbackInputChannelOffset()
                    : GetSelectedWaveLoopbackChannelOffset(),
                source);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            List<ArrayMicrophoneDefinition> edited = dialog.Microphones
                .Select(microphone => microphone.Clone())
                .ToList();
            if (asio)
            {
                asioArrayMicrophones = edited;
                asioArrayDeviceId = CurrentCaptureDeviceId;
            }
            else
            {
                waveArrayMicrophones = edited;
                waveArrayDeviceId = CurrentCaptureDeviceId;
            }

            UpdateArrayMicrophoneButton();
            // Apply on the fly like every other control; otherwise closing the panel dropped the edit.
            RaiseSweepSettingsChanged();
        }

        private void UpdateArrayMicrophoneButton()
        {
            int count = SelectedArrayMicrophones.Count;
            if (count > 0 && !SelectedArrayMatchesDevice)
            {
                // Not a count: none would be recorded; the device name is the whole message.
                buttonArrayMicrophones.Text =
                    $"{count} on {DescribeArrayDevice()}...";
                return;
            }

            int usable = UsableArrayMicrophoneCount();
            string suffix = usable == count ? string.Empty : $" ({count - usable} unusable)";
            buttonArrayMicrophones.Text = count == 0
                ? "None..."
                : count == 1
                    ? $"1 microphone{suffix}..."
                    : $"{count} microphones{suffix}...";
        }

        /// <remarks>Must match <c>MeasurementSettingsFile.ResolveArrayChannels</c>, which drops unrecordable inputs.</remarks>
        private int UsableArrayMicrophoneCount() =>
            SelectedArrayMatchesDevice ? SelectedReachableArrayChannels().Count : 0;

        private void buttonClearCalibration0_Click(object? sender, EventArgs e)
        {
            microphoneCalibration0DegreesPath = null;
            UpdateCalibrationButtons();
            RaiseCalibrationChanged();
        }

        private void RaiseCalibrationChanged() =>
            CalibrationChanged?.Invoke(new CalibrationSelection(
                NormalizeCalibrationPath(microphoneCalibration0DegreesPath),
                additionalMicrophoneCalibrations
                    .Select(definition => definition.Clone())
                    .ToList(),
                splCalibration));

        private void buttonSplCalibration_Click(object? sender, EventArgs e)
        {
            if (audioSessionFactory == null)
            {
                return;
            }

            AudioSessionRequest request;
            try
            {
                request = BuildCalibrationCaptureRequest();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "SPL calibration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new SplCalibrationDialog(audioSessionFactory, request, splCalibration);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
            {
                splCalibration = dialog.Result;
                UpdateSplCalibrationButton();
                // Persist a completed physical calibration now, not on an Apply that may never come.
                RaiseCalibrationChanged();
            }
        }

        private void buttonClearSplCalibration_Click(object? sender, EventArgs e)
        {
            splCalibration = null;
            UpdateSplCalibrationButton();
            RaiseCalibrationChanged();
        }

        // Microphone only, no loopback: calibrated against an external calibrator.
        private AudioSessionRequest BuildCalibrationCaptureRequest()
        {
            var backend = (AudioBackend)comboBoxAudioBackend.SelectedIndex;
            PlaybackChannel playbackChannel = GetSelectedPlaybackChannel();
            return AudioSessionRequestBuilder.Build(
                backend,
                GetSelectedSampleRate(),
                (int)numericUpDownBits.Value,
                playbackChannel,
                waveInputChannelOffset: GetSelectedWaveInputChannelOffset(),
                waveLoopbackInputChannelOffset: null,
                asioInputChannelOffset: GetSelectedAsioInputChannelOffset(),
                asioLoopbackInputChannelOffset: null,
                asioOutputChannelOffset: GetSelectedAsioOutputChannelOffset(),
                outputDeviceNumber: GetSelectedPlaybackDeviceNumber(),
                inputDeviceNumber: GetSelectedRecordingDeviceNumber(),
                wasapiCaptureEndpointId:
                    comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor capture
                        ? capture.Id
                        : null,
                wasapiRenderEndpointId:
                    comboBoxPlaybackDevice.SelectedItem is AudioEndpointDescriptor render
                        ? render.Id
                        : null,
                asioDriverName:
                    comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asio
                        ? asio.DriverName
                        : null,
                bufferMilliseconds: 100,
                expectedCaptureSamples: 0);
        }

        private void UpdateSplCalibrationButton()
        {
            buttonSplCalibration.Enabled = audioSessionFactory != null;
            buttonClearSplCalibration.Enabled = splCalibration != null;

            if (splCalibration == null)
            {
                buttonSplCalibration.Text = "Calibrate...";
                buttonSplCalibration.ForeColor = Color.White;
                deviceToolTip.SetToolTip(
                    buttonSplCalibration,
                    audioSessionFactory != null
                        ? "Measure the offset from a 1 kHz acoustic calibrator so measurements " +
                            "can be shown in dB SPL. Uses the currently selected input."
                        : "SPL calibration is unavailable.");
                deviceToolTip.SetToolTip(buttonClearSplCalibration, "No SPL calibration.");
                return;
            }

            buttonSplCalibration.Text =
                $"{splCalibration.ReferenceLevelDbSpl:0} dB · {splCalibration.OffsetDb:+0.0;-0.0;0.0} dB";
            bool stale = !CurrentInputMatches(splCalibration);
            buttonSplCalibration.ForeColor = stale ? Color.Gold : Color.White;
            string detail =
                $"Measured {splCalibration.MeasuredLevelDbFs:0.0} dBFS at " +
                $"{splCalibration.MeasuredFrequencyHz:0} Hz " +
                $"({splCalibration.ReferenceLevelDbSpl:0} dB SPL reference).\r\n" +
                $"Offset {splCalibration.OffsetDb:+0.0;-0.0;0.0} dB · " +
                $"{splCalibration.CapturedAtUtc.ToLocalTime():g}.";
            if (stale)
            {
                detail += "\r\n⚠ The current input differs from the calibrated one — recalibrate.";
            }
            deviceToolTip.SetToolTip(buttonSplCalibration, detail);
            deviceToolTip.SetToolTip(buttonClearSplCalibration, "Clear the SPL calibration.");
        }

        private bool CurrentInputMatches(SplCalibration calibration)
        {
            var backend = (AudioBackend)comboBoxAudioBackend.SelectedIndex;
            int microphoneChannelOffset = backend == AudioBackend.Asio
                ? GetSelectedAsioInputChannelOffset()
                : GetSelectedWaveInputChannelOffset();
            return calibration.MatchesInput(
                backend,
                GetSelectedSampleRate(),
                (int)numericUpDownBits.Value,
                microphoneChannelOffset,
                backend == AudioBackend.Wave ? GetSelectedRecordingDeviceNumber() : null,
                comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor capture
                    ? capture.Id
                    : null,
                comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asio ? asio.DriverName : null);
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

            // Probe now: an unparsable file would otherwise silently leave measurements uncalibrated.
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
            int count = additionalMicrophoneCalibrations.Count;
            buttonCalibrationExtra.Text = count == 0
                ? "Manage..."
                : $"Manage... ({count})";
            deviceToolTip.SetToolTip(
                buttonCalibrationExtra,
                "Further calibration files, and curves estimated from one of them for " +
                "an angle of incidence. The measurement microphone and every array " +
                "microphone are read through one of them.");
            deviceToolTip.SetToolTip(
                comboBoxMicrophoneCalibration,
                "The calibration the measurement microphone is read through." +
                Environment.NewLine + Environment.NewLine +
                "A run FREEZES it into the file it writes, so it describes the capsule " +
                "about to record — set it before measuring, not after. The analysis " +
                "views then read a measurement through the curve it was recorded " +
                "with, and Virtual DSP offers it as \"Own (as measured)\".");
            RefreshMicrophoneCalibrationCombo();
        }

        private void UpdateCalibrationButton(
            Button selectButton,
            Button clearButton,
            string? path)
        {
            string? normalized = NormalizeCalibrationPath(path);
            // Deleted or unparsable files both silently disable correction at plot time otherwise.
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
            if (initializing)
            {
                return;
            }

            RefreshSweepBandPreview();
            RaiseSweepSettingsChanged();
        }

        private void averagingSetting_Changed(object? sender, EventArgs e) =>
            RaiseSweepSettingsChanged();

        private void numericUpDownSweepBand_ValueChanged(object? sender, EventArgs e)
        {
            if (initializing || updatingSweepBand)
            {
                return;
            }

            updatingSweepBand = true;
            try
            {
                if (numericUpDownLowFrequency.Value >= numericUpDownHighFrequency.Value)
                {
                    if (sender == numericUpDownHighFrequency)
                    {
                        numericUpDownLowFrequency.Value = Math.Max(
                            numericUpDownLowFrequency.Minimum,
                            numericUpDownHighFrequency.Value - 1);
                    }
                    else
                    {
                        numericUpDownHighFrequency.Value = Math.Min(
                            numericUpDownHighFrequency.Maximum,
                            numericUpDownLowFrequency.Value + 1);
                        if (numericUpDownLowFrequency.Value >= numericUpDownHighFrequency.Value)
                        {
                            numericUpDownLowFrequency.Value =
                                numericUpDownHighFrequency.Value - 1;
                        }
                    }
                }
            }
            finally
            {
                updatingSweepBand = false;
            }

            RefreshSweepBandPreview();
            RaiseSweepSettingsChanged();
        }

        // From the panel's values, not the last generated sweep (stale until the next run).
        private void RefreshSweepBandPreview()
        {
            if (comboBoxSampleRate.SelectedItem is not int)
            {
                // No rate opens: GetSelectedSampleRate's 44.1 kHz fallback would describe an unrunnable sweep.
                labelActualRangeCaption.Text = "—";
                labelActualRangeCaption.ForeColor = Color.Gold;
                deviceToolTip.SetToolTip(
                    labelActualRangeCaption,
                    "No sample rate opens for the current configuration, so there is " +
                    "nothing to compute the sweep against.");
                return;
            }

            double lowHz = (double)numericUpDownLowFrequency.Value;
            double highHz = (double)numericUpDownHighFrequency.Value;
            double perOctaveSeconds = (double)numericUpDownRequestedDuration.Value * 0.001;
            int sampleRate = GetSelectedSampleRate();
            double totalSeconds = ExponentialSineSweep.TotalDurationForOctavePace(
                lowHz, highHz, perOctaveSeconds, sampleRate);
            ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(
                lowHz, highHz, totalSeconds, sampleRate);
            labelActualRangeCaption.Text = spec.IsValid
                ? $"{spec.LowFrequencyHz:0.#}–{spec.HighFrequencyHz:0} Hz · " +
                    $"{spec.OctaveSpan:0.00} oct · {spec.ComputedDurationSeconds:0.00} s"
                : "—";
            string? warning = DescribeSweepShortfall(spec, lowHz, highHz, totalSeconds);
            labelActualRangeCaption.ForeColor = warning == null
                ? Color.FromArgb(150, 200, 170)
                : Color.Gold;
            deviceToolTip.SetToolTip(
                labelActualRangeCaption,
                warning ??
                    "The band the sweep covers at full amplitude, with the fades " +
                    "outside it, and how long it takes.");
        }

        private static string? DescribeSweepShortfall(
            ExpSweepSpec spec,
            double requestedLowHz,
            double requestedHighHz,
            double requestedTotalSeconds)
        {
            if (!spec.IsValid)
            {
                return null;
            }

            if (requestedTotalSeconds > ExponentialSineSweep.MaxDurationSeconds &&
                spec.OctaveSpan > 0)
            {
                double effectivePace = spec.ComputedDurationSeconds / spec.OctaveSpan;
                return $"⚠ Capped at {ExponentialSineSweep.MaxDurationSeconds:0} s " +
                    $"(asked for {requestedTotalSeconds:0} s), so the sweep really " +
                    $"paces {effectivePace * 1000.0:0} ms per octave.";
            }

            if (spec.Covers(requestedLowHz, requestedHighHz))
            {
                return null;
            }

            // Full amplitude needs a whole cycle plus fade room, so a short sweep falls short at the bottom first.
            return $"⚠ Full amplitude only from {spec.FullAmplitudeLowFrequencyHz:0.#} " +
                $"to {spec.FullAmplitudeHighFrequencyHz:0} Hz: one cycle at " +
                $"{requestedLowHz:0.#} Hz already takes " +
                $"{1000.0 / Math.Max(requestedLowHz, 1e-9):0} ms. Raise the " +
                "per-octave time to reach the requested band.";
        }

        // Writes the sweep the next measurement would play, for playback from another device while this records.
        // Uses the selected (not applied) rate, matching the achieved-range line.
        private void buttonSaveSweepFile_Click(object? sender, EventArgs e)
        {
            double lowFrequencyHz = (double)numericUpDownLowFrequency.Value;
            double highFrequencyHz = (double)numericUpDownHighFrequency.Value;
            int sampleRate = GetSelectedSampleRate();
            double totalSeconds = GetRequestedDurationSeconds(sampleRate);

            using var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "wav",
                FileName = SweepWavExport.SuggestFileName(
                    lowFrequencyHz,
                    highFrequencyHz,
                    totalSeconds,
                    sampleRate),
                Filter = "WAV audio (*.wav)|*.wav",
                OverwritePrompt = true,
                Title = "Save the sweep signal"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            UseWaitCursor = true;
            try
            {
                // Own instance: generating into the live one would discard the result on screen.
                using var sweep = new ExponentialSineSweep();
                sweep.FillData(
                    lowFrequencyHz,
                    highFrequencyHz,
                    totalSeconds,
                    (int)numericUpDownBits.Value,
                    sampleRate);
                AudioFileCodec.WriteWav(
                    dialog.FileName,
                    SweepWavExport.BuildContent(
                        sweep.SweepData,
                        sampleRate,
                        GetSelectedPlaybackChannel()));
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Save sweep",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void comboBoxAudioBackend_SelectedIndexChanged(object sender, EventArgs e) =>
            HandleAudioConfigurationChanged();

        private void comboBoxWaveInputChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            // Channel count changes which sample rates the device supports.
            RefreshSampleRateOptions(GetSelectedSampleRate());
        }

        private void comboBoxPlaybackDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateComboBoxToolTip(comboBoxPlaybackDevice);
            if (comboBoxPlaybackDevice.SelectedItem is AudioEndpointDescriptor endpoint)
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
            if (comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor endpoint)
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
            // With ASIO the rate list comes from the driver probe.
            RefreshAsioDriverInfo(
                GetSelectedSampleRate(),
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            RefreshSampleRateOptions(GetSelectedSampleRate());
            UpdateAudioBackendControls();
        }

        private void comboBoxChannel_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            RefreshSampleRateOptions(GetSelectedSampleRate());
            RaiseSweepSettingsChanged();
        }

        private void comboBoxSampleRate_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            // A user pick ends any earlier automatic-fallback marker; the probe verdict is re-taken below.
            sampleRateFellBackFrom = null;

            // Preview only; the rate itself waits for Apply.
            RefreshSweepBandPreview();
            if (IsSelectedWasapiBackend())
            {
                // Picking a rate already in the list does not rebuild it, so the WASAPI status line must be rewritten here.
                UpdateWaveLoopbackControls();
            }
            if (comboBoxAudioBackend.SelectedIndex != (int)AudioBackend.Asio)
            {
                return;
            }

            // Buffer size and latency are rate-dependent.
            RefreshAsioDriverInfo(
                GetSelectedSampleRate(),
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            UpdateAudioBackendControls();
        }

        private void UpdateAudioBackendControls()
        {
            UpdateArrayMicrophoneButton();
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
            UiStyle.SetTextEnabledLook(labelAsioDriver, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioInputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioOutputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioSampleRate, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioSampleRateStatus, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatency, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatencyValue, useAsio);
            UiStyle.SetTextEnabledLook(labelPlaybackDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelRecordingDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveInputChannel, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveLoopbackChannel, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveLoopbackStatus, !useAsio);
            labelDeviceSettings.Visible = useWasapi;
            buttonDeviceSettings.Visible = useWasapi;
            buttonDeviceSettings.Enabled = useWasapi;
            UiStyle.SetTextEnabledLook(labelAsioLoopbackChannel, useAsio);
            // The calibration is pinned to one input; flag it stale when backend/device changes.
            UpdateSplCalibrationButton();
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

        private static AudioEndpointDescriptor SelectWasapiEndpoint(
            IReadOnlyList<AudioEndpointDescriptor> endpoints,
            string? preferredId,
            string direction)
        {
            AudioEndpointDescriptor? endpoint = endpoints.FirstOrDefault(candidate =>
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
                // The control panel may have changed buffer size or reported rates.
                RefreshAsioDriverInfo(
                    preferredSampleRate,
                    preferredInputOffset,
                    preferredOutputOffset,
                    preferredLoopbackOffset);
                RefreshSampleRateOptions(preferredSampleRate);
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

        // Opens the driver once and caches it, including the rates GetSupportedSampleRates serves.
        private void RefreshAsioDriverInfo(
            int sampleRate,
            int preferredInputOffset,
            int preferredOutputOffset,
            int? preferredLoopbackOffset)
        {
            string? driverName = comboBoxAsioDriver.SelectedItem is AsioDeviceInfo asioDriver
                ? asioDriver.DriverName
                : null;
            asioDriverInfo = AsioDeviceCatalog.GetDriverInfo(driverName, sampleRate);
            // Settled here: not every caller rebuilds the rate list, and a stale flag reported a succeeded probe as failed.
            sampleRateProbeFailed = IsAsioSampleRateProbeFailure();

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
            // A named but unopenable driver must not collapse the saved routing to channel 1 / None on the next apply.
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
            if (sampleRateProbeFailed)
            {
                // The last probe said nothing, so do not claim support for an untested rate.
                labelAsioSampleRateStatus.Text =
                    $"{sampleRate} Hz kept — the driver did not report its rates";
                labelAsioSampleRateStatus.ForeColor = Color.Khaki;
            }
            else if (sampleRateFellBackFrom is int previous)
            {
                labelAsioSampleRateStatus.Text =
                    $"{previous} Hz is not offered by this driver — changed to {sampleRate} Hz";
                labelAsioSampleRateStatus.ForeColor = Color.LightSalmon;
            }
            else
            {
                labelAsioSampleRateStatus.Text = asioDriverInfo.SupportsSampleRate
                    ? $"{sampleRate} Hz supported"
                    : $"{sampleRate} Hz not supported";
                labelAsioSampleRateStatus.ForeColor = asioDriverInfo.SupportsSampleRate
                    ? Color.LightGreen
                    : Color.LightSalmon;
            }
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
                wasapiCaptureEndpoints = Array.Empty<AudioEndpointDescriptor>();
                wasapiRenderEndpoints = Array.Empty<AudioEndpointDescriptor>();
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
                        AudioEndpointDirection.Render);
                    PopulateWasapiEndpointCombo(
                        comboBoxRecordingDevice,
                        wasapiCaptureEndpoints,
                        preferredWasapiCaptureEndpointId,
                        preferredWasapiCaptureEndpointName,
                        AudioEndpointDirection.Capture);
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
            IReadOnlyList<AudioEndpointDescriptor> endpoints,
            string? preferredId,
            string? preferredName,
            AudioEndpointDirection direction)
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
            IReadOnlyList<AudioEndpointDescriptor> endpoints,
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

        internal static AudioEndpointDescriptor CreateUnavailableEndpoint(
            string endpointId,
            string? friendlyName,
            AudioEndpointDirection direction) =>
            new(
                endpointId,
                string.IsNullOrWhiteSpace(friendlyName) ? endpointId : friendlyName,
                direction,
                new AudioFormat(44_100, 16, 1, AudioSampleEncoding.Pcm),
                0,
                IsAvailable: false,
                IsDefault: false);

        private void FillWasapiChannelControls(
            int preferredInputOffset,
            int? preferredLoopbackOffset)
        {
            int channelCount = comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor endpoint
                ? endpoint.ChannelCount
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
                AudioEndpointDescriptor? capture = comboBoxRecordingDevice.SelectedItem as AudioEndpointDescriptor;
                AudioEndpointDescriptor? render = comboBoxPlaybackDevice.SelectedItem as AudioEndpointDescriptor;
                if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
                {
                    labelWaveLoopbackStatus.Text =
                        "⚠ A saved endpoint is unavailable. Reconnect it or select a replacement.";
                    labelWaveLoopbackStatus.ForeColor = Color.Gold;
                    return;
                }
                if (GetSelectedWaveLoopbackChannelOffset() == null)
                {
                    labelWaveLoopbackStatus.Font = WarningStatusFont;
                    labelWaveLoopbackStatus.Text =
                        "⚠ Loopback channel is REQUIRED. Select the physical input carrying " +
                        "the playback reference.";
                    labelWaveLoopbackStatus.ForeColor = Color.Gold;
                    return;
                }
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.WasapiExclusive)
                {
                    int selectedRate = GetSelectedSampleRate();
                    int bits = (int)numericUpDownBits.Value;
                    int captureChannels = GetSelectedWaveRecordingChannelCount();
                    int renderChannels = GetSelectedPlaybackChannelCount();
                    if (comboBoxSampleRate.Items.Count == 0)
                    {
                        // No rate opens at all, so do not name the fallback rate. Exclusive passes the format unchanged;
                        // mono (a one-channel format) is the usual reason native-stereo endpoints refuse.
                        labelWaveLoopbackStatus.Text =
                            $"⚠ No sample rate opens in Exclusive: {bits}-bit, " +
                            $"{captureChannels}-ch capture, {renderChannels}-ch render. " +
                            (renderChannels < 2
                                ? "Mono asks for a one-channel format most endpoints " +
                                    "refuse — try Stereo."
                                : "Try another endpoint pair, or Shared.");
                        labelWaveLoopbackStatus.ForeColor = Color.LightSalmon;
                        return;
                    }
                    bool supported = IsExclusiveFormatSupported(
                        capture.Id,
                        render.Id,
                        selectedRate,
                        bits,
                        captureChannels,
                        renderChannels);
                    labelWaveLoopbackStatus.Text = supported
                        ? $"Exclusive: {selectedRate:N0} Hz / {bits}-bit opens directly " +
                            "on both endpoints."
                        : $"⚠ Exclusive format {selectedRate:N0} Hz / {bits}-bit is not supported by both endpoints.";
                    labelWaveLoopbackStatus.ForeColor = supported
                        ? Color.LightGray
                        : Color.LightSalmon;
                    return;
                }
                string compatibility = capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
                    ? ""
                    : " — sample rates do not match";
                labelWaveLoopbackStatus.Text =
                    $"Shared mix format: {capture.PreferredFormat.SampleRate:N0} Hz / " +
                    $"{capture.PreferredFormat.BitsPerSample}-bit capture, " +
                    $"{render.PreferredFormat.BitsPerSample}-bit render{compatibility}. " +
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
            // No loopback = no transfer IR, no measurement; make it impossible to overlook.
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
                AudioEndpointDescriptor { ChannelCount: >= 2, IsAvailable: true };

        private void ValidateSelectedWaveLoopback()
        {
            bool loopbackSelected =
                comboBoxWaveLoopbackChannel.SelectedItem is InputChannelOption { Offset: not null };
            ValidateRequiredWaveLoopback(
                loopbackSelected,
                SelectedRecordingDeviceSupportsWaveLoopback());
        }

        internal static void ValidateRequiredWaveLoopback(
            bool loopbackSelected,
            bool recordingDeviceSupportsLoopback)
        {
            if (!loopbackSelected)
            {
                throw new InvalidOperationException(
                    "A loopback reference channel is required before measuring.");
            }
            if (!recordingDeviceSupportsLoopback)
            {
                throw new InvalidOperationException(
                    "Wave loopback requires a selected stereo recording device.");
            }
        }

        // An empty Wave rate list means none in common; skipping validation let an unopenable rate through.
        private void ValidateSelectedWaveSampleRate(int sampleRate) =>
            SampleRateOptions.ValidateSelectedRate(
                GetSupportedSampleRates(),
                sampleRate,
                "Wave devices");

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

        // A missing persisted device stays as "(missing)" so Apply cannot silently re-target another device.
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

        // An offset the driver does not report now (fewer channels, busy driver) must survive the round-trip.
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
                // The panel can close during the ~1 s capture; touching a disposed form would crash the async void handler.
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
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                // Opening ASIO is a slow synchronous COM call; skip it for Wave changes.
                RefreshAsioDriverInfo(
                    preferredSampleRate,
                    GetSelectedAsioInputChannelOffset(),
                    GetSelectedAsioOutputChannelOffset(),
                    GetSelectedAsioLoopbackInputChannelOffset());
            }
            RefreshSampleRateOptions(preferredSampleRate);
            UpdateAudioBackendControls();
        }

        /// <summary>Re-reads the device after Apply reconfigured it under the open panel (otherwise the status stays stale).</summary>
        internal void RefreshAudioDeviceView()
        {
            if (initializing || IsDisposed)
            {
                return;
            }

            int preferredSampleRate = GetSelectedSampleRate();
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                RefreshAsioDriverInfo(
                    preferredSampleRate,
                    GetSelectedAsioInputChannelOffset(),
                    GetSelectedAsioOutputChannelOffset(),
                    GetSelectedAsioLoopbackInputChannelOffset());
            }

            RefreshSampleRateOptions(preferredSampleRate);
            UpdateAudioBackendControls();
        }

        private void RefreshSampleRateOptions(int preferredSampleRate)
        {
            SampleRateResolution resolution = SampleRateOptions.Resolve(
                GetSupportedSampleRates(),
                preferredSampleRate,
                comboBoxSampleRate.Items.Count > 0,
                IsAsioSampleRateProbeFailure());
            sampleRateProbeFailed = resolution.ProbeFailed;
            sampleRateFellBackFrom = resolution.FellBackFrom;
            if (resolution.Rates is null)
            {
                // No answer: keep the list and selection (rebuilding replaced a working 96 kHz with 44.1).
                // Rewrite the status line, which was rendered from the previous flags.
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
                {
                    UpdateAsioStatusLabels();
                }
                return;
            }

            int[] availableRates = resolution.Rates;
            int selectedSampleRate = resolution.Selected;
            // Empty list is a real outcome: no rate works, Apply refuses. Do not fill in the configured rate.

            bool wasInitializing = initializing;
            initializing = true;
            try
            {
                comboBoxSampleRate.Items.Clear();
                comboBoxSampleRate.Items.AddRange(
                    availableRates
                        .Select(rate => (object)rate)
                        .ToArray());
                // -1 on an empty list; index 0 would throw mid-rebuild with the guard raised, deafening all combos.
                comboBoxSampleRate.SelectedIndex = SampleRateOptions.FindRateIndex(
                    availableRates,
                    selectedSampleRate);
            }
            finally
            {
                initializing = wasInitializing;
            }
            // Rate may move under the initializing guard, so refresh the preview here (Init previews once at the end).
            if (!initializing)
            {
                RefreshSweepBandPreview();
            }

            // Written after the combo is filled: RefreshAsioDriverInfo wrote it while the selection was still the fallback.
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                UpdateAsioStatusLabels();
            }
            else if (IsSelectedWasapiBackend())
            {
                // Channel changes rebuild the rate list without UpdateAudioBackendControls, so rewrite the endpoint line.
                UpdateWaveLoopbackControls();
            }
        }

        private bool IsAsioSampleRateProbeFailure() =>
            SampleRateOptions.IsProbeFailure(
                comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio,
                asioDriverInfo.DriverName,
                asioDriverInfo.SupportedSampleRates.Count);

        private IReadOnlyList<int> GetSupportedSampleRates()
        {
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                // From the last probe: some drivers refuse a second open moments later (empty rate list).
                return asioDriverInfo.SupportedSampleRates;
            }

            if (IsSelectedWasapiBackend())
            {
                AudioEndpointDescriptor? capture = comboBoxRecordingDevice.SelectedItem as AudioEndpointDescriptor;
                AudioEndpointDescriptor? render = comboBoxPlaybackDevice.SelectedItem as AudioEndpointDescriptor;
                if (capture is not { IsAvailable: true } || render is not { IsAvailable: true })
                {
                    return [];
                }
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.WasapiShared)
                {
                    return capture.PreferredFormat.SampleRate == render.PreferredFormat.SampleRate
                        ? [capture.PreferredFormat.SampleRate]
                        : [];
                }

                int captureChannels = GetSelectedWaveRecordingChannelCount();
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

        private int GetSelectedPlaybackChannelCount() =>
            GetSelectedPlaybackChannel() == PlaybackChannel.Mono ? 1 : 2;

        /// <summary>Array channels actually recordable on the selected device.</summary>
        /// <remarks>Must match <c>MeasurementSettingsFile.ResolveArrayChannels</c>, or a probed rate fails at the device.</remarks>
        private IReadOnlyList<int> SelectedReachableArrayChannels()
        {
            bool asio = SelectedAudioBackend == AudioBackend.Asio;
            int microphoneChannel = asio
                ? GetSelectedAsioInputChannelOffset()
                : GetSelectedWaveInputChannelOffset();
            int? loopbackChannel = asio
                ? GetSelectedAsioLoopbackInputChannelOffset()
                : GetSelectedWaveLoopbackChannelOffset();
            var reachable = GetArrayInputChannels().Channels.ToHashSet();
            var channels = new List<int>();
            foreach (ArrayMicrophoneDefinition microphone in SelectedArrayMicrophones)
            {
                if (microphone.ChannelOffset >= 0 &&
                    microphone.ChannelOffset != microphoneChannel &&
                    microphone.ChannelOffset != loopbackChannel &&
                    (reachable.Count == 0 || reachable.Contains(microphone.ChannelOffset)) &&
                    !channels.Contains(microphone.ChannelOffset))
                {
                    channels.Add(microphone.ChannelOffset);
                }
            }

            return channels;
        }

        /// <remarks>Uses <see cref="AudioCaptureRouting.RequiredInputChannelCount"/> so the probed width matches the opened width.</remarks>
        private int GetSelectedWaveRecordingChannelCount()
        {
            int microphone = GetSelectedWaveInputChannelOffset();
            int? loopback = GetSelectedWaveLoopbackChannelOffset();
            var routing = new AudioCaptureRouting(microphone, loopback)
            {
                ArrayChannels = SelectedReachableArrayChannels()
            };
            // Mic on offset 1 needs a 2-channel format even without loopback.
            int loopbackChannels = loopback.HasValue ? 2 : 1;
            return Math.Max(routing.RequiredInputChannelCount, loopbackChannels);
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
    }
}
