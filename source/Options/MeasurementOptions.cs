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
            RecordSettingsApply.Apply(session, expSweepMeasurement, settings);
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
    }
}
