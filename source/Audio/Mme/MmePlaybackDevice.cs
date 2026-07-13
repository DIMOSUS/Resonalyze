using NAudio.Wave;

namespace Resonalyze;

public sealed class MmePlaybackDevice : IAudioPlaybackDevice
{
    private readonly WaveOutEvent output;
    private TaskCompletionSource<bool>? playbackEnded;
    private IWaveProvider? initializedSource;
    private bool disposed;

    public MmePlaybackDevice(int deviceNumber)
    {
        output = new WaveOutEvent
        {
            DeviceNumber = deviceNumber
        };
        output.PlaybackStopped += HandlePlaybackStopped;
    }

    public WaveFormat PlaybackFormat { get; private set; } = null!;

    public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (playbackEnded is { Task.IsCompleted: false })
        {
            throw new InvalidOperationException("Playback is already running.");
        }

        if (initializedSource != null && !ReferenceEquals(initializedSource, source))
        {
            throw new InvalidOperationException(
                "This playback session is already initialized with another source.");
        }
        PlaybackFormat = source.WaveFormat;
        playbackEnded = SampleWaiterRegistry.NewSignal();
        if (initializedSource == null)
        {
            output.Init(source);
            initializedSource = source;
        }
        output.Play();
        return Task.CompletedTask;
    }

    public async Task WaitForPlaybackEndAsync(CancellationToken cancellationToken)
    {
        Task task = playbackEnded?.Task ??
            throw new InvalidOperationException("Playback has not been started.");
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        if (!disposed && playbackEnded is { Task.IsCompleted: false })
        {
            output.Stop();
        }
        return Task.CompletedTask;
    }

    private void HandlePlaybackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception == null)
        {
            playbackEnded?.TrySetResult(true);
        }
        else
        {
            playbackEnded?.TrySetException(args.Exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        await StopAsync().ConfigureAwait(false);
        disposed = true;
        output.PlaybackStopped -= HandlePlaybackStopped;
        output.Dispose();
    }
}
