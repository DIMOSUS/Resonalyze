using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

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
    public async Task UnexpectedStopIsObservableWithoutSampleWaiter()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0, 0]);
        await start;
        Task stopped = session.WaitForStopAsync(CancellationToken.None);

        device.StopWithError(new IOException("Device unplugged."));

        IOException exception = await Assert.ThrowsAsync<IOException>(() => stopped);
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

    [Fact]
    public async Task Reset_WhileOldPacketIsDecoding_DoesNotAppendItToNewRun()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        var decoder = new BlockingDecoder();
        await using var session = new PcmCaptureSession(device, decoder: decoder);

        Task start = session.StartAsync(CancellationToken.None);
        device.Push([1, 0]);
        Assert.True(decoder.FirstDecodeStarted.Wait(TimeSpan.FromSeconds(2)));

        session.Reset();
        decoder.ReleaseFirstDecode.Set();
        device.Push([2, 0]);
        await start.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([2f], session.GetSamplesSnapshot()[0]);
    }

    [Fact]
    public async Task PausedCaptureDropsAppendsButKeepsMeteringAndResumesOnReset()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        int levelEvents = 0;
        session.LevelsAvailable += _ => levelEvents++;
        Task start = session.StartAsync(CancellationToken.None);
        device.Push(new byte[1600 * 2]); // one 30 Hz meter interval at 48 kHz
        await start;
        Assert.Equal(1600, session.ReadSamples);
        int levelsAfterStart = levelEvents;

        // A long confirmation pause: audio keeps arriving but must not accumulate.
        session.Pause();
        device.Push(new byte[1600 * 2]);
        await WaitUntilAsync(() => Volatile.Read(ref levelEvents) > levelsAfterStart);
        Assert.Equal(1600, session.ReadSamples);
        Assert.True(levelEvents > levelsAfterStart); // meter stayed live

        // The next run resumes and starts from a clean buffer.
        session.Reset();
        Assert.Equal(0, session.ReadSamples);
        device.Push([0x06, 0x00]);
        await session.WaitForSamplesAsync(1, CancellationToken.None);
        Assert.Equal(1, session.ReadSamples);
    }

    [Fact]
    public async Task StopBeforeSampleWaiterFaultsTheLaterWaitImmediately()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0, 0]);
        await start;

        // The device dies while playback is still running — before the sweep's
        // sample waiter is ever created.
        device.StopWithError(new IOException("Device unplugged."));

        // Registering the waiter now must fault at once, not hang until Abort.
        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            session.WaitForSamplesAsync(1000, CancellationToken.None));
        Assert.Equal("Device unplugged.", exception.Message);
    }

    [Fact]
    public async Task TerminalFailureSurvivesResetBetweenRuns()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0, 0]);
        await start;
        device.StopWithError(new IOException("Device unplugged."));

        // A Reset for the next averaged run must not forget the failure.
        session.Reset();

        await Assert.ThrowsAsync<IOException>(() =>
            session.WaitForSamplesAsync(1000, CancellationToken.None));
    }

    [Fact]
    public async Task RealRestartClearsTerminalFailure()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        Task start = session.StartAsync(CancellationToken.None);
        device.Push([0, 0]);
        await start;
        device.StopWithError(new IOException("Device unplugged."));

        // A genuine restart (not a between-runs Reset) resumes normally.
        Task restart = session.StartAsync(CancellationToken.None);
        device.Push([0x01, 0x00, 0x02, 0x00]);
        await restart;

        await session.WaitForSamplesAsync(2, CancellationToken.None);
        Assert.Equal(2, session.ReadSamples);
    }

    [Fact]
    public async Task PacketFlagsAreCountedAndDiscontinuityIsPublished()
    {
        var device = new FakeCaptureDevice(new WaveFormat(48000, 16, 1));
        await using var session = new PcmCaptureSession(device);
        int notifications = 0;
        session.CaptureDiscontinuity += () => notifications++;
        Task start = session.StartAsync(CancellationToken.None);

        device.Push([0, 0], discontinuity: true, silent: true, timestampError: true);
        await start;

        Assert.Equal(1, session.DiscontinuityCount);
        Assert.Equal(1, session.SilentPacketCount);
        Assert.Equal(1, session.TimestampErrorCount);
        Assert.Equal(1, notifications);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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
        public int MaximumPacketBytes => CaptureFormat.AverageBytesPerSecond / 10;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Push(
            byte[] bytes,
            bool discontinuity = false,
            bool silent = false,
            bool timestampError = false)
        {
            DataAvailable?.Invoke(
                this,
                new AudioCaptureDataEventArgs
                {
                    Buffer = bytes,
                    BytesRecorded = bytes.Length,
                    Format = CaptureFormat,
                    Discontinuity = discontinuity,
                    Silent = silent,
                    TimestampError = timestampError
                });
        }

        public void StopWithError(Exception exception) =>
            Stopped?.Invoke(this, new AudioDeviceStoppedEventArgs(exception));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingDecoder : IInterleavedSampleDecoder
    {
        private int decodeCount;

        public ManualResetEventSlim FirstDecodeStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstDecode { get; } = new();
        public int ChannelCount => 1;

        public int Decode(ReadOnlySpan<byte> source, float[][] destination)
        {
            if (Interlocked.Increment(ref decodeCount) == 1)
            {
                FirstDecodeStarted.Set();
                ReleaseFirstDecode.Wait(TimeSpan.FromSeconds(2));
            }

            destination[0][0] = source[0];
            return 1;
        }
    }
}
