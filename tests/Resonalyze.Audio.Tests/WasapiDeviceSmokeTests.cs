using NAudio.Wave;

namespace Resonalyze.Audio.Tests;

/// <summary>
/// Hardware smoke tests for the low-level WASAPI devices. Skipped unless the
/// endpoint environment variables are set, so a normal CI run does not report
/// them as executed.
/// </summary>
public sealed class WasapiDeviceSmokeTests
{
    private static (string Capture, string Render)? Endpoints()
    {
        string? capture = Environment.GetEnvironmentVariable("RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? render = Environment.GetEnvironmentVariable("RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        return string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(render)
            ? null
            : (capture, render);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task SelectedEndpointsCanBeReopenedAfterEnumeration()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        // Enumerate first to reproduce the UI path. Explicitly releasing the
        // returned MMDevice wrappers used to disconnect the cached COM RCW and
        // make the following WasapiOut construction fail with E_NOINTERFACE.
        using (var service = new WindowsAudioEndpointService())
        {
            Assert.Contains(service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId);
            Assert.Contains(service.GetRenderEndpoints(), endpoint => endpoint.Id == renderId);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await using var capture = new WasapiCaptureDevice(captureId, 100);
        await using var render = new WasapiPlaybackDevice(renderId!, 100);
        Assert.True(capture.ChannelCount > 0);
        Assert.True(render.PlaybackFormat.SampleRate > 0);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task FailedDeviceConstructionReleasesComResources()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        Assert.Throws<ArgumentException>(() => new WasapiCaptureDevice(renderId!));
        Assert.Throws<ArgumentException>(() => new WasapiPlaybackDevice(captureId));

        await using var capture = new WasapiCaptureDevice(captureId, 100);
        await using var render = new WasapiPlaybackDevice(renderId!, 100);
        Assert.True(capture.ChannelCount > 0);
        Assert.True(render.PlaybackFormat.SampleRate > 0);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task SelectedEndpointsSupportTwoCapturePlaybackRuns()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        await using var captureDevice = new WasapiCaptureDevice(captureId, 100);
        await using var playbackDevice = new WasapiPlaybackDevice(renderId!, 100);
        await using var captureSession = new PcmCaptureSession(captureDevice);
        long? lastDevicePosition = null;
        long? lastQpcPosition = null;
        captureDevice.DataAvailable += (_, packet) =>
        {
            lastDevicePosition = packet.DevicePositionFrames;
            lastQpcPosition = packet.QpcPosition;
        };
        int sampleRate = playbackDevice.PlaybackFormat.SampleRate;
        var format = new WaveFormat(sampleRate, 16, 2);
        byte[] silence = new byte[sampleRate / 20 * format.BlockAlign];
        using var memory = new MemoryStream(silence, writable: false);
        using var stream = new RawSourceWaveStream(memory, format);

        await captureSession.StartAsync(CancellationToken.None);
        for (int run = 0; run < 2; run++)
        {
            if (run > 0)
            {
                captureSession.Reset();
            }
            stream.Position = 0;
            await playbackDevice.StartAsync(stream, CancellationToken.None);
            await playbackDevice.WaitForPlaybackEndAsync(CancellationToken.None);
            await captureSession.WaitForSamplesAsync(
                Math.Max(1, captureDevice.CaptureFormat.SampleRate / 20),
                CancellationToken.None);
        }
        await captureSession.StopAsync();
        Assert.True(captureDevice.CapturePackets > 0);
        Assert.NotNull(lastDevicePosition);
        Assert.NotNull(lastQpcPosition);
    }
}
