using NAudio.Wave;

namespace Resonalyze;

public interface IAudioPlaybackDevice : IAsyncDisposable
{
    WaveFormat PlaybackFormat { get; }

    Task StartAsync(IWaveProvider source, CancellationToken cancellationToken);
    Task WaitForPlaybackEndAsync(CancellationToken cancellationToken);
    Task StopAsync();
}
