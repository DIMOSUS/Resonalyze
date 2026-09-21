using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Resonalyze.Dsp;

using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions : Form
    {
        private readonly WrappingToolTip deviceToolTip = new();
        private readonly IRecordDevices devices;
        private readonly RecordSettingsSession session;
        // Null under the designer constructor, which disables the Calibrate button.
        private readonly IAudioSessionFactory? audioSessionFactory;
        private readonly (ThemedComboBox Combo, RecordChoice Choice)[] choices;
        private readonly (ThemedNumericUpDown Field, RecordNumber Number)[] numbers;
        private Font? normalStatusFont;
        private Font? warningStatusFont;
        private bool presenting;

        // Raised on any calibration change so the host persists it immediately, not on Apply.
        internal event Action<RecordCalibrationSelection>? CalibrationChanged;
        // Only the audio-backend group (backend, rate, bit depth, device panel) waits for Apply; everything else raises this.
        public event Action? SweepSettingsChanged;

        public MeasurementOptions()
            : this(null, new SystemRecordDevices())
        {
        }

        public MeasurementOptions(IAudioSessionFactory audioSessionFactory)
            : this(
                audioSessionFactory ?? throw new ArgumentNullException(nameof(audioSessionFactory)),
                new SystemRecordDevices())
        {
        }

        internal MeasurementOptions(IAudioSessionFactory? audioSessionFactory, IRecordDevices devices)
        {
            this.audioSessionFactory = audioSessionFactory;
            this.devices = devices;
            InitializeComponent();
            session = new RecordSettingsSession(devices);
            session.SweepSettingsChanged += () => SweepSettingsChanged?.Invoke();
            session.CalibrationChanged += selection => CalibrationChanged?.Invoke(selection);
            choices =
            [
                (comboBoxAudioBackend, session.Backend),
                (comboBoxChannel, session.PlaybackChannel),
                (comboBoxPlaybackDevice, session.PlaybackDevice),
                (comboBoxRecordingDevice, session.RecordingDevice),
                (comboBoxWaveInputChannel, session.WaveInput),
                (comboBoxWaveLoopbackChannel, session.WaveLoopback),
                (comboBoxAsioDriver, session.AsioDriver),
                (comboBoxAsioInputChannel, session.AsioInput),
                (comboBoxAsioOutputChannel, session.AsioOutput),
                (comboBoxAsioLoopbackChannel, session.AsioLoopback),
                (comboBoxSampleRate, session.SampleRate),
                (comboBoxProtectiveHighPassKind, session.HighPassKind),
                (comboBoxProtectiveHighPassSlope, session.HighPassSlope),
                (comboBoxMicrophoneCalibration, session.MicrophoneCalibration)
            ];
            numbers =
            [
                (numericUpDownBits, session.Bits),
                (numericUpDownLowFrequency, session.LowFrequency),
                (numericUpDownHighFrequency, session.HighFrequency),
                (numericUpDownRequestedDuration, session.OctavePaceMilliseconds),
                (numericUpDownProtectiveHighPassFrequency, session.HighPassFrequency),
                (numericUpDownAverageRunCount, session.AverageRunCount)
            ];
            WireFields();
            WireToolTips();
            devices.EndpointsChanged += HandleEndpointsChanged;
            Disposed += (_, _) =>
            {
                devices.EndpointsChanged -= HandleEndpointsChanged;
                devices.Dispose();
            };
            Present();
        }

        private ThemedComboBox comboBoxPlaybackDevice => waveAudioBackendPanel.ComboBoxPlaybackDevice;

        private ThemedComboBox comboBoxRecordingDevice => waveAudioBackendPanel.ComboBoxRecordingDevice;

        private ThemedComboBox comboBoxWaveInputChannel => waveAudioBackendPanel.ComboBoxWaveInputChannel;

        private ThemedComboBox comboBoxWaveLoopbackChannel => waveAudioBackendPanel.ComboBoxWaveLoopbackChannel;

        private Label labelPlaybackDevice => waveAudioBackendPanel.LabelPlaybackDevice;

        private Label labelRecordingDevice => waveAudioBackendPanel.LabelRecordingDevice;

        private Label labelWaveInputChannel => waveAudioBackendPanel.LabelWaveInputChannel;

        private Label labelWaveLoopbackChannel => waveAudioBackendPanel.LabelWaveLoopbackChannel;

        private Label labelWaveLoopbackStatus => waveAudioBackendPanel.LabelWaveLoopbackStatus;

        private Label labelDeviceSettings => waveAudioBackendPanel.LabelDeviceSettings;

        private Button buttonDeviceSettings => waveAudioBackendPanel.ButtonDeviceSettings;

        private ThemedComboBox comboBoxAsioDriver => asioAudioBackendPanel.ComboBoxAsioDriver;

        private ThemedComboBox comboBoxAsioInputChannel => asioAudioBackendPanel.ComboBoxAsioInputChannel;

        private ThemedComboBox comboBoxAsioOutputChannel => asioAudioBackendPanel.ComboBoxAsioOutputChannel;

        private ThemedComboBox comboBoxAsioLoopbackChannel => asioAudioBackendPanel.ComboBoxAsioLoopbackChannel;

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

        internal void Init(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            if (expSweepMeasurement.Sweep == null)
            {
                throw new InvalidOperationException("Sweep measurement is not initialized.");
            }

            session.Load(settings);
            Present();
        }

        internal void SetOptions(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            ApplyRoute(expSweepMeasurement, settings);
        }

        /// <summary>Writes the immediately-applied half of the panel; the backend group waits for <see cref="SetOptions"/>.</summary>
        /// <remarks>Nothing is pushed into <see cref="ExpSweepMeasurement"/>: its <c>Init</c> discards the measured result.</remarks>
        internal void ApplySweepSettings(MeasurementSettingsFile.SweepMeasurementSettings settings) =>
            session.WriteLiveSettings(settings);

        /// <summary>Adopts a list the shell changed behind this panel, so the next Apply does not write back the stale one.</summary>
        internal void AdoptAdditionalCalibrations(IReadOnlyList<MicrophoneCalibrationDefinition> definitions)
        {
            session.AdoptAdditionalCalibrations(definitions);
            Present();
        }

        /// <summary>Re-reads the device after Apply reconfigured it under the open panel (otherwise the status stays stale).</summary>
        internal void RefreshAudioDeviceView()
        {
            if (IsDisposed)
            {
                return;
            }

            session.RefreshAudioDevice();
            Present();
        }

        private void WireFields()
        {
            foreach ((ThemedComboBox combo, RecordChoice choice) in choices)
            {
                combo.SelectedIndexChanged += (_, _) =>
                {
                    if (presenting)
                    {
                        return;
                    }

                    choice.SelectedIndex = combo.SelectedIndex;
                    Present();
                };
            }

            foreach ((ThemedNumericUpDown field, RecordNumber number) in numbers)
            {
                field.ApplyFieldRange(number.Range);
                field.ValueChanged += (_, _) =>
                {
                    if (presenting)
                    {
                        return;
                    }

                    number.Value = field.Value;
                    Present();
                };
            }

            comboBoxProtectiveHighPassKind.Format += (_, args) =>
            {
                if (args.ListItem is ProtectiveHighPassKind kind)
                {
                    args.Value = RecordHighPass.KindLabel(kind);
                }
            };
            comboBoxProtectiveHighPassSlope.Format += (_, args) =>
            {
                if (args.ListItem is int slope)
                {
                    args.Value = RecordHighPass.SlopeLabel(slope);
                }
            };
            buttonAsioInputProbe.Click += buttonAsioInputProbe_Click;
            buttonAsioControlPanel.Click += buttonAsioControlPanel_Click;
            buttonDeviceSettings.Click += buttonDeviceSettings_Click;
        }

        private void WireToolTips()
        {
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

        // Writes every control from the session; the controls' own change events are ignored meanwhile.
        private void Present()
        {
            presenting = true;
            try
            {
                foreach ((ThemedComboBox combo, RecordChoice choice) in choices)
                {
                    PresentChoice(combo, choice);
                }

                foreach ((ThemedNumericUpDown field, RecordNumber number) in numbers)
                {
                    field.Value = number.Value;
                }

                UpdateComboBoxToolTip(comboBoxPlaybackDevice);
                UpdateComboBoxToolTip(comboBoxRecordingDevice);
                UpdateComboBoxToolTip(comboBoxAsioDriver);
                PresentDevices();
                PresentCalibrations();
                PresentSweepBand();
                bool highPassEditable = RecordHighPass.IsEditable(session);
                numericUpDownProtectiveHighPassFrequency.Enabled = highPassEditable;
                comboBoxProtectiveHighPassSlope.Enabled = highPassEditable;
            }
            finally
            {
                presenting = false;
            }
        }

        private void PresentChoice(ThemedComboBox combo, RecordChoice choice)
        {
            if (!combo.Items.Cast<object>().SequenceEqual(choice.Items))
            {
                combo.Items.Clear();
                combo.Items.AddRange(choice.Items.ToArray());
                if (combo == comboBoxPlaybackDevice ||
                    combo == comboBoxRecordingDevice ||
                    (combo == comboBoxAsioDriver && combo.Items.Count > 0))
                {
                    ConfigureDropDownWidth(combo);
                }
            }

            if (combo.SelectedIndex != choice.SelectedIndex)
            {
                combo.SelectedIndex = choice.SelectedIndex;
            }
        }

        private void PresentDevices()
        {
            RecordDeviceView view = RecordDeviceStatus.Read(session);
            bool useAsio = view.UseAsio;
            waveAudioBackendPanel.Visible = !useAsio;
            asioAudioBackendPanel.Visible = useAsio;
            comboBoxPlaybackDevice.Enabled = !useAsio;
            comboBoxRecordingDevice.Enabled = !useAsio;
            comboBoxWaveInputChannel.Enabled = !useAsio;
            comboBoxWaveLoopbackChannel.Enabled = view.WaveLoopbackEnabled;
            comboBoxAsioDriver.Enabled = view.AsioDriverEnabled;
            buttonAsioControlPanel.Enabled = view.AsioControlPanelEnabled;
            buttonAsioInputProbe.Enabled = view.AsioInputProbeEnabled;
            comboBoxAsioInputChannel.Enabled = view.AsioInputsEnabled;
            comboBoxAsioLoopbackChannel.Enabled = view.AsioInputsEnabled;
            comboBoxAsioOutputChannel.Enabled = view.AsioOutputEnabled;
            UiStyle.SetTextEnabledLook(labelAsioDriver, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioInputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioOutputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioLoopbackChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioSampleRate, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatency, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatencyValue, useAsio);
            UiStyle.SetTextEnabledLook(labelPlaybackDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelRecordingDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveInputChannel, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveLoopbackChannel, !useAsio);
            labelDeviceSettings.Visible = view.UseWasapi;
            buttonDeviceSettings.Visible = view.UseWasapi;
            buttonDeviceSettings.Enabled = view.UseWasapi;
            labelPlaybackDevice.Text = view.PlaybackDeviceCaption;
            labelRecordingDevice.Text = view.RecordingDeviceCaption;
            labelWaveInputChannel.Text = view.WaveInputCaption;
            labelWaveLoopbackChannel.Text = view.WaveLoopbackCaption;
            labelWaveLoopbackStatus.Font = view.LoopbackStatus.Emphasized ? WarningStatusFont : NormalStatusFont;
            labelWaveLoopbackStatus.Text = view.LoopbackStatus.Text;
            labelWaveLoopbackStatus.ForeColor = view.LoopbackStatus.Color;
            // Only ASIO's own route has a verdict; the hidden line keeps the last one, greyed.
            if (!useAsio)
            {
                labelAsioSampleRateStatus.ForeColor = UiPalette.TextDisabled;
                return;
            }

            labelAsioSampleRateStatus.Text = view.AsioSampleRateStatus.Text;
            labelAsioSampleRateStatus.ForeColor = view.AsioSampleRateStatus.Color;
            labelAsioPlaybackLatencyValue.Text = view.AsioPlaybackLatency;
        }

        private void PresentCalibrations()
        {
            string? zeroDegreePath = session.MicrophoneCalibration0DegreesPath;
            string? problem = session.ZeroDegreeCalibrationProblem;
            buttonCalibration0.Text = zeroDegreePath == null
                ? "Select file..."
                : Path.GetFileName(zeroDegreePath);
            buttonCalibration0.ForeColor = problem != null ? UiPalette.Error : UiPalette.TextPrimary;
            buttonClearCalibration0.Enabled = zeroDegreePath != null;
            deviceToolTip.SetToolTip(
                buttonCalibration0,
                zeroDegreePath == null
                    ? "No calibration file selected."
                    : problem ?? zeroDegreePath);
            deviceToolTip.SetToolTip(
                buttonClearCalibration0,
                zeroDegreePath == null
                    ? "No calibration file selected."
                    : "Clear selected calibration file.");
            int count = session.AdditionalMicrophoneCalibrations.Count;
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
            comboBoxMicrophoneCalibration.Enabled = session.MicrophoneCalibration.Items.Count > 1;
            buttonArrayMicrophones.Text = RecordArrayInputs.ButtonText(session);
            PresentSplCalibration();
        }

        private void PresentSplCalibration()
        {
            SplCalibration? splCalibration = session.SplCalibration;
            buttonSplCalibration.Enabled = audioSessionFactory != null;
            buttonClearSplCalibration.Enabled = splCalibration != null;

            if (splCalibration == null)
            {
                buttonSplCalibration.Text = "Calibrate...";
                buttonSplCalibration.ForeColor = UiPalette.TextPrimary;
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
            buttonSplCalibration.ForeColor = stale ? UiPalette.Warning : UiPalette.TextPrimary;
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
            var backend = (AudioBackend)session.Backend.SelectedIndex;
            return calibration.MatchesInput(
                backend,
                session.SelectedSampleRate,
                (int)session.Bits.Value,
                backend == AudioBackend.Asio ? session.SelectedAsioInputOffset : session.SelectedWaveInputOffset,
                backend == AudioBackend.Wave ? session.SelectedRecordingDeviceNumber : null,
                session.SelectedCaptureEndpoint?.Id,
                session.SelectedAsioDriverName);
        }

        // Microphone only, no loopback: calibrated against an external calibrator.
        private AudioSessionRequest BuildCalibrationCaptureRequest() =>
            AudioSessionRequestBuilder.Build(
                (AudioBackend)session.Backend.SelectedIndex,
                session.SelectedSampleRate,
                (int)session.Bits.Value,
                session.SelectedPlaybackChannel,
                waveInputChannelOffset: session.SelectedWaveInputOffset,
                waveLoopbackInputChannelOffset: null,
                asioInputChannelOffset: session.SelectedAsioInputOffset,
                asioLoopbackInputChannelOffset: null,
                asioOutputChannelOffset: session.SelectedAsioOutputOffset,
                outputDeviceNumber: session.SelectedPlaybackDeviceNumber,
                inputDeviceNumber: session.SelectedRecordingDeviceNumber,
                wasapiCaptureEndpointId: session.SelectedCaptureEndpoint?.Id,
                wasapiRenderEndpointId: session.SelectedRenderEndpoint?.Id,
                asioDriverName: session.SelectedAsioDriverName,
                bufferMilliseconds: 100,
                expectedCaptureSamples: 0);

        // From the session's values, not the last generated sweep (stale until the next run).
        private void PresentSweepBand()
        {
            if (session.SampleRate.SelectedItem is not int sampleRate)
            {
                // No rate opens: the 44.1 kHz fallback would describe an unrunnable sweep.
                labelActualRangeCaption.Text = "—";
                labelActualRangeCaption.ForeColor = UiPalette.Warning;
                deviceToolTip.SetToolTip(
                    labelActualRangeCaption,
                    "No sample rate opens for the current configuration, so there is " +
                    "nothing to compute the sweep against.");
                return;
            }

            double lowHz = (double)session.LowFrequency.Value;
            double highHz = (double)session.HighFrequency.Value;
            double perOctaveSeconds = (double)session.OctavePaceMilliseconds.Value * 0.001;
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
                ? UiPalette.Success
                : UiPalette.Warning;
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

        private void ApplyRoute(
            ExpSweepMeasurement expSweepMeasurement,
            MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            session.WriteCalibrations(settings);

            int sampleRate = session.SelectedSampleRate;
            // The field is the source of truth (read-only today, equal to expSweepMeasurement.Bits).
            int bits = (int)session.Bits.Value;
            PlaybackChannel playbackChannel = session.SelectedPlaybackChannel;
            double lowFrequencyHz = (double)session.LowFrequency.Value;
            double highFrequencyHz = (double)session.HighFrequency.Value;
            double requestedDuration = session.RequestedDurationSeconds(sampleRate);
            var audioBackend = (AudioBackend)session.Backend.SelectedIndex;
            int outputDeviceNumber = session.PlaybackDevice.SelectedItem is AudioDeviceInfo playbackDevice
                ? playbackDevice.DeviceNumber
                : session.PreferredWavePlaybackDeviceNumber;
            int inputDeviceNumber = session.RecordingDevice.SelectedItem is AudioDeviceInfo recordingDevice
                ? recordingDevice.DeviceNumber
                : session.PreferredWaveRecordingDeviceNumber;
            string? asioDriverName = session.SelectedAsioDriverName;
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
                ValidateRequiredWaveLoopback(
                    session.WaveLoopback.SelectedItem is InputChannelOption { Offset: not null },
                    session.RecordingDeviceSupportsWaveLoopback);
            }
            if (audioBackend == AudioBackend.Wave)
            {
                // An empty Wave rate list means none in common; skipping validation let an unopenable rate through.
                SampleRateOptions.ValidateSelectedRate(session.SupportedSampleRates(), sampleRate, "Wave devices");
            }
            int asioInputChannelOffset = session.SelectedAsioInputOffset;
            int? asioLoopbackInputChannelOffset = session.SelectedAsioLoopbackOffset;
            if (audioBackend == AudioBackend.Asio &&
                asioLoopbackInputChannelOffset.HasValue &&
                asioLoopbackInputChannelOffset.Value == asioInputChannelOffset)
            {
                throw new InvalidOperationException(
                    "Microphone and loopback inputs must use different ASIO channels.");
            }
            int asioOutputChannelOffset = session.SelectedAsioOutputOffset;
            int waveInputChannelOffset = session.SelectedWaveInputOffset;
            int? waveLoopbackInputChannelOffset = session.SelectedWaveLoopbackOffset;
            int averageRunCount = (int)session.AverageRunCount.Value;
            string? wasapiCaptureEndpointId =
                session.SelectedCaptureEndpoint?.Id ?? session.PreferredWasapiCaptureEndpointId;
            string? wasapiRenderEndpointId =
                session.SelectedRenderEndpoint?.Id ?? session.PreferredWasapiRenderEndpointId;
            if (audioBackend.IsWasapi())
            {
                using IRecordEndpointReader endpoints = session.OpenEndpoints();
                AudioEndpointDescriptor captureEndpoint = SelectWasapiEndpoint(
                    endpoints.GetCaptureEndpoints(),
                    wasapiCaptureEndpointId,
                    "capture");
                AudioEndpointDescriptor renderEndpoint = SelectWasapiEndpoint(
                    endpoints.GetRenderEndpoints(),
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
                    // An empty list (no common rate) reads as 44.1 kHz; persisting that would persist a refused format.
                    // Checked after availability so a gone endpoint keeps its message.
                    SampleRateOptions.ValidateSelectedRate(
                        session.SupportedSampleRates(),
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

            string? wasapiCaptureEndpointName =
                session.SelectedCaptureEndpoint?.DisplayName ?? session.PreferredWasapiCaptureEndpointName;
            string? wasapiRenderEndpointName =
                session.SelectedRenderEndpoint?.DisplayName ?? session.PreferredWasapiRenderEndpointName;
            IReadOnlyList<int> arrayChannels = RecordArrayInputs.ReachableChannels(session);
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
                    WasapiCaptureEndpointName: wasapiCaptureEndpointName,
                    WasapiRenderEndpointName: wasapiRenderEndpointName,
                    WasapiBufferMilliseconds: settings.WasapiBufferMilliseconds,
                    // Include the array, or the applied configuration differs from what the settings build for the next run.
                    WaveArrayInputChannelOffsets: audioBackend == AudioBackend.Asio ? [] : arrayChannels,
                    AsioArrayInputChannelOffsets: audioBackend == AudioBackend.Asio ? arrayChannels : []),
                new SweepAveragingConfiguration(averageRunCount),
                RecordHighPass.Read(session)));

            settings.LowFrequencyHz = lowFrequencyHz;
            settings.HighFrequencyHz = highFrequencyHz;
            settings.WasapiCaptureEndpointId = wasapiCaptureEndpointId;
            settings.WasapiRenderEndpointId = wasapiRenderEndpointId;
            settings.WasapiCaptureEndpointName = wasapiCaptureEndpointName;
            settings.WasapiRenderEndpointName = wasapiRenderEndpointName;
            session.AdoptAppliedEndpoints(
                wasapiCaptureEndpointId,
                wasapiRenderEndpointId,
                wasapiCaptureEndpointName,
                wasapiRenderEndpointName);

            expSweepMeasurement.SplCalibration = session.SplCalibration;
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

        private void ValidateSelectedAsioDriver(int sampleRate)
        {
            AsioDriverInfo asioDriverInfo = session.AsioDriverInfo;
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

                    session.RefreshEndpoints();
                    Present();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void buttonCalibration0_Click(object? sender, EventArgs e)
        {
            session.SetZeroDegreeCalibration(SelectCalibrationFile(session.MicrophoneCalibration0DegreesPath));
            Present();
        }

        private void buttonCalibrationExtra_Click(object? sender, EventArgs e)
        {
            using var dialog = new MicrophoneCalibrationsDialog(
                session.AdditionalMicrophoneCalibrations,
                session.MicrophoneCalibration0DegreesPath,
                SelectCalibrationFile);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            session.SetAdditionalCalibrations(dialog.Definitions);
            Present();
        }

        private void buttonClearCalibration0_Click(object? sender, EventArgs e)
        {
            session.SetZeroDegreeCalibration(null);
            Present();
        }

        private void buttonArrayMicrophones_Click(object? sender, EventArgs e)
        {
            (IReadOnlyList<int> channels, string source) = RecordArrayInputs.InputChannels(session);
            using var dialog = new ArrayMicrophonesDialog(
                session.ArrayMicrophones,
                session.CalibrationEntries(),
                channels,
                RecordArrayInputs.MicrophoneChannel(session),
                RecordArrayInputs.LoopbackChannel(session),
                source);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            session.SetArrayMicrophones(dialog.Microphones);
            Present();
        }

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

            using var dialog = new SplCalibrationDialog(audioSessionFactory, request, session.SplCalibration);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
            {
                session.SetSplCalibration(dialog.Result);
                Present();
            }
        }

        private void buttonClearSplCalibration_Click(object? sender, EventArgs e)
        {
            session.SetSplCalibration(null);
            Present();
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

        // Writes the sweep the next measurement would play, for playback from another device while this records.
        // Uses the selected (not applied) rate, matching the achieved-range line.
        private void buttonSaveSweepFile_Click(object? sender, EventArgs e)
        {
            double lowFrequencyHz = (double)session.LowFrequency.Value;
            double highFrequencyHz = (double)session.HighFrequency.Value;
            int sampleRate = session.SelectedSampleRate;
            double totalSeconds = session.RequestedDurationSeconds(sampleRate);

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
                    (int)session.Bits.Value,
                    sampleRate);
                AudioFileCodec.WriteWav(
                    dialog.FileName,
                    SweepWavExport.BuildContent(
                        sweep.SweepData,
                        sampleRate,
                        session.SelectedPlaybackChannel));
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

        private void ConfigureDropDownWidth(ThemedComboBox comboBox)
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

        private void UpdateComboBoxToolTip(ThemedComboBox comboBox)
        {
            string text = comboBox.SelectedItem != null
                ? comboBox.GetItemText(comboBox.SelectedItem) ?? string.Empty
                : string.Empty;
            deviceToolTip.SetToolTip(comboBox, text);
        }

        private void buttonAsioControlPanel_Click(object? sender, EventArgs e)
        {
            try
            {
                session.ShowAsioControlPanel();
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

            Present();
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

        private async void buttonAsioInputProbe_Click(object? sender, EventArgs e)
        {
            if (session.AsioDriver.SelectedItem is not AsioDeviceInfo)
            {
                return;
            }

            try
            {
                buttonAsioInputProbe.Enabled = false;
                buttonAsioInputProbe.Text = "Testing...";
                IReadOnlyList<AsioInputProbeChannelResult> results = await session.ProbeAsioInputsAsync()!;
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
                    session.SettleRoute();
                    Present();
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

        private Font NormalStatusFont =>
            normalStatusFont ??= labelWaveLoopbackStatus.Font;

        private Font WarningStatusFont =>
            warningStatusFont ??= new Font(NormalStatusFont, FontStyle.Bold);
    }
}
