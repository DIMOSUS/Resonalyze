using NAudio.Wave;

namespace Resonalyze.Audio;

/// <summary>
/// Playback-only ASIO session for the signal generator. Wraps a single
/// <see cref="AsioOut"/> (AutoStop) so a finite signal ends by itself and stop
/// releases the driver immediately.
/// </summary>
internal sealed class AsioPlaybackSession : IAudioPlaybackSession
{
    private readonly string driverName;
    private readonly int outputChannelOffset;
    private readonly AudioPlaybackSignal signal;
    private AsioOut? driver;
    private FloatArrayWaveStream? stream;
    private TaskCompletionSource<bool>? completion;
    private bool disposed;

    public AsioPlaybackSession(AudioSessionRequest request, AudioPlaybackSignal signal)
    {
        driverName = request.AsioDriverName ?? string.Empty;
        outputChannelOffset = request.AsioOutputChannelOffset;
        this.signal = signal;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(driverName))
        {
            throw new InvalidOperationException("ASIO driver is not selected.");
        }

        var createdDriver = new AsioOut(driverName)
        {
            AutoStop = true,
            ChannelOffset = outputChannelOffset
        };
        try
        {
            if (!createdDriver.IsSampleRateSupported(signal.SampleRate))
            {
                throw new InvalidOperationException(
                    $"ASIO driver '{driverName}' does not support {signal.SampleRate} Hz.");
            }
            if (outputChannelOffset < 0 ||
                outputChannelOffset + 2 > createdDriver.DriverOutputChannelCount)
            {
                throw new InvalidOperationException(
                    $"ASIO output channel pair starting at {outputChannelOffset + 1} is not available.");
            }

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stream = AudioPlaybackStreamFactory.CreateFloat(signal);
            createdDriver.PlaybackStopped += HandlePlaybackStopped;
            createdDriver.Init(stream);
            driver = createdDriver;
            createdDriver.Play();
        }
        catch
        {
            createdDriver.PlaybackStopped -= HandlePlaybackStopped;
            createdDriver.Dispose();
            stream?.Dispose();
            stream = null;
            throw;
        }

        return Task.CompletedTask;
    }

    public Task WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        Task task = completion?.Task ??
            throw new InvalidOperationException("Playback has not been started.");
        return task.WaitAsync(cancellationToken);
    }

    public Task StopAsync()
    {
        if (!disposed && driver != null)
        {
            driver.Stop();
        }
        return Task.CompletedTask;
    }

    private void HandlePlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception != null)
        {
            completion?.TrySetException(args.Exception);
        }
        else
        {
            completion?.TrySetResult(true);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }
        disposed = true;
        if (driver != null)
        {
            driver.PlaybackStopped -= HandlePlaybackStopped;
            driver.Stop();
            driver.Dispose();
            driver = null;
        }
        stream?.Dispose();
        completion?.TrySetResult(false);
        return ValueTask.CompletedTask;
    }
}
