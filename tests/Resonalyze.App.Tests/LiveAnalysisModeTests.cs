using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class LiveAnalysisModeTests
{
    [Fact]
    public async Task RtaModeWithLoopback_UsesMicOnlyAnalysisAndKeepsTheExcitation()
    {
        // The decoupling the explicit mode buys: an RTA capture stays reference-free
        // even when a loopback IS configured — and unlike Silent it still plays its
        // excitation. Before the split, RTA analysis existed only as a side effect of
        // the Silent signal or a missing loopback.
        RecordingStreamingSession? session = null;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => session = new RecordingStreamingSession(
                framesToRaise: 20,
                failAfterFrames: false));
        using var measurement = new NoiseMeasurement(factory);
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.PinkPeriodic
        };
        measurement.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            liveSpectrumOptions: options);

        Assert.True(measurement.HasConfiguredLoopback);
        Assert.True(measurement.IsRtaCapture);

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
        Assert.Contains(
            session!.LastPlaybackSignal!.MonoSamples,
            sample => sample != 0.0f);
    }

    [Fact]
    public void TransferModeWithLoopback_StaysTheTransferAnalyzer()
    {
        using var measurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        measurement.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1,
            liveSpectrumOptions: new LiveSpectrumOptions
            {
                AnalysisMode = LiveAnalysisMode.TransferFunction
            });

        Assert.False(measurement.IsRtaCapture);
    }

    [Fact]
    public void TransferModeWithoutLoopback_FallsBackToRtaAnalysis()
    {
        using var measurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        measurement.Init(
            44_100,
            24,
            0.5,
            PlaybackChannel.Mono,
            sequenceLength: 1024,
            liveSpectrumOptions: new LiveSpectrumOptions
            {
                AnalysisMode = LiveAnalysisMode.TransferFunction
            });

        Assert.True(measurement.IsMicOnly);
        Assert.True(measurement.IsRtaCapture);
    }

    // ----- settings migration: files written before the explicit mode existed -----

    [Fact]
    public void LegacySplScale_MigratesToRtaMode()
    {
        // Before the split, dB SPL was only reachable as an RTA, so a legacy file
        // with the SPL scale selected marks an RTA session.
        var legacy = new MeasurementSettingsFile.LiveSpectrumSettings
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.Pink
        };

        var options = new LiveSpectrumOptions();
        legacy.ApplyTo(options);

        Assert.Equal(LiveAnalysisMode.Rta, options.AnalysisMode);
        Assert.Equal(NoiseColor.Pink, options.NoiseColor);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, options.MagnitudeScale);
    }

    [Fact]
    public void LegacySilentSignal_MigratesToRtaModeKeepingSilent()
    {
        var legacy = new MeasurementSettingsFile.LiveSpectrumSettings
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.Silent
        };

        var options = new LiveSpectrumOptions();
        legacy.ApplyTo(options);

        Assert.Equal(LiveAnalysisMode.Rta, options.AnalysisMode);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Fact]
    public void LegacyRelativeTransfer_MigratesToTransferMode()
    {
        var legacy = new MeasurementSettingsFile.LiveSpectrumSettings
        {
            MagnitudeScale = MagnitudeScale.Relative,
            NoiseColor = NoiseColor.PinkPeriodic
        };

        var options = new LiveSpectrumOptions();
        legacy.ApplyTo(options);

        Assert.Equal(LiveAnalysisMode.TransferFunction, options.AnalysisMode);
    }

    [Fact]
    public void ExplicitMode_WinsOverTheLegacyInference()
    {
        // A new file stores the mode explicitly; a stored SPL scale is then just the
        // remembered RTA-mode checkbox preference, not evidence of an RTA session.
        var stored = new MeasurementSettingsFile.LiveSpectrumSettings
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic
        };

        var options = new LiveSpectrumOptions();
        stored.ApplyTo(options);

        Assert.Equal(LiveAnalysisMode.TransferFunction, options.AnalysisMode);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, options.MagnitudeScale);
    }

    [Fact]
    public void HandEditedSilentInTransferMode_RepairsTheSignal()
    {
        // Only a hand-edited file can pair Silent with an explicit Transfer mode; the
        // invariant (Silent is RTA-only) is repaired on load, keeping the stated mode.
        var stored = new MeasurementSettingsFile.LiveSpectrumSettings
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            NoiseColor = NoiseColor.Silent
        };

        var options = new LiveSpectrumOptions();
        stored.ApplyTo(options);

        Assert.Equal(LiveAnalysisMode.TransferFunction, options.AnalysisMode);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void ModeAndTilt_SurviveACaptureRoundTrip()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.White,
            CompensateNoiseTilt = true
        };

        MeasurementSettingsFile.LiveSpectrumSettings captured =
            MeasurementSettingsFile.LiveSpectrumSettings.Capture(options);
        var restored = new LiveSpectrumOptions();
        captured.ApplyTo(restored);

        Assert.Equal(LiveAnalysisMode.Rta, restored.AnalysisMode);
        Assert.Equal(NoiseColor.White, restored.NoiseColor);
        Assert.True(restored.CompensateNoiseTilt);
    }
}
