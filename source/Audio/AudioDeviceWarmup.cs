using NAudio.Wave;

namespace Resonalyze;

internal static class AudioDeviceWarmup
{
    private static readonly TimeSpan WarmupPlaybackDuration = TimeSpan.FromMilliseconds(200);

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
        using var silence = FloatArrayWaveStream.FromMonoSamples(
            new float[Math.Max(256, settings.SampleRate / 20)],
            settings.SampleRate,
            settings.PlaybackChannel);

        await session.StartAsync(
            silence,
            settings.SampleRate,
            autoStop: false,
            linkedCancellation.Token);
        await session.StopAsync();
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
        if (settings.WaveLoopbackInputChannelOffset.HasValue)
        {
            maxChannelOffset = Math.Max(
                maxChannelOffset,
                settings.WaveLoopbackInputChannelOffset.Value);
        }

        return maxChannelOffset + 1;
    }

    private static int GetAsioCaptureFirstInputOffset(
        MeasurementSettingsFile.SweepMeasurementSettings settings)
    {
        return settings.AsioLoopbackInputChannelOffset.HasValue
            ? Math.Min(
                settings.AsioInputChannelOffset,
                settings.AsioLoopbackInputChannelOffset.Value)
            : settings.AsioInputChannelOffset;
    }

    private static int GetRequiredAsioInputChannelCount(
        MeasurementSettingsFile.SweepMeasurementSettings settings,
        int firstInputOffset)
    {
        int lastInputOffset = settings.AsioLoopbackInputChannelOffset.HasValue
            ? Math.Max(
                settings.AsioInputChannelOffset,
                settings.AsioLoopbackInputChannelOffset.Value)
            : settings.AsioInputChannelOffset;
        return lastInputOffset - firstInputOffset + 1;
    }
}
