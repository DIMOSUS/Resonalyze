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
        // Raised the moment any calibration (the microphone's 0° file, the list of
        // additional ones, or the SPL anchor) is selected, edited, captured, or
        // cleared, so the host can apply and persist it immediately instead of
        // only when the panel is applied.
        internal event Action<CalibrationSelection>? CalibrationChanged;
        // Raised whenever a control outside the audio-backend group changes. That
        // whole group — the backend, the format it opens the device with (sample
        // rate and bit depth), its device panel and the Apply button — is the only
        // part of this panel that waits to be applied; everything else takes
        // effect as it is edited.
        public event Action? SweepSettingsChanged;

        /// <summary>A snapshot of the calibrations the panel manages.</summary>
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
        // What the last rate probe amounted to, so the status line can say which of the
        // three situations it is in rather than pronouncing on a rate it did not test.
        private bool sampleRateProbeFailed;
        private int? sampleRateFellBackFrom;
        private bool initializing;
        private string? microphoneCalibration0DegreesPath;
        private List<MicrophoneCalibrationDefinition> additionalMicrophoneCalibrations = [];
        private List<ArrayMicrophoneDefinition> waveArrayMicrophones = [];
        private List<ArrayMicrophoneDefinition> asioArrayMicrophones = [];
        // The device each array was configured on. Null until the array is next
        // edited; see MeasurementSettingsFile.ArrayMatchesDevice.
        private string? waveArrayDeviceId;
        private string? asioArrayDeviceId;
        // The SPL calibration anchor and the factory used to capture it. The
        // factory is only present when the form is created for real use (the
        // parameterless designer constructor leaves it null, which disables the
        // Calibrate button).
        private readonly IAudioSessionFactory? audioSessionFactory;
        private SplCalibration? splCalibration;
        // Remembers the loopback channel choice while a mono or missing
        // recording device forces the combo to "None", so selecting a stereo
        // device again restores it instead of losing it on the next apply.
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
            // The array button reports what would actually be RECORDED, so moving the
            // measurement microphone or the loopback onto one of the array's inputs
            // has to show there: that is the moment a configured position stops being
            // recordable, and it happens in a different part of this panel.
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

            // Clamped and rounded: the settings file is not normalized against
            // the control ranges, and (int) truncation shaves a millisecond off
            // durations that are not exactly representable in binary.
            (double lowFrequencyHz, double highFrequencyHz) = settings.ResolveBand(settings.SampleRate);
            numericUpDownLowFrequency.Value = numericUpDownLowFrequency.ClampValue(
                Math.Round(lowFrequencyHz));
            numericUpDownHighFrequency.Value = numericUpDownHighFrequency.ClampValue(
                Math.Max((double)numericUpDownLowFrequency.Value + 1.0, Math.Round(highFrequencyHz)));
            // The duration is entered per octave; show the stored total that way.
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
            // Total duration and the achieved-range line are filled by the shared
            // preview at the end of Init, once the sample-rate control is settled.
            numericUpDownAverageRunCount.Value = Math.Clamp(settings.AverageRunCount, 1, 64);
            microphoneCalibration0DegreesPath = settings.MicrophoneCalibration0DegreesPath;
            additionalMicrophoneCalibrations = settings.AdditionalMicrophoneCalibrations
                .Select(definition => definition.Clone())
                .ToList();
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
            // The button's own refresh happens in the UpdateAudioBackendControls
            // call at the end of Init, once the device/rate selections are settled.
            // The driver probe comes first: with ASIO it is what supplies the list
            // of sample rates, so the rate control cannot be filled before it.
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
            // Read the bit depth from the control, the single UI source of truth,
            // matching GetSupportedSampleRates. Equal to expSweepMeasurement.Bits
            // while the control is read-only, so this is a no-op today; it stops
            // silently ignoring the control the day it becomes editable.
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
                    // Shared never takes the rate from the combo, so no fallback of the
                    // combo's can reach the configuration through it.
                    sampleRate = captureEndpoint.PreferredFormat.SampleRate;
                }
                else if (audioBackend == AudioBackend.WasapiExclusive)
                {
                    // Exclusive does take it from the combo, and the combo can be empty:
                    // an endpoint pair with no rate in common is a real answer, and
                    // GetSelectedSampleRate then answers with its own 44.1 kHz fallback.
                    // Persisting that is persisting a format the endpoints just refused.
                    // Checked here, after the availability test above, so an endpoint that
                    // is simply gone keeps its own message instead of being reported as a
                    // rate mismatch.
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
                    // The array too, or applying this panel hands the measurement a
                    // configuration that differs from the one the settings file builds
                    // for the next run — the array simply absent from it. Harmless
                    // today only because a run re-applies from the settings; a
                    // configuration that is quietly not the one being measured with is
                    // the shape of a defect, not a state to leave standing.
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

            // Keep the live measurement's anchor in step with the applied settings,
            // so a freshly captured impulse response stamps the current calibration.
            expSweepMeasurement.SplCalibration = splCalibration;
        }

        /// <summary>
        /// Writes the immediately-applied half of the panel — the sweep band, its
        /// pacing, the playback channel and the averaging — into
        /// <paramref name="settings"/>. The audio backend group is deliberately
        /// left out: the backend, its format (sample rate and bit depth), its
        /// device panel and the Apply button all stay pending on the controls
        /// until <see cref="SetOptions"/> commits them together.
        /// </summary>
        /// <remarks>
        /// Nothing is pushed into the live <see cref="ExpSweepMeasurement"/> here.
        /// Its <c>Init</c> discards the measured result, and the settings reach it
        /// anyway right before the next sweep runs, so an edit made while looking
        /// at a measurement cannot throw that measurement away.
        /// </remarks>
        internal void ApplySweepSettings(
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            settings.LowFrequencyHz = (double)numericUpDownLowFrequency.Value;
            settings.HighFrequencyHz = (double)numericUpDownHighFrequency.Value;
            // Paced against the APPLIED sample rate, not the one selected in the
            // backend group: an uncommitted rate must not leak into the sweep. The
            // total is recomputed against the new rate when Apply commits it.
            settings.RequestedDurationSeconds = GetRequestedDurationSeconds(settings.SampleRate);
            settings.PlaybackChannel = GetSelectedPlaybackChannel();
            settings.AverageRunCount = (int)numericUpDownAverageRunCount.Value;
            ProtectiveHighPassConfiguration protectiveHighPass = ReadProtectiveHighPass();
            settings.ProtectiveHighPassKind = protectiveHighPass.Kind;
            settings.ProtectiveHighPassFrequencyHz = protectiveHighPass.FrequencyHz;
            settings.ProtectiveHighPassSlopeDbPerOctave =
                protectiveHighPass.SlopeDbPerOctave;
            // The array belongs here as much as the sweep does: it is part of the
            // capture routing, so an edit has to reach the settings on the same
            // apply that reopens the device with the new channels. Left out, the
            // panel showed the new count while the file kept the old list and the
            // next open read it back empty.
            settings.WaveArrayMicrophones = waveArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.AsioArrayMicrophones = asioArrayMicrophones
                .Select(definition => definition.Clone())
                .ToList();
            settings.WaveArrayDeviceId = waveArrayDeviceId;
            settings.AsioArrayDeviceId = asioArrayDeviceId;
        }

        // The duration field holds a per-octave pace; expand it to the total sweep
        // length the achieved band needs.
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

        /// <summary>
        /// Replaces the working copy of the additional-calibration list with one
        /// the shell changed behind this panel (a curve kept from a Virtual DSP
        /// session), so the next Apply writes that list back rather than the one
        /// this panel opened on.
        /// </summary>
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

        /// <summary>
        /// The calibrations the array can choose from: this panel's WORKING copy,
        /// not the applied one, so a calibration added here a moment ago can be
        /// assigned to an array microphone before anything is applied.
        /// </summary>
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

        // The array belongs to the backend it was configured on: a channel
        // number names a different input on each.
        private List<ArrayMicrophoneDefinition> SelectedArrayMicrophones =>
            SelectedAudioBackend == AudioBackend.Asio
                ? asioArrayMicrophones
                : waveArrayMicrophones;

        // ...and to the DEVICE, which the backend does not narrow down. Two
        // interfaces with eight inputs each agree about every channel NUMBER and
        // about nothing else, so an array carried across them keeps its
        // calibrations and its notes while pointing at inputs nobody chose.
        private string? SelectedArrayDeviceId =>
            SelectedAudioBackend == AudioBackend.Asio
                ? asioArrayDeviceId
                : waveArrayDeviceId;

        // What the array would be stamped with if it were configured right now.
        private string? CurrentCaptureDeviceId =>
            SelectedAudioBackend == AudioBackend.Asio
                ? (comboBoxAsioDriver.SelectedItem as AsioDeviceInfo)?.DriverName
                : (comboBoxRecordingDevice.SelectedItem as AudioEndpointDescriptor)?.Id
                    ?? preferredWasapiCaptureEndpointId;

        // The same verdict the settings reach, so the panel is not a second opinion.
        private bool SelectedArrayMatchesDevice =>
            MeasurementSettingsFile.SweepMeasurementSettings.ArrayMatchesDevice(
                SelectedArrayDeviceId,
                CurrentCaptureDeviceId);

        // The name to show for the device an array was configured on, when it is not
        // this one. An ASIO stamp IS the driver name; a WASAPI stamp is an endpoint
        // id, which is unreadable, so the list is asked for its name.
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

        /// <summary>
        /// Every input the selected backend can record, and where that list comes
        /// from — the second half matters because "there is no room for an array"
        /// has more than one cause, and the user can only act on the right one.
        /// </summary>
        private (IReadOnlyList<int> Channels, string Source) GetArrayInputChannels()
        {
            AudioBackend backend = SelectedAudioBackend;
            if (backend == AudioBackend.Asio)
            {
                return (
                    asioDriverInfo.InputChannels.Select(channel => channel.Offset).ToArray(),
                    "ASIO driver inputs");
            }
            if (backend.IsWasapi())
            {
                int count = comboBoxRecordingDevice.SelectedItem is AudioEndpointDescriptor endpoint
                    ? endpoint.ChannelCount
                    : 0;
                // An interface that presents its inputs to WASAPI as separate
                // stereo endpoints reports two here, and its further inputs are
                // genuinely unreachable in one session — through ASIO they are not.
                return (
                    Enumerable.Range(0, count).ToArray(),
                    count > 2
                        ? "WASAPI endpoint channels"
                        : "WASAPI endpoint channels; use ASIO to reach an interface's further inputs");
            }

            return ([0, 1], "MME is limited to two channels");
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
            // Confirming the dialog is the confirmation: whatever the list said
            // before, these inputs are now meant for the device selected now.
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
            // Every other control on this panel applies on the fly; a dialog is no
            // different. Without this the edit sat in the field until some unrelated
            // control happened to raise the event, and closing the panel first threw
            // it away.
            RaiseSweepSettingsChanged();
        }

        private void UpdateArrayMicrophoneButton()
        {
            int count = SelectedArrayMicrophones.Count;
            if (count > 0 && !SelectedArrayMatchesDevice)
            {
                // Not a count, because none of them would be recorded. Naming the
                // device it belongs to is the whole message: the inputs are still
                // there, so nothing else on this panel would look wrong.
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

        /// <summary>
        /// How many of the configured array microphones would actually be recorded.
        /// </summary>
        /// <remarks>
        /// The same rule <c>MeasurementSettingsFile.ResolveArrayChannels</c> applies,
        /// and it has to be the same or the button is a second opinion. It matters
        /// because that rule DROPS what it cannot record — a settings file has to stay
        /// startable — so a measurement microphone moved onto an array input after the
        /// array was configured takes a position out of the set. Reported here rather
        /// than left to be noticed as a curve that never appeared.
        /// </remarks>
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
                // A completed physical calibration is not a tentative edit: persist
                // it now instead of waiting for an Apply that the user may never
                // make (the panel only applies on the Apply button, not on close).
                RaiseCalibrationChanged();
            }
        }

        private void buttonClearSplCalibration_Click(object? sender, EventArgs e)
        {
            splCalibration = null;
            UpdateSplCalibrationButton();
            RaiseCalibrationChanged();
        }

        // The capture side of the currently selected audio configuration, with no
        // loopback: the SPL calibration listens to the microphone alone against an
        // external calibrator. Playback is silent, so the render selection only
        // needs to be openable.
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
            int count = additionalMicrophoneCalibrations.Count;
            buttonCalibrationExtra.Text = count == 0
                ? "Manage..."
                : $"Manage... ({count})";
            deviceToolTip.SetToolTip(
                buttonCalibrationExtra,
                "Further calibration files, and curves estimated from one of them for " +
                "an angle of incidence. Every analysis mode can then be corrected with " +
                "any of them.");
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
            if (initializing)
            {
                return;
            }

            RefreshSweepBandPreview();
            RaiseSweepSettingsChanged();
        }

        private void averagingSetting_Changed(object? sender, EventArgs e) =>
            RaiseSweepSettingsChanged();

        // Keeps low < high while the user edits either bound, then refreshes the
        // achieved-range preview. Guarded so the cross-adjustment does not recurse.
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

        // Fills the read-only Compute Duration field and the achieved-range line
        // from the values shown in the panel, not from the last generated sweep's
        // state (which is stale until the next run).
        private void RefreshSweepBandPreview()
        {
            if (comboBoxSampleRate.SelectedItem is not int)
            {
                // Nothing is selected because nothing is offered: no rate opens for this
                // configuration. GetSelectedSampleRate answers 44.1 kHz to callers that
                // need a number anyway, and a band and duration computed from it would
                // describe a sweep this configuration cannot run.
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
            // The achieved band, its octave span and the resulting total sweep
            // duration, all in one line (there is no separate duration field).
            labelActualRangeCaption.Text = spec.IsValid
                ? $"{spec.LowFrequencyHz:0.#}–{spec.HighFrequencyHz:0} Hz · " +
                    $"{spec.OctaveSpan:0.00} oct · {spec.ComputedDurationSeconds:0.00} s"
                : "—";
            // The line already shows the truth, but silently, so say out loud when
            // the sweep does not deliver what the fields above ask for.
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

        // Null when the sweep delivers the requested band at full amplitude within
        // the length limit; otherwise what the user is actually getting instead.
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

            // Full amplitude needs a whole cycle plus room for the fade, so a short
            // sweep falls short at the bottom first.
            return $"⚠ Full amplitude only from {spec.FullAmplitudeLowFrequencyHz:0.#} " +
                $"to {spec.FullAmplitudeHighFrequencyHz:0} Hz: one cycle at " +
                $"{requestedLowHz:0.#} Hz already takes " +
                $"{1000.0 / Math.Max(requestedLowHz, 1e-9):0} ms. Raise the " +
                "per-octave time to reach the requested band.";
        }

        // Writes the sweep the panel currently describes — the same samples the
        // next measurement would play, on the channels the playback selection
        // routes them to — so it can be played from something that is not
        // Resonalyze (a phone, a head unit, a test disc) while this still
        // records. The band and pace are read from the controls; the sample rate
        // is the SELECTED one, matching the achieved-range line above the button
        // rather than whatever the last Apply committed.
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
                // A sweep of its own, not the live measurement's: generating into
                // that one would discard the result currently on screen.
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
            // Probe the new driver before rebuilding the rate list: with ASIO the
            // list comes out of that probe.
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

            // The playback channel count decides which sample rates the device can
            // open, so the rate list is rebuilt before the change is applied.
            RefreshSampleRateOptions(GetSelectedSampleRate());
            RaiseSweepSettingsChanged();
        }

        private void comboBoxSampleRate_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            // The user picked this rate, so nothing was taken away from them: the
            // marker left by an earlier automatic fallback describes an action that is
            // now over, and UpdateAsioStatusLabels would otherwise keep reporting
            // "96000 Hz is not offered — changed to 48000 Hz" about a rate the user
            // chose. The probe verdict is not cleared here but re-taken below: the
            // re-probe for the new rate is a fresh answer, and RefreshAsioDriverInfo
            // settles the flag from it before the status line is written.
            sampleRateFellBackFrom = null;

            // The achieved band and its cycle-quantized duration depend on the
            // sample rate, so re-preview on any change (not just for ASIO). The
            // rate itself belongs to the audio backend group and is not applied
            // until the Apply button commits it — only the preview moves here.
            RefreshSweepBandPreview();
            if (IsSelectedWasapiBackend())
            {
                // The endpoint status line reports on the SELECTED rate, and picking
                // another one out of a list that already holds it does not rebuild the
                // list — so RefreshSampleRateOptions, the other place that rewrites the
                // line, never runs here. Without this the line goes on naming the rate
                // the user just moved away from. Only for WASAPI: the other branches of
                // UpdateWaveLoopbackControls move the loopback selection, which would
                // re-enter through its own SelectedIndexChanged.
                UpdateWaveLoopbackControls();
            }
            if (comboBoxAudioBackend.SelectedIndex != (int)AudioBackend.Asio)
            {
                return;
            }

            // Buffer size and latency are rate-dependent, so the driver is probed
            // again for the new rate; the rate list itself does not change.
            RefreshAsioDriverInfo(
                GetSelectedSampleRate(),
                GetSelectedAsioInputChannelOffset(),
                GetSelectedAsioOutputChannelOffset(),
                GetSelectedAsioLoopbackInputChannelOffset());
            UpdateAudioBackendControls();
        }

        private void UpdateAudioBackendControls()
        {
            // The array is per backend, so the button's count changes with the
            // selection, not only when the array itself is edited.
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
            // Refresh the stale marker: the calibration is pinned to one input, so
            // switching backend/device must flag it if it no longer matches.
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
                // Re-probe first: the control panel may have changed the buffer
                // size or the rates the driver reports.
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

        // Opens the selected driver once and caches everything read from it,
        // including the sample rates GetSupportedSampleRates then serves. The rate
        // is passed in because Init probes before the rate control exists.
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
            // The probe just happened, so its verdict is settled here rather than
            // wherever the rate list is next rebuilt. Not every caller rebuilds one:
            // a manual rate change re-probes for the latency figures alone, and
            // leaving the flag behind let a probe that has since SUCCEEDED still be
            // reported as "the driver did not report its rates". RefreshSampleRateOptions
            // recomputes the same predicate for the resolution it acts on.
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
            if (sampleRateProbeFailed)
            {
                // The number is the live selection and the verdict came from the last
                // probe; when that probe told us nothing the two must not be combined
                // into a confident sentence about a rate nobody tested.
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
                        // Not "that rate is unsupported": no rate is, so the combo is
                        // empty and GetSelectedSampleRate is answering with its own
                        // fallback — naming it here would report on a rate nobody
                        // offered. What was refused is the format, and Exclusive hands
                        // it to the endpoint unchanged, so the way out is to ask for a
                        // different one. Mono is the usual reason: it asks for a
                        // one-channel format, and endpoints that only accept their
                        // native stereo one then refuse at every rate.
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

        // Empty is an answer here, never silence: a Wave pair reports no rate in common
        // only when there is none. Skipping validation on it let the rate sitting in the
        // combo through to a device that cannot open it.
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
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                // Opening the ASIO driver is a synchronous COM instantiation
                // that can take seconds; don't pay it for Wave-side changes. It
                // has to happen before the rate list, which ASIO reads off it.
                RefreshAsioDriverInfo(
                    preferredSampleRate,
                    GetSelectedAsioInputChannelOffset(),
                    GetSelectedAsioOutputChannelOffset(),
                    GetSelectedAsioLoopbackInputChannelOffset());
            }
            RefreshSampleRateOptions(preferredSampleRate);
            UpdateAudioBackendControls();
        }

        /// <summary>
        /// Re-reads the audio device after the host has applied these settings to it.
        /// </summary>
        /// <remarks>
        /// The panel's picture of the driver is a snapshot, taken when the panel opened
        /// or when a control changed — and Apply reconfigures the device UNDER it while
        /// it stays on screen. So a rate change probed the driver while it was still
        /// open at the old rate, got "not supported", painted the status amber, and
        /// kept it there: the driver was reinitialised at the new rate a moment later
        /// and nothing asked it again. Reopening the panel was the only way to get a
        /// straight answer, which is the tell that the view had gone stale rather than
        /// the device being wrong.
        /// </remarks>
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
                // Nothing is rebuilt on the absence of an answer: the list and the
                // user's selection stand, and the status line says the driver did not
                // report. Rebuilding here is what used to replace a working 96 kHz with
                // 44.1 and persist it on the next Apply.
                //
                // The flags above have just changed, and the last thing to write the
                // status line was RefreshAsioDriverInfo, before Resolve ran — so it is
                // still rendered from the previous state and would keep a stale
                // supported/not-supported sentence. Write it here too, exactly as the
                // settled path below does.
                if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
                {
                    UpdateAsioStatusLabels();
                }
                return;
            }

            int[] availableRates = resolution.Rates;
            int selectedSampleRate = resolution.Selected;
            // An empty list is a real outcome, not a missing one: no rate works for this
            // configuration, so the combo offers nothing and Apply refuses. Filling in the
            // configured rate here is what used to hand the user a rate no device reported.

            bool wasInitializing = initializing;
            initializing = true;
            try
            {
                comboBoxSampleRate.Items.Clear();
                comboBoxSampleRate.Items.AddRange(
                    availableRates
                        .Select(rate => (object)rate)
                        .ToArray());
                // -1 on the empty list, which is a list nobody can select from rather
                // than a missing one. Asking for entry 0 there throws, and the throw
                // used to escape mid-rebuild with the guard still raised, leaving every
                // combo in the window deaf to selection until it was reopened.
                comboBoxSampleRate.SelectedIndex = SampleRateOptions.FindRateIndex(
                    availableRates,
                    selectedSampleRate);
            }
            finally
            {
                initializing = wasInitializing;
            }
            // A device/backend change can move the selected sample rate under the
            // initializing guard (so comboBoxSampleRate_SelectedIndexChanged is
            // suppressed); refresh the achieved-range and Compute Duration preview
            // here so they never lag the rate. Skipped during Init, which previews
            // once at the end.
            if (!initializing)
            {
                RefreshSweepBandPreview();
            }

            // The status line pairs a number taken from the selection with a verdict
            // taken from the probe, so it can only be written once the selection has
            // settled. RefreshAsioDriverInfo writes it before this method has filled the
            // combo, when GetSelectedSampleRate still answers with its own fallback —
            // which is how "96000" in the list came to sit above "44100 Hz supported"
            // in green. Writing it again here is what keeps the two halves in step.
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                UpdateAsioStatusLabels();
            }
            else if (IsSelectedWasapiBackend())
            {
                // Same reason, for the endpoint status line: it reports on the rate list
                // this method has just rebuilt. The playback channel and the microphone
                // channel both rebuild it without going through UpdateAudioBackendControls,
                // and those are exactly the two things the "no rate opens" line asks the
                // user to change — so without this it would still say so afterwards.
                UpdateWaveLoopbackControls();
            }
        }

        // The driver name is what decides whether there is anything to preserve, the
        // same test RefreshAsioDriverInfo uses for the saved channel routing.
        private bool IsAsioSampleRateProbeFailure() =>
            SampleRateOptions.IsProbeFailure(
                comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio,
                asioDriverInfo.DriverName,
                asioDriverInfo.SupportedSampleRates.Count);

        private IReadOnlyList<int> GetSupportedSampleRates()
        {
            if (comboBoxAudioBackend.SelectedIndex == (int)AudioBackend.Asio)
            {
                // From the last driver probe, not a fresh open: RefreshAsioDriverInfo
                // always runs first, and a second open of the same driver moments
                // later is what some drivers refuse (leaving an empty rate list).
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

        /// <summary>
        /// The array microphones this panel would actually record on the selected
        /// device: configured, not colliding with the measurement pair, and present on
        /// the interface now chosen.
        /// </summary>
        /// <remarks>
        /// The same rule <c>MeasurementSettingsFile.ResolveArrayChannels</c> applies
        /// when it builds the configuration, because a panel that offered a sample
        /// rate for a narrower capture than the measurement will open is a panel that
        /// says Supported and then fails at the device.
        /// </remarks>
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

        /// <summary>
        /// How many input channels the measurement will ask the device to open.
        /// </summary>
        /// <remarks>
        /// Answered by <see cref="AudioCaptureRouting.RequiredInputChannelCount"/>, the
        /// very property every backend opens its capture with, so the width this panel
        /// probes a format at and the width the measurement asks for cannot drift. They
        /// did: this counted the microphone and the loopback and stopped there, so an
        /// interface that supports two channels at 96 kHz but not eight was offered the
        /// rate, said Supported, and failed at the device when the sweep ran.
        /// </remarks>
        private int GetSelectedWaveRecordingChannelCount()
        {
            int microphone = GetSelectedWaveInputChannelOffset();
            int? loopback = GetSelectedWaveLoopbackChannelOffset();
            var routing = new AudioCaptureRouting(microphone, loopback)
            {
                ArrayChannels = SelectedReachableArrayChannels()
            };
            // The microphone on channel 2 (offset 1) needs a 2-channel format even
            // without a loopback selection, and a loopback needs two whichever
            // channels the pair sits on.
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
