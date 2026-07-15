using NAudio.Wave;

namespace Resonalyze.Audio;

internal interface IAudioCaptureDevice : IAsyncDisposable
{
    event Action<AudioCapturePacket>? DataAvailable;
    event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

    WaveFormat CaptureFormat { get; }
    int ChannelCount { get; }
    int MaximumPacketBytes { get; }

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}
