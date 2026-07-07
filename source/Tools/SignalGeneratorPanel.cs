using System.ComponentModel;
using NAudio.Wave;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed record SignalGeneratorPlaybackSettings(
    AudioBackend Backend,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    int WaveOutputDeviceNumber,
    string? AsioDriverName,
    int AsioOutputChannelOffset);

public partial class SignalGeneratorPanel : UserControl
{
    private IWavePlayer? player;
    private WaveStream? playbackStream;
    private PcmStreamSet? pcmStreams;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<SignalGeneratorPlaybackSettings>? PlaybackSettingsProvider { get; set; }

    public SignalGeneratorPanel()
    {
        InitializeComponent();
        InitializeOptions();
        UpdateSignalControls();
        VisibleChanged += (_, _) => RefreshAudioSettings();
        Disposed += (_, _) => DisposePlayback();
    }

    private void InitializeOptions()
    {
        comboBoxSignalType.Items.Clear();
        comboBoxSignalType.Items.AddRange(
        [
            new SignalTypeOption(SignalGeneratorType.PinkPeriodicNoise, "Pink noise (periodic)"),
            new SignalTypeOption(SignalGeneratorType.PinkNoise, "Pink noise"),
            new SignalTypeOption(SignalGeneratorType.BrownNoise, "Brown / red noise"),
            new SignalTypeOption(SignalGeneratorType.WhiteNoise, "White noise"),
            new SignalTypeOption(SignalGeneratorType.Sine, "Sine")
        ]);
        comboBoxSignalType.SelectedIndex = 0;

        comboBoxSignalType.SelectedIndexChanged += (_, _) => UpdateSignalControls();
        buttonPlay.Click += (_, _) => PlaySelectedSignal();
        buttonStop.Click += (_, _) => StopPlayback();
    }

    private void UpdateSignalControls()
    {
        bool isSine = SelectedSignalType == SignalGeneratorType.Sine;
        numericFrequency.Enabled = isSine;
        Ui.UiStyle.SetTextEnabledLook(labelFrequency, isSine);
        labelStatus.Text = player?.PlaybackState == PlaybackState.Playing
            ? "Playing"
            : "Ready";
    }

    private SignalGeneratorType SelectedSignalType =>
        comboBoxSignalType.SelectedItem is SignalTypeOption option
            ? option.Type
            : SignalGeneratorType.PinkPeriodicNoise;

    private void PlaySelectedSignal()
    {
        try
        {
            StopPlayback();
            SignalGeneratorPlaybackSettings settings = GetPlaybackSettings();
            RefreshAudioSettings(settings);

            int durationSeconds = (int)numericDuration.Value;
            float[] monoSamples = CreateMonoSamples(
                settings.SampleRate,
                durationSeconds,
                SelectedSignalType,
                (double)numericFrequency.Value,
                (double)numericLevel.Value / 100.0);
            // 10 ms ramps: an abrupt start/end is an audible click, unfriendly
            // to tweeters at the levels this tool is used at.
            SignalFade.ApplyFadeInOut(monoSamples, settings.SampleRate / 100);

            if (settings.Backend == AudioBackend.Asio)
            {
                player = CreateAsioPlayer(settings);
                playbackStream = FloatArrayWaveStream.FromMonoSamples(
                    monoSamples,
                    settings.SampleRate,
                    settings.PlaybackChannel);
            }
            else
            {
                player = new WaveOutEvent
                {
                    DeviceNumber = settings.WaveOutputDeviceNumber
                };
                pcmStreams = new PcmStreamSet(
                    monoSamples,
                    settings.SampleRate,
                    settings.BitsPerSample);
                playbackStream = pcmStreams.GetStream(settings.PlaybackChannel);
            }

            player.Init(playbackStream);
            player.PlaybackStopped += PlayerPlaybackStopped;
            player.Play();
            buttonPlay.Enabled = false;
            buttonStop.Enabled = true;
            labelStatus.Text = "Playing";
        }
        catch (Exception exception)
        {
            DisposePlayback();
            buttonPlay.Enabled = true;
            buttonStop.Enabled = false;
            labelStatus.Text = exception.Message;
        }
    }

