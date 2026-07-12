using NAudio.Wave;

namespace Resonalyze;

public interface IAudioCaptureDevice : IAsyncDisposable
{
    event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
    event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

    WaveFormat CaptureFormat { get; }
    int ChannelCount { get; }

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}
