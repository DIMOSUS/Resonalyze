using System.ComponentModel;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed record SignalGeneratorPlaybackSettings(
    AudioBackend Backend,
    int SampleRate,
    int BitsPerSample,
    PlaybackChannel PlaybackChannel,
    int WaveOutputDeviceNumber,
    string? AsioDriverName,
    int AsioOutputChannelOffset,
    string? WasapiRenderEndpointId,
    string? WasapiRenderEndpointName,
    int WasapiBufferMilliseconds);

public partial class SignalGeneratorPanel : UserControl
{
    private IAudioPlaybackSession? playbackSession;
    private CancellationTokenSource? playbackCancellation;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<SignalGeneratorPlaybackSettings>? PlaybackSettingsProvider { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal IAudioSessionFactory? AudioSessionFactory { get; set; }

    public SignalGeneratorPanel()
    {
        InitializeComponent();
        InitializeOptions();
        UpdateSignalControls();
        VisibleChanged += (_, _) => RefreshAudioSettings();
        Disposed += (_, _) => TearDownPlayback();
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
        buttonPlay.Click += async (_, _) => await PlaySelectedSignalAsync();
        buttonStop.Click += async (_, _) => await StopPlaybackFromButtonAsync();
    }

    private void UpdateSignalControls()
    {
        bool isSine = SelectedSignalType == SignalGeneratorType.Sine;
        numericFrequency.Enabled = isSine;
        Ui.UiStyle.SetTextEnabledLook(labelFrequency, isSine);
        labelStatus.Text = playbackSession != null ? "Playing" : "Ready";
    }

    private SignalGeneratorType SelectedSignalType =>
        comboBoxSignalType.SelectedItem is SignalTypeOption option
            ? option.Type
            : SignalGeneratorType.PinkPeriodicNoise;

    private async Task PlaySelectedSignalAsync()
    {
        try
        {
            await StopPlaybackAsync();
            SignalGeneratorPlaybackSettings settings = GetPlaybackSettings();
            RefreshAudioSettings(settings);
            IAudioSessionFactory factory = AudioSessionFactory ??
                throw new InvalidOperationException("Audio session factory is not connected.");

            // A sine above Nyquist does not become ultrasound — it aliases to
            // an arbitrary audible tone (96 kHz at a 44.1 kHz device plays as
            // ~7.8 kHz, loud). Refuse instead of silently playing a different
            // signal than the user asked for.
            double nyquistLimit = settings.SampleRate * 0.49;
            if (SelectedSignalType == SignalGeneratorType.Sine &&
                (double)numericFrequency.Value > nyquistLimit)
            {
                labelStatus.Text =
                    $"Frequency exceeds the device limit " +
                    $"({nyquistLimit / 1000.0:0.#} kHz at {settings.SampleRate / 1000.0:0.#} kHz).";
                System.Media.SystemSounds.Beep.Play();
                return;
            }

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

            var signal = new AudioPlaybackSignal(
                monoSamples,
                settings.SampleRate,
                settings.BitsPerSample,
                settings.PlaybackChannel,
                Loop: false);
            AudioSessionRequest request = BuildRequest(settings);
            playbackCancellation = new CancellationTokenSource();
            playbackSession = await factory.OpenPlaybackAsync(
                request, signal, playbackCancellation.Token);
            await playbackSession.StartAsync(playbackCancellation.Token);
            buttonPlay.Enabled = false;
            buttonStop.Enabled = true;
            labelStatus.Text = "Playing";
            _ = MonitorPlaybackAsync(playbackSession);
        }
        catch (Exception exception)
        {
            await DisposePlaybackAsync();
            buttonPlay.Enabled = true;
            buttonStop.Enabled = false;
            labelStatus.Text = exception.Message;
        }
    }

    private async Task MonitorPlaybackAsync(IAudioPlaybackSession session)
    {
        Exception? error = null;
        try
        {
            await session.WaitForCompletionAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // A user stop cancels the wait; the stop path resets the UI.
            return;
        }
        catch (Exception exception)
        {
            error = exception;
        }

        if (IsDisposed || !ReferenceEquals(playbackSession, session))
        {
            return;
        }
        try
        {
            BeginInvoke((Action)(() => CompletePlayback(session, error)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void CompletePlayback(IAudioPlaybackSession session, Exception? error)
    {
        if (!ReferenceEquals(playbackSession, session))
        {
            return;
        }
        await DisposePlaybackAsync();
        if (IsDisposed)
        {
            return;
        }
        buttonPlay.Enabled = true;
        buttonStop.Enabled = false;
        labelStatus.Text = error?.Message ?? "Ready";
    }

    private async Task StopPlaybackFromButtonAsync()
    {
        await StopPlaybackAsync();
        if (!IsDisposed)
        {
            buttonPlay.Enabled = true;
            buttonStop.Enabled = false;
            labelStatus.Text = "Ready";
        }
    }

    private async Task StopPlaybackAsync()
    {
        IAudioPlaybackSession? session = playbackSession;
        if (session != null)
        {
            try
            {
                await session.StopAsync();
            }
            catch
            {
                // Best-effort stop; teardown follows regardless.
            }
        }
        await DisposePlaybackAsync();
    }

    private async Task DisposePlaybackAsync()
    {
        playbackCancellation?.Cancel();
        IAudioPlaybackSession? session = playbackSession;
        playbackSession = null;
        if (session != null)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // A device teardown failure must not surface as a UI error here.
            }
        }
        playbackCancellation?.Dispose();
        playbackCancellation = null;
    }

    private void TearDownPlayback()
    {
        // The control is being disposed; cancel and release the session without
        // blocking the UI thread on the async teardown.
        playbackCancellation?.Cancel();
        IAudioPlaybackSession? session = playbackSession;
        playbackSession = null;
        if (session != null)
        {
            _ = session.DisposeAsync();
        }
    }

    private static AudioSessionRequest BuildRequest(SignalGeneratorPlaybackSettings settings) =>
        AudioSessionRequestBuilder.Build(
            settings.Backend,
            settings.SampleRate,
            settings.BitsPerSample,
            settings.PlaybackChannel,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: null,
            asioInputChannelOffset: 0,
            asioLoopbackInputChannelOffset: null,
            settings.AsioOutputChannelOffset,
            settings.WaveOutputDeviceNumber,
            inputDeviceNumber: -1,
            wasapiCaptureEndpointId: null,
            settings.WasapiRenderEndpointId,
            settings.AsioDriverName,
            settings.WasapiBufferMilliseconds,
            expectedCaptureSamples: 0);

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
        string backend = settings.Backend switch
        {
            AudioBackend.Asio => "ASIO",
            AudioBackend.WasapiShared => "WASAPI Shared",
            AudioBackend.WasapiExclusive => "WASAPI Exclusive",
            _ => "MME"
        };
        string output = settings.Backend switch
        {
            AudioBackend.Asio => GetAsioOutputName(settings),
            AudioBackend.WasapiShared or AudioBackend.WasapiExclusive =>
                settings.WasapiRenderEndpointName ?? "WASAPI endpoint",
            _ => GetWaveOutputName(settings.WaveOutputDeviceNumber)
        };
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
