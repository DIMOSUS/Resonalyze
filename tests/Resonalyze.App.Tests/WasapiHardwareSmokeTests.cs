using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// End-to-end hardware smoke tests for the measurement stack over real WASAPI
/// endpoints, driven entirely through the public audio abstraction (no direct
/// device construction). Skipped unless the endpoint environment variables are
/// set, so a normal CI run does not report them as executed.
/// </summary>
public sealed class WasapiHardwareSmokeTests
{
    private static readonly IAudioSessionFactory Factory =
        new AudioSessionFactory(AudioBackendRegistry.CreateDefault());

    private static (string Capture, string Render)? Endpoints()
    {
        string? capture = Environment.GetEnvironmentVariable("RESONALYZE_WASAPI_CAPTURE_ENDPOINT_ID");
        string? render = Environment.GetEnvironmentVariable("RESONALYZE_WASAPI_RENDER_ENDPOINT_ID");
        return string.IsNullOrWhiteSpace(capture) || string.IsNullOrWhiteSpace(render)
            ? null
            : (capture, render);
    }

    private static int SharedMixRate(string captureId)
    {
        using var service = new WindowsAudioEndpointService();
        return Assert.Single(service.GetCaptureEndpoints(), endpoint => endpoint.Id == captureId)
            .PreferredFormat.SampleRate;
    }

    private static int? FirstExclusiveRate(string captureId, string renderId) =>
        SampleRateCatalog.GetCandidateRates()
            .Select(rate => WasapiFormatSupport.CheckExclusive(captureId, renderId, rate, 24, 2, 2))
            .FirstOrDefault(format => format.Supported)?.SampleRate;

    private static AudioSessionRequest DuplexProbe(string captureId, string renderId, int sampleRate) =>
        AudioSessionRequestBuilder.Build(
            AudioBackend.WasapiShared, sampleRate, 24, PlaybackChannel.Right,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            asioInputChannelOffset: 0, asioLoopbackInputChannelOffset: null, asioOutputChannelOffset: 0,
            outputDeviceNumber: -1, inputDeviceNumber: -1,
            wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId, asioDriverName: null,
            bufferMilliseconds: 100, expectedCaptureSamples: sampleRate);

    // Proves the endpoints were released: opening a fresh duplex session succeeds.
    private static async Task AssertEndpointsReusableAsync(string captureId, string renderId, int sampleRate)
    {
        await using IAudioDuplexSession session =
            await Factory.OpenDuplexAsync(DuplexProbe(captureId, renderId, sampleRate), CancellationToken.None);
        Assert.NotNull(session);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task FailedDuplexValidationSurfacesAndReleasesEndpoints()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int mixRate = SharedMixRate(captureId);
        int wrongRate = mixRate == 48_000 ? 44_100 : 48_000;
        using (var measurement = new ExpSweepMeasurement(Factory))
        {
            measurement.Init(
                8, wrongRate, 24, 0.25, PlaybackChannel.Right,
                audioBackend: AudioBackend.WasapiShared,
                waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
                wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId);

            Assert.False(await measurement.RunAsync());
            Assert.NotNull(measurement.LastError);
        }

        await AssertEndpointsReusableAsync(captureId, renderId!, mixRate);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task FullSweepMeasurementSupportsEightRuns()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int sampleRate = SharedMixRate(captureId);
        using var measurement = new ExpSweepMeasurement(Factory);
        measurement.Init(
            12, sampleRate, 24, 1.0, PlaybackChannel.Right,
            audioBackend: AudioBackend.WasapiShared,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            averageRunCount: 8,
            wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId,
            wasapiBufferMilliseconds: 100);

        bool succeeded = await measurement.RunAsync();

        Assert.True(succeeded, measurement.LastError?.ToString());
        Assert.Equal(8, measurement.AverageRunCount);
        Assert.Equal(8, measurement.AcceptedAverageRunCount);
        Assert.NotNull(measurement.LastAudioSessionDiagnostics);
        Assert.True(measurement.LastAudioSessionDiagnostics!.CapturePackets > 0);
        Assert.True(measurement.LastAudioSessionDiagnostics.RenderCallbacks > 0);
        Assert.True(measurement.LastAudioSessionDiagnostics.ActualBufferFrames > 0);
        Assert.Equal(0, measurement.LastAudioSessionDiagnostics.TimestampErrors);
        Assert.Equal(0, measurement.LastAudioSessionDiagnostics.RenderUnderruns);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task ExclusiveSweepRunsOnAReportedDuplexFormat()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int? sampleRate = FirstExclusiveRate(captureId, renderId!);
        Assert.NotNull(sampleRate);

        using var measurement = new ExpSweepMeasurement(Factory);
        measurement.Init(
            8, sampleRate!.Value, 24, 0.25, PlaybackChannel.Right,
            audioBackend: AudioBackend.WasapiExclusive,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            averageRunCount: 1,
            wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId,
            wasapiBufferMilliseconds: 100);

        bool succeeded = await measurement.RunAsync();

        Assert.True(succeeded, measurement.LastError?.ToString());
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AbortedSweepReleasesEndpointsForImmediateReuse()
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int sampleRate = SharedMixRate(captureId);
        using (var measurement = new ExpSweepMeasurement(Factory))
        {
            measurement.Init(
                16, sampleRate, 24, 5.0, PlaybackChannel.Right,
                audioBackend: AudioBackend.WasapiShared,
                waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
                wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId,
                wasapiBufferMilliseconds: 100);

            Task<bool> running = measurement.RunAsync();
            await Task.Delay(500);
            await measurement.AbortAsync();

            Assert.False(await running);
            Assert.Null(measurement.LastError);
            Assert.False(measurement.InProgress);
        }

        await AssertEndpointsReusableAsync(captureId, renderId!, sampleRate);
    }

    [Theory]
    [InlineData(AudioBackend.WasapiShared)]
    [InlineData(AudioBackend.WasapiExclusive)]
    [Trait("Category", "Hardware")]
    public async Task LiveNoiseProducesSpectrum(AudioBackend backend)
    {
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int sampleRate = backend == AudioBackend.WasapiExclusive
            ? FirstExclusiveRate(captureId, renderId!) ?? throw SkipNoExclusive()
            : SharedMixRate(captureId);

        using var measurement = new NoiseMeasurement(Factory);
        measurement.Init(
            sampleRate, 24, 2.0, PlaybackChannel.Right,
            sequenceLength: 2048,
            audioBackend: backend,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId,
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
        if (Endpoints() is not var (captureId, renderId) || captureId is null)
        {
            return;
        }

        int sampleRate = backend == AudioBackend.WasapiExclusive
            ? FirstExclusiveRate(captureId, renderId!) ?? throw SkipNoExclusive()
            : SharedMixRate(captureId);

        AudioSessionRequest request = AudioSessionRequestBuilder.Build(
            backend, sampleRate, 24, PlaybackChannel.Right,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            asioInputChannelOffset: 0, asioLoopbackInputChannelOffset: null, asioOutputChannelOffset: 0,
            outputDeviceNumber: -1, inputDeviceNumber: -1,
            wasapiCaptureEndpointId: captureId, wasapiRenderEndpointId: renderId, asioDriverName: null,
            bufferMilliseconds: 100, expectedCaptureSamples: 0);

        await Factory.WarmUpAsync(request, CancellationToken.None);
    }

    private static Exception SkipNoExclusive() =>
        new InvalidOperationException("No exclusive duplex format is supported by the endpoints.");
}
