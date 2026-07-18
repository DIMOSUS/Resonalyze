using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class SilentLiveSpectrumTests
{
    [Fact]
    public async Task EffectiveSplFallback_RebuildsPlaybackAsPeriodicPink()
    {
        RecordingStreamingSession? session = null;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => session = new RecordingStreamingSession(
                framesToRaise: 1,
                failAfterFrames: false));
        using var measurement = new NoiseMeasurement(factory);
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Silent };
        measurement.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            liveSpectrumOptions: options);

        Assert.True(LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative));
        measurement.RefreshPlaybackSignal();

        Task<bool> running = measurement.RunAsync();
        for (int attempt = 0;
            attempt < 100 && session?.LastPlaybackSignal == null;
            attempt++)
        {
            await Task.Delay(10);
        }
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
        Assert.Contains(
            session!.LastPlaybackSignal!.MonoSamples,
            sample => sample != 0.0f);
    }

    [Fact]
    public void NormalizedSilentSignal_IsCapturedAsPeriodicPinkForPersistence()
    {
        // The persistence follow-up: once the runtime signal is normalized away from
        // Silent (SPL lost), capturing the live options for the settings file must
        // record the normalized value, so a stale Silent cannot return on next launch.
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Silent };
        LiveSpectrumController.NormalizeSignalType(options, MagnitudeScale.Relative);

        MeasurementSettingsFile.LiveSpectrumSettings captured =
            MeasurementSettingsFile.LiveSpectrumSettings.Capture(options);

        Assert.Equal(NoiseColor.PinkPeriodic, captured.NoiseColor);
    }

    [Fact]
    public async Task SilentWithLoopback_UsesMicrophoneOnlyAnalysisAndZeroPlayback()
    {
        RecordingStreamingSession? session = null;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => session = new RecordingStreamingSession(
                framesToRaise: 20,
                failAfterFrames: false));
        using var measurement = new NoiseMeasurement(factory);
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Silent };
        measurement.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            liveSpectrumOptions: options);

        Task<bool> running = measurement.RunAsync();
        LiveSpectrumSnapshot? snapshot = null;
        for (int attempt = 0; attempt < 100 && snapshot == null; attempt++)
        {
            await Task.Delay(10);
            snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        }
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Magnitude);
        Assert.Null(snapshot.Coherence);
        Assert.NotNull(snapshot.InputMagnitude);
        Assert.NotNull(session?.LastPlaybackSignal);
        Assert.All(
            session!.LastPlaybackSignal!.MonoSamples,
            sample => Assert.Equal(0.0f, sample));
    }
}
