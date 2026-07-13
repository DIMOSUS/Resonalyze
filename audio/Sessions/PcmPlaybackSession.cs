namespace Resonalyze.Audio;

/// <summary>
/// Playback-only session for the PCM backends (Wave/MME and WASAPI): plays a
/// prepared finite (or looping) signal to completion or until stopped.
/// </summary>
internal sealed class PcmPlaybackSession : IAudioPlaybackSession
{
    private readonly IAudioPlaybackDevice playback;
    private readonly AudioPlaybackSignal signal;
    private PcmPlaybackStream? playbackStream;
    private bool disposed;

    public PcmPlaybackSession(IAudioPlaybackDevice playback, AudioPlaybackSignal signal)
    {
        this.playback = playback;
        this.signal = signal;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        playbackStream = AudioPlaybackStreamFactory.CreatePcm(signal);
        playbackStream.Rewind();
        if (signal.Loop)
        {
            await playback.StartAsync(
                new LoopingWaveProvider(playbackStream.Stream),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await playback.StartAsync(playbackStream.Stream, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task WaitForCompletionAsync(CancellationToken cancellationToken) =>
        playback.WaitForPlaybackEndAsync(cancellationToken);

    public Task StopAsync() => playback.StopAsync();

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            await playback.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            playbackStream?.Dispose();
        }
    }
}