    private AsioOut CreateAsioPlayer(SignalGeneratorPlaybackSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AsioDriverName))
        {
            throw new InvalidOperationException("ASIO driver is not selected.");
        }

        var asio = new AsioOut(settings.AsioDriverName)
        {
            AutoStop = true,
            ChannelOffset = settings.AsioOutputChannelOffset
        };
        if (!asio.IsSampleRateSupported(settings.SampleRate))
        {
            throw new InvalidOperationException(
                $"ASIO driver '{settings.AsioDriverName}' does not support {settings.SampleRate} Hz.");
        }
        if (settings.AsioOutputChannelOffset < 0 ||
            settings.AsioOutputChannelOffset + 2 > asio.DriverOutputChannelCount)
        {
            throw new InvalidOperationException(
                $"ASIO output channel pair starting at {settings.AsioOutputChannelOffset + 1} is not available.");
        }

        return asio;
    }

    private SignalGeneratorPlaybackSettings GetPlaybackSettings()
    {
        if (PlaybackSettingsProvider == null)
        {
            throw new InvalidOperationException("Playback settings are not connected.");
        }

        return PlaybackSettingsProvider();
    }

    private float[] CreateMonoSamples(
        int sampleRate,
        int durationSeconds,
        SignalGeneratorType signalType,
        double frequencyHz,
        double level)
    {
        int samples = checked(sampleRate * durationSeconds);
        var result = new float[samples];
        level = Math.Clamp(level, 0.0, 1.0);

        if (signalType != SignalGeneratorType.Sine)
        {
            using var signal = new NoiseSignal();
            signal.FillData(
                durationSeconds,
                24,
                sampleRate,
                ToNoiseColor(signalType));
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (float)Math.Clamp(signal.FloatData[i] * level * 2.0, -1.0, 1.0);
            }

            return result;
        }

        double angularStep = 2.0 * Math.PI * frequencyHz / sampleRate;
        for (int sampleIndex = 0; sampleIndex < result.Length; sampleIndex++)
        {
            result[sampleIndex] = (float)(Math.Sin(sampleIndex * angularStep) * level);
        }

        return result;
    }

    private void PlayerPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        void UpdateUi()
        {
            DisposePlayback();
            buttonPlay.Enabled = true;
            buttonStop.Enabled = false;
            labelStatus.Text = e.Exception == null
                ? "Ready"
                : e.Exception.Message;
        }

        if (IsDisposed)
        {
            DisposePlayback();
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)UpdateUi);
            }
            catch (InvalidOperationException)
            {
                // The handle was destroyed between the guard and the call.
                DisposePlayback();
            }
            return;
        }

        UpdateUi();
    }

    private void StopPlayback()
    {
        if (player?.PlaybackState == PlaybackState.Playing)
        {
            player.Stop();
            return;
        }

        DisposePlayback();
        if (!IsDisposed)
        {
            buttonPlay.Enabled = true;
            buttonStop.Enabled = false;
            labelStatus.Text = "Ready";
        }
    }

    private void DisposePlayback()
    {
        if (player != null)
        {
            player.PlaybackStopped -= PlayerPlaybackStopped;
            player.Dispose();
            player = null;
        }

        playbackStream?.Dispose();
        playbackStream = null;
        pcmStreams?.Dispose();
        pcmStreams = null;
    }

    internal void RefreshAudioSettings()
    {
        try
        {
            RefreshAudioSettings(GetPlaybackSettings());
        }
        catch
        {
            labelAudioSettings.Text = "Audio settings are not available";
        }
    }

    private void RefreshAudioSettings(SignalGeneratorPlaybackSettings settings)
    {
        string backend = settings.Backend == AudioBackend.Asio ? "ASIO" : "Wave";
        string output = settings.Backend == AudioBackend.Asio
            ? GetAsioOutputName(settings)
            : GetWaveOutputName(settings.WaveOutputDeviceNumber);
        labelAudioSettings.Text =
            $"{backend}: {settings.SampleRate} Hz, {settings.BitsPerSample}-bit, " +
            $"{settings.PlaybackChannel}, {output}";
    }

    private static string GetWaveOutputName(int deviceNumber) =>
        AudioDeviceCatalog.GetPlaybackDevices()
            .FirstOrDefault(device => device.DeviceNumber == deviceNumber)
            ?.Name ?? "Default playback device";

    private static string GetAsioOutputName(SignalGeneratorPlaybackSettings settings)
    {
        AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
            settings.AsioDriverName,
            settings.SampleRate);
        AsioChannelInfo? channel = driverInfo.OutputChannels
            .FirstOrDefault(candidate => candidate.Offset == settings.AsioOutputChannelOffset);
        return channel == null
            ? settings.AsioDriverName ?? "ASIO driver"
            : $"{settings.AsioDriverName}: {channel.Name}";
    }

    private static NoiseColor ToNoiseColor(SignalGeneratorType type) =>
        type switch
        {
            SignalGeneratorType.PinkNoise => NoiseColor.Pink,
            SignalGeneratorType.BrownNoise => NoiseColor.Brown,
            SignalGeneratorType.WhiteNoise => NoiseColor.White,
            _ => NoiseColor.PinkPeriodic
        };

    private enum SignalGeneratorType
    {
        PinkPeriodicNoise,
        PinkNoise,
        BrownNoise,
        WhiteNoise,
        Sine
    }

    private sealed class SignalTypeOption
    {
        public SignalTypeOption(SignalGeneratorType type, string displayName)
        {
            Type = type;
            DisplayName = displayName;
        }

        public SignalGeneratorType Type { get; }

        private string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
