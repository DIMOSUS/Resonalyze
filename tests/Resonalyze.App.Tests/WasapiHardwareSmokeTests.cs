using NAudio.Wave;

namespace Resonalyze.App.Tests;

public sealed class WasapiHardwareSmokeTests
{
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task SelectedEndpointsCanBeReopenedAfterEnumeration()
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
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
        await using var render = new WasapiPlaybackDevice(renderId, 100);
        Assert.True(capture.ChannelCount > 0);
        Assert.True(render.PlaybackFormat.SampleRate > 0);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task SelectedEndpointsSupportTwoCapturePlaybackRuns()
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        await using var captureDevice = new WasapiCaptureDevice(captureId, 100);
        await using var playbackDevice = new WasapiPlaybackDevice(renderId, 100);
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

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task FullSweepMeasurementSupportsEightRuns()
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        int sampleRate;
        using (var service = new WindowsAudioEndpointService())
        {
            AudioEndpointInfo capture = Assert.Single(
                service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId);
            AudioEndpointInfo render = Assert.Single(
                service.GetRenderEndpoints(), endpoint => endpoint.Id == renderId);
            Assert.Equal(capture.MixFormat.SampleRate, render.MixFormat.SampleRate);
            sampleRate = capture.MixFormat.SampleRate;
        }

        using var measurement = new ExpSweepMeasurement();
        measurement.Init(
            12,
            sampleRate,
            24,
            1.0,
            PlaybackChannel.Right,
            audioBackend: AudioBackend.WasapiShared,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            averageRunCount: 8,
            wasapiCaptureEndpointId: captureId,
            wasapiRenderEndpointId: renderId,
            wasapiBufferMilliseconds: 100);

        bool succeeded = await measurement.RunAsync();

        Assert.True(succeeded, measurement.LastError?.ToString());
        Assert.Equal(8, measurement.AverageRunCount);
        Assert.Equal(8, measurement.AcceptedAverageRunCount);
        Assert.NotNull(measurement.LastAudioSessionDiagnostics);
        Assert.True(measurement.LastAudioSessionDiagnostics.CapturePackets > 0);
        Assert.True(measurement.LastAudioSessionDiagnostics.RenderCallbacks > 0);
        Assert.True(measurement.LastAudioSessionDiagnostics.ActualBufferFrames > 0);
        // Some drivers mark the first packet after AudioClient.Start as a
        // discontinuity. Per-run baselines still reject any discontinuity
        // occurring during an accepted sweep.
        Assert.Equal(0, measurement.LastAudioSessionDiagnostics.TimestampErrors);
        Assert.Equal(0, measurement.LastAudioSessionDiagnostics.RenderUnderruns);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task ExclusiveSweepRunsOnAReportedDuplexFormat()
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        DuplexFormatSupport? supported = SampleRateCatalog.GetCandidateRates()
            .Select(rate => WasapiFormatSupport.CheckExclusive(
                captureId,
                renderId,
                rate,
                24,
                2,
                2))
            .FirstOrDefault(format => format.Supported);
        Assert.NotNull(supported);

        using var measurement = new ExpSweepMeasurement();
        measurement.Init(
            8,
            supported.SampleRate,
            supported.BitsPerSample,
            0.25,
            PlaybackChannel.Right,
            audioBackend: AudioBackend.WasapiExclusive,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            averageRunCount: 1,
            wasapiCaptureEndpointId: captureId,
            wasapiRenderEndpointId: renderId,
            wasapiBufferMilliseconds: 100);

        bool succeeded = await measurement.RunAsync();

        Assert.True(succeeded, measurement.LastError?.ToString());
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AbortedSweepReleasesEndpointsForImmediateReuse()
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        int sampleRate;
        using (var service = new WindowsAudioEndpointService())
        {
            sampleRate = Assert.Single(
                service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId)
                .MixFormat.SampleRate;
        }

