using NAudio.Wave;

namespace Resonalyze;

internal static class AudioDeviceWarmup
{
    private const double AsioWarmupAmplitude = 0.00025;

    private static readonly TimeSpan WarmupPlaybackDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan AsioPrerollDuration = TimeSpan.FromMilliseconds(2200);

    public static async Task WarmUpAsync(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AudioBackend == AudioBackend.Asio)
        {
            await WarmUpAsioAsync(settings, cancellationToken);
            return;
        }

        await WarmUpWaveAsync(settings, cancellationToken);
    }

    private static async Task WarmUpWaveAsync(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        CancellationToken cancellationToken)
    {
        using var recorder = new SoundRecorder();
        recorder.Init(
            settings.SampleRate,
            settings.Bits,
            GetRequiredWaveInputChannelCount(settings),
            settings.InputDeviceNumber);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linkedCancellation.CancelAfter(TimeSpan.FromSeconds(5));

        await recorder.StartRecordingAsync(linkedCancellation.Token);
        await recorder.StopRecordingAsync();

        int channelCount = settings.PlaybackChannel == PlaybackChannel.Stereo ? 2 : 1;
        var silence = new SilenceProvider(new WaveFormat(
            settings.SampleRate,
            settings.Bits,
            channelCount));
        using var player = new WaveOutEvent
        {
            DeviceNumber = settings.OutputDeviceNumber
        };
        player.Init(silence);
        await PlayBrieflyAsync(player, linkedCancellation.Token);
    }

    private static async Task WarmUpAsioAsync(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        CancellationToken cancellationToken)
    {
        int firstInputOffset = GetAsioCaptureFirstInputOffset(settings);
        int inputChannelCount = GetRequiredAsioInputChannelCount(settings, firstInputOffset);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        linkedCancellation.CancelAfter(TimeSpan.FromSeconds(5));

        using var session = new AsioFullDuplexSession(
            settings.AsioDriverName ?? string.Empty,
            firstInputOffset,
            settings.AsioOutputChannelOffset,
            inputChannelCount);
        using var warmupSignal = FloatArrayWaveStream.FromMonoSamples(
            CreateAsioWarmupSignal(settings.SampleRate),
            settings.SampleRate,
            settings.PlaybackChannel);
        var loopingWarmup = new LoopingWaveProvider(warmupSignal);

        await session.StartAsync(
            loopingWarmup,
            settings.SampleRate,
            autoStop: false,
            linkedCancellation.Token);
        int prerollSamples = Math.Max(
            1,
            (int)Math.Round(settings.SampleRate * AsioPrerollDuration.TotalSeconds));
        await session.WaitForSamplesAsync(prerollSamples, linkedCancellation.Token);
        await session.StopAsync();
    }

    private static float[] CreateAsioWarmupSignal(int sampleRate)
    {
        int sampleCount = Math.Max(256, sampleRate / 2);
        var samples = new float[sampleCount];
        var random = new Random(42);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2.0 - 1.0) * AsioWarmupAmplitude);
        }

        return samples;
    }

    private static async Task PlayBrieflyAsync(
        WaveOutEvent player,
        CancellationToken cancellationToken)
    {
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void PlaybackStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception != null)
            {
                stopped.TrySetException(args.Exception);
            }
            else
            {
                stopped.TrySetResult(true);
            }
        }

        player.PlaybackStopped += PlaybackStopped;
        try
        {
            player.Play();
            await Task.Delay(WarmupPlaybackDuration, cancellationToken);
            player.Stop();
            await stopped.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            player.PlaybackStopped -= PlaybackStopped;
        }
    }

    private static int GetRequiredWaveInputChannelCount(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        int maxChannelOffset = settings.WaveInputChannelOffset;
        int? loopbackOffset = GetEffectiveWaveLoopbackInputChannelOffset(settings);
        if (loopbackOffset.HasValue)
        {
            maxChannelOffset = Math.Max(
                maxChannelOffset,
                loopbackOffset.Value);
        }

        return maxChannelOffset + 1;
    }

    private static int GetAsioCaptureFirstInputOffset(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        int? loopbackOffset = GetEffectiveAsioLoopbackInputChannelOffset(settings);
        return loopbackOffset.HasValue
            ? Math.Min(
                settings.AsioInputChannelOffset,
                loopbackOffset.Value)
            : settings.AsioInputChannelOffset;
    }

    private static int GetRequiredAsioInputChannelCount(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        int firstInputOffset)
    {
        int? loopbackOffset = GetEffectiveAsioLoopbackInputChannelOffset(settings);
        int lastInputOffset = loopbackOffset.HasValue
            ? Math.Max(
                settings.AsioInputChannelOffset,
                loopbackOffset.Value)
            : settings.AsioInputChannelOffset;
        return lastInputOffset - firstInputOffset + 1;
    }

    private static int? GetEffectiveWaveLoopbackInputChannelOffset(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        if (settings.WaveLoopbackInputChannelOffset == settings.WaveInputChannelOffset)
        {
            return null;
        }

        return settings.WaveLoopbackInputChannelOffset;
    }

    private static int? GetEffectiveAsioLoopbackInputChannelOffset(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        if (settings.AsioLoopbackInputChannelOffset == settings.AsioInputChannelOffset)
        {
            return null;
        }

        return settings.AsioLoopbackInputChannelOffset;
    }
}
