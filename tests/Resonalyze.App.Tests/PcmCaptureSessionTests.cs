using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class PcmCaptureSessionTests
{
    [Fact]
    public async Task StartWaitsForFirstPacketAndSampleWaitCompletes()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);

        Task start = session.StartAsync(CancellationToken.None);
        Assert.False(start.IsCompleted);
        device.Push([0, 0, 0xff, 0x7f]);
        await start;

        await session.WaitForSamplesAsync(2, CancellationToken.None);
        Assert.Equal(2, session.ReadSamples);
    }

    [Fact]
    public async Task UnexpectedStopFaultsPendingWaits()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0, 0]);
        await start;
        Task wait = session.WaitForSamplesAsync(100, CancellationToken.None);

        device.StopWithError(new IOException("Device unplugged."));

        IOException exception = await Assert.ThrowsAsync<IOException>(() => wait);
        Assert.Equal("Device unplugged.", exception.Message);
    }

    [Fact]
    public async Task ResetRemovesSamplesFromPreviousRun()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0xff, 0x7f]);
        await start;
        Assert.Single(session.GetSamplesSnapshot()[0]);

        session.Reset();

        Assert.Empty(session.GetSamplesSnapshot()[0]);
    }

    private sealed class FakeCaptureDevice : IAudioCaptureDevice
    {
        public FakeCaptureDevice(WaveFormat format)
        {
            CaptureFormat = format;
        }

        public event EventHandler<AudioCaptureDataEventArgs>? DataAvailable;
        public event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

        public WaveFormat CaptureFormat { get; }
        public int ChannelCount => CaptureFormat.Channels;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Push(byte[] bytes)
        {
            DataAvailable?.Invoke(
                this,
                new AudioCaptureDataEventArgs
                {
                    Buffer = bytes,
                    BytesRecorded = bytes.Length,
                    Format = CaptureFormat
                });
        }

        public void StopWithError(Exception exception) =>
            Stopped?.Invoke(this, new AudioDeviceStoppedEventArgs(exception));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
