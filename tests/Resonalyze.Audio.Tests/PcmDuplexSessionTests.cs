using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

/// <summary>
/// Verifies the PCM duplex-session core (shared by the MME and WASAPI backends)
/// against fake devices: role routing, session reuse across runs, WASAPI-style
/// diagnostics and anomaly reporting — all without hardware.
/// </summary>
public sealed class PcmDuplexSessionTests
{
    private const int SweepSamples = 64;

    private static AudioPlaybackSignal Signal() =>
        new(new float[SweepSamples], 48_000, 16, PlaybackChannel.Mono, Loop: false);

    [Fact]
    public async Task PlayAndCaptureRoutesRolesAndReportsDiagnostics()
    {
        var capture = new PushCaptureDevice(new WaveFormat(48_000, 16, 2));
        var playback = new PushPlaybackDevice(capture, pushFrames: SweepSamples);
        await using var session = new PcmDuplexSession(
            capture, playback, Signal(), new AudioCaptureRouting(0, 1),
            expectedCaptureSamples: 4096, backendName: "TestBackend", requestedBufferMilliseconds: 100);

        AudioCaptureResult result = await session.PlayAndCaptureAsync(
            captureTailSamples: 0, CancellationToken.None);

        Assert.Equal(2, result.Channels.Length);
        Assert.Equal(0, result.MicrophoneChannel);
        Assert.Equal(1, result.LoopbackChannel);
        Assert.True(result.StereoSeparationExpected);
        Assert.Equal(AudioCaptureAnomalies.None, result.Anomalies);
        Assert.NotNull(result.Diagnostics);
        Assert.Equal("TestBackend", result.Diagnostics!.Backend);
        Assert.Equal(48_000, result.Diagnostics.CaptureFormat.SampleRate);
        Assert.Equal(100, result.Diagnostics.RequestedBufferMilliseconds);
    }

    [Fact]
    public async Task AveragingReusesDevicesAcrossRuns()
    {
        var capture = new PushCaptureDevice(new WaveFormat(48_000, 16, 2));
        var playback = new PushPlaybackDevice(capture, pushFrames: SweepSamples);
        await using var session = new PcmDuplexSession(
            capture, playback, Signal(), new AudioCaptureRouting(0, 1),
            expectedCaptureSamples: 4096, backendName: "TestBackend", requestedBufferMilliseconds: 100);

        await session.PlayAndCaptureAsync(0, CancellationToken.None);
        await session.PlayAndCaptureAsync(0, CancellationToken.None);

        Assert.Equal(1, capture.StartCount);
        Assert.Equal(2, playback.StartCount);
        // The same bound signal is replayed each run — the render device's
        // single-source guard must never trip.
        Assert.False(playback.SawDifferentSource);
    }

    [Fact]
    public async Task DiscontinuityDuringRunIsReportedAsAnomaly()
    {
        var capture = new PushCaptureDevice(new WaveFormat(48_000, 16, 2));
        var playback = new PushPlaybackDevice(capture, pushFrames: SweepSamples)
        {
            BumpDiscontinuityOnStart = true
        };
        await using var session = new PcmDuplexSession(
            capture, playback, Signal(), new AudioCaptureRouting(0, 1),
            expectedCaptureSamples: 4096, backendName: "TestBackend", requestedBufferMilliseconds: 100);

        AudioCaptureResult result = await session.PlayAndCaptureAsync(
            0, CancellationToken.None);

        Assert.True(result.Anomalies.HasFlag(AudioCaptureAnomalies.CaptureDiscontinuity));
    }

    [Fact]
    public async Task DeviceStopDuringRunSurfacesAsExceptionInsteadOfHanging()
    {
        var capture = new PushCaptureDevice(new WaveFormat(48_000, 16, 2));
        // Push fewer frames than the run needs, then stop the capture device — the
        // sweep sample wait must fault, not block until an Abort.
        var playback = new PushPlaybackDevice(capture, pushFrames: 10)
        {
            StopCaptureOnStart = new IOException("Capture device removed mid-sweep.")
        };
        await using var session = new PcmDuplexSession(
            capture, playback, Signal(), new AudioCaptureRouting(0, 1),
            expectedCaptureSamples: 4096, backendName: "TestBackend", requestedBufferMilliseconds: 100);

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            session.PlayAndCaptureAsync(captureTailSamples: 0, CancellationToken.None));
        Assert.Equal("Capture device removed mid-sweep.", exception.Message);
    }

    // A capture device that emits one startup frame (to release the first-buffer
    // wait) and then, when the playback device "plays", the frames that satisfy
    // the sample wait.
    private sealed class PushCaptureDevice : IAudioCaptureDevice, ICaptureDiagnosticsSource
    {
        public PushCaptureDevice(WaveFormat format) => CaptureFormat = format;

        public event Action<AudioCapturePacket>? DataAvailable;
        public event EventHandler<AudioDeviceStoppedEventArgs>? Stopped;

        public void StopWithError(Exception exception) =>
            Stopped?.Invoke(this, new AudioDeviceStoppedEventArgs(exception));

        public WaveFormat CaptureFormat { get; }
        public int ChannelCount => CaptureFormat.Channels;
        public int MaximumPacketBytes => CaptureFormat.AverageBytesPerSecond / 10;
        public int StartCount { get; private set; }

        public string EndpointId => "capture-endpoint";
        public long CapturePackets { get; private set; }
        public long Discontinuities { get; set; }
        public long SilentPackets => 0;
        public long TimestampErrors => 0;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            Push(1);
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Push(int frames)
        {
            CapturePackets++;
            var bytes = new byte[frames * CaptureFormat.BlockAlign];
            Array.Fill(bytes, (byte)0x10);
            DataAvailable?.Invoke(new AudioCapturePacket(
                bytes,
                bytes.Length,
                CaptureFormat));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PushPlaybackDevice : IAudioPlaybackDevice, IRenderDiagnosticsSource
    {
        private readonly PushCaptureDevice capture;
        private readonly int pushFrames;
        private IWaveProvider? initializedSource;

        public PushPlaybackDevice(PushCaptureDevice capture, int pushFrames)
        {
            this.capture = capture;
            this.pushFrames = pushFrames;
        }

        public WaveFormat PlaybackFormat { get; } = new(48_000, 16, 1);
        public int StartCount { get; private set; }
        public bool SawDifferentSource { get; private set; }
        public bool BumpDiscontinuityOnStart { get; init; }
        public Exception? StopCaptureOnStart { get; init; }

        public string EndpointId => "render-endpoint";
        public long RenderCallbacks => 1;
        public long RenderUnderruns => 0;
        public int ActualBufferFrames => 480;

        public Task StartAsync(IWaveProvider source, CancellationToken cancellationToken)
        {
            // Mirror the real render devices: a session must replay the one
            // source it was opened with, never a different instance.
            if (initializedSource != null && !ReferenceEquals(initializedSource, source))
            {
                SawDifferentSource = true;
            }
            initializedSource = source;
            StartCount++;
            if (BumpDiscontinuityOnStart)
            {
                capture.Discontinuities++;
            }
            capture.Push(pushFrames);
            // Simulate the capture device dying while playback is running.
            if (StopCaptureOnStart != null)
            {
                capture.StopWithError(StopCaptureOnStart);
            }
            return Task.CompletedTask;
        }

        public Task WaitForPlaybackEndAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