        using (var measurement = new ExpSweepMeasurement())
        {
            measurement.Init(
                16,
                sampleRate,
                24,
                5.0,
                PlaybackChannel.Right,
                audioBackend: AudioBackend.WasapiShared,
                waveInputChannelOffset: 0,
                waveLoopbackInputChannelOffset: 1,
                wasapiCaptureEndpointId: captureId,
                wasapiRenderEndpointId: renderId,
                wasapiBufferMilliseconds: 100);

            Task<bool> running = measurement.RunAsync();
            await Task.Delay(500);
            await measurement.AbortAsync();

            Assert.False(await running);
            Assert.Null(measurement.LastError);
            Assert.False(measurement.InProgress);
        }

        await using var capture = new WasapiCaptureDevice(captureId, 100);
        await using var render = new WasapiPlaybackDevice(renderId, 100);
        Assert.True(capture.ChannelCount > 0);
        Assert.True(render.PlaybackFormat.SampleRate > 0);
    }

    [Theory]
    [InlineData(AudioBackend.WasapiShared)]
    [InlineData(AudioBackend.WasapiExclusive)]
    [Trait("Category", "Hardware")]
    public async Task LiveNoiseProducesSpectrum(AudioBackend backend)
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        int sampleRate;
        if (backend == AudioBackend.WasapiExclusive)
        {
            DuplexFormatSupport? supported = SampleRateCatalog.GetCandidateRates()
                .Select(rate => WasapiFormatSupport.CheckExclusive(
                    captureId,
                    renderId,
                    rate,
                    24,
                    2,
                    2))
                .FirstOrDefault(format => format.Supported);
            Assert.NotNull(supported);
            sampleRate = supported.SampleRate;
        }
        else
        {
            using var service = new WindowsAudioEndpointService();
            AudioEndpointInfo capture = Assert.Single(
                service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId);
            sampleRate = capture.MixFormat.SampleRate;
        }

        using var measurement = new NoiseMeasurement();
        measurement.Init(
            sampleRate,
            24,
            2.0,
            PlaybackChannel.Right,
            sequenceLength: 2048,
            audioBackend: backend,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            wasapiCaptureEndpointId: captureId,
            wasapiRenderEndpointId: renderId,
            wasapiBufferMilliseconds: 100);

        Task<bool> running = measurement.RunAsync();
        await Task.Delay(1500);
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.NotNull(measurement.GetAccumulatedSpectrumSnapshot());
    }

    [Theory]
    [InlineData(AudioBackend.WasapiShared)]
    [InlineData(AudioBackend.WasapiExclusive)]
    [Trait("Category", "Hardware")]
    public async Task AudioWarmupCompletes(AudioBackend backend)
    {
        string? captureId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? renderId = Environment.GetEnvironmentVariable(
            "RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        if (string.IsNullOrWhiteSpace(captureId) || string.IsNullOrWhiteSpace(renderId))
        {
            return;
        }

        int sampleRate;
        if (backend == AudioBackend.WasapiExclusive)
        {
            DuplexFormatSupport? supported = SampleRateCatalog.GetCandidateRates()
                .Select(rate => WasapiFormatSupport.CheckExclusive(
                    captureId,
                    renderId,
                    rate,
                    24,
                    2,
                    2))
                .FirstOrDefault(format => format.Supported);
            Assert.NotNull(supported);
            sampleRate = supported.SampleRate;
        }
        else
        {
            using var service = new WindowsAudioEndpointService();
            sampleRate = Assert.Single(
                service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId)
                .MixFormat.SampleRate;
        }

        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            AudioBackend = backend,
            SampleRate = sampleRate,
            Bits = 24,
            PlaybackChannel = PlaybackChannel.Right,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1,
            WasapiCaptureEndpointId = captureId,
            WasapiRenderEndpointId = renderId,
            WasapiBufferMilliseconds = 100
        };

        await AudioDeviceWarmup.WarmUpAsync(settings);
    }
}
