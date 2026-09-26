using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Live Spectrum's state without UI: what a run leaves held, what a discard or a loaded capture replaces, and what a
/// capture document is built from. The analyzer runs on a fake input; nothing here draws.
/// </summary>
public sealed class LiveSpectrumSessionTests
{
    [Fact]
    public async Task AStopHoldsTheRunsFinalReading()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Pink
        });
        int changed = 0;
        session.Changed += () => changed++;

        session.Start();
        Assert.True(session.InProgress);
        Assert.True(session.HasDisplayableCurve);
        await FirstFrameAsync(session);
        LiveSpectrumSnapshot? held = await session.StopAsync();

        Assert.NotNull(held);
        Assert.False(session.InProgress);
        Assert.Same(held, session.HeldSnapshot);
        Assert.Equal(session.Reread(session.Display)!.FrameCount, held!.FrameCount);
        Assert.True(session.HasDisplayableCurve);
        Assert.True(session.HasCaptureToSave);
        Assert.Equal(2, changed);
    }

    // The analyzer reports on its own thread; the controller hands the end to the session on the UI thread.
    [Fact]
    public async Task ADeviceFailureIsReportedWhenTheRunEnds()
    {
        var options = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.Rta };
        var analyzer = new NoiseMeasurement(new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(
                framesToRaise: 3,
                failAfterFrames: true,
                microphonePeak: 0.5f)));
        analyzer.Init(44_100, 24, 0.5, PlaybackChannel.Mono, sequenceLength: 1024, liveSpectrumOptions: options);
        using var session = new LiveSpectrumSession(
            analyzer, options, () => null, _ => CapturedMicrophoneCalibration.None);
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Completed += success => ended.TrySetResult(success);
        var reported = new List<Exception>();
        session.Failed += reported.Add;

        session.Start();
        bool succeeded = await ended.Task.WaitAsync(TimeSpan.FromSeconds(20));
        session.RunEnded(succeeded);

        Assert.False(succeeded);
        Assert.IsType<InvalidOperationException>(Assert.Single(reported));
    }

    [Fact]
    public async Task AStopIsNotAFailure()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.Rta });
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Completed += success => ended.TrySetResult(success);
        var reported = new List<Exception>();
        session.Failed += reported.Add;

        session.Start();
        await FirstFrameAsync(session);
        await session.StopAsync();
        session.RunEnded(await ended.Task.WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.Empty(reported);
    }

    // A stopped curve records the previous acquisition setup while the display reads options live, so a change discards it.
    [Fact]
    public async Task DiscardDropsTheAccumulationTheHeldCurveAndItsEnvelope()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Pink
        });
        session.Start();
        await FirstFrameAsync(session);
        await session.StopAsync();
        session.PeakHold.Hold([new SignalPoint(1000.0, 85.0)]);
        int changed = 0;
        session.Changed += () => changed++;

        session.Discard();

        Assert.Null(session.HeldSnapshot);
        Assert.Null(session.Reread(session.Display));
        Assert.Null(session.PeakHold.Points);
        Assert.False(session.HasDisplayableCurve);
        Assert.Equal(1, changed);
    }

    // History restores and Record Settings reconfigure a stopped analyzer; its bins must not be re-read at another rate.
    [Fact]
    public async Task ANewCaptureSessionDropsTheStoppedReading_TheSameOneKeepsIt()
    {
        var options = new LiveSpectrumOptions { AnalysisMode = LiveAnalysisMode.Rta, SequenceLength = 1024 };
        using LiveSpectrumSession session = Create(options);
        session.Start();
        await FirstFrameAsync(session);
        await session.StopAsync();
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            SampleRate = 44_100,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1
        };

        session.Configure(settings);
        Assert.NotNull(session.HeldSnapshot);
        Assert.NotNull(session.Reread(session.Display));

        settings.SampleRate = 48_000;
        session.Configure(settings);
        Assert.Null(session.HeldSnapshot);
        Assert.Null(session.Reread(session.Display));
        Assert.False(session.HasCaptureToSave);

        session.Start();
        await FirstFrameAsync(session);
        await session.StopAsync();
        // Another input: Save would file the reading under that input's session and SPL anchor.
        settings.WaveInputChannelOffset = 1;
        settings.WaveLoopbackInputChannelOffset = 0;
        session.Configure(settings);
        Assert.Null(session.HeldSnapshot);
        Assert.False(session.HasCaptureToSave);
    }

    // Runs in every mode: the envelope is invalid wherever the analyzer sits when the calibration moves.
    [Fact]
    public void ACalibrationChangeDropsThePeakHold()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions());
        session.PeakHold.Hold([new SignalPoint(1000.0, 85.0)]);

        session.InvalidateCalibration();

        Assert.Null(session.PeakHold.Points);
    }

    [Fact]
    public async Task ALoadedCaptureStandsInForTheRunUntilTheNextStart()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm
        });
        session.Start();
        await FirstFrameAsync(session);
        await session.StopAsync();
        LiveCaptureDocument document = LoadedMmm(clippedFrames: null);
        int changed = 0;
        session.Changed += () => changed++;

        session.ShowLoaded(document);

        Assert.Same(document, session.LoadedCapture);
        Assert.Null(session.HeldSnapshot);
        // Re-saving would restamp the capture with this session's recipe.
        Assert.False(session.HasCaptureToSave);
        Assert.Equal(string.Empty, session.DisplayedCalibrationName);
        Assert.Equal(1, changed);

        session.Start();
        Assert.Null(session.LoadedCapture);
        await session.StopAsync();
    }

    // Bins are re-rendered on Save; a microphone chosen mid-walk must not reach the walk.
    [Fact]
    public async Task ACaptureKeepsTheMicrophoneItsRunStartedThrough()
    {
        CalibrationFile a = CalibrationFile.FromPoints(
            [new CalibrationPoint(20, 6.0), new CalibrationPoint(20_000, 6.0)]);
        CalibrationFile b = CalibrationFile.FromPoints(
            [new CalibrationPoint(20, -6.0), new CalibrationPoint(20_000, -6.0)]);
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            CalibrationId = "cal-a"
        };
        using LiveSpectrumSession session = Create(
            options,
            id => id == "cal-a"
                ? new CapturedMicrophoneCalibration(id, "capsule A", a)
                : new CapturedMicrophoneCalibration(id, "capsule B", b));

        session.Start();
        await FirstFrameAsync(session);
        options.CalibrationId = "cal-b";
        await session.StopAsync();

        LiveCaptureDocument? document = session.BuildCaptureDocument();
        Assert.NotNull(document);
        Assert.Equal("capsule A", document.Calibration?.Name);
        Assert.Equal("capsule A", session.DisplayedCalibrationName);
        Assert.All(document.CalibrationCorrectionDb, correction => Assert.Equal(6.0, correction, precision: 6));
    }

    [Theory]
    [InlineData(null, "Loaded — 17 s, 25 frames")]
    [InlineData(0, "Loaded — 17 s, 25 frames")]
    [InlineData(3, "Loaded — 17 s, 25 frames, 3 clipped")]
    public void TheReadOutOfALoadedCaptureIsItsOwnRecipe(int? clippedFrames, string text)
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm
        });

        session.ShowLoaded(LoadedMmm(clippedFrames));

        Assert.Equal(text, session.Progress(session.Display)?.Text);
    }

    [Fact]
    public void OnlyMmmReadsOutItsProgress()
    {
        using LiveSpectrumSession session = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta
        });

        session.ShowLoaded(LoadedMmm(clippedFrames: 3));

        Assert.Null(session.Progress(session.Display));
    }

    // A checkbox must not throw away minutes of walking, but a running Infinite RTA restarts under the new display.
    [Fact]
    public async Task ARunningInfiniteAverageRestartsOnADisplayChange_AStoppedOneAndAWalkDoNot()
    {
        using LiveSpectrumSession rta = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            AveragingSpeed = AveragingSpeed.Infinite,
            PeakHold = true
        });
        using LiveSpectrumSession walk = Create(new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm
        });
        rta.Start();
        walk.Start();
        await FirstFrameAsync(rta);
        await FirstFrameAsync(walk);
        rta.PeakHold.Drawn(rta.Display.PeakHoldKey);
        rta.PeakHold.Hold([new SignalPoint(1000.0, 85.0)]);

        Assert.True(rta.ApplyDisplayOptions());
        Assert.Null(rta.PeakHold.Points);
        Assert.False(walk.ApplyDisplayOptions());

        await rta.StopAsync();
        Assert.False(rta.ApplyDisplayOptions());
        Assert.NotNull(rta.Reread(rta.Display));
        await walk.StopAsync();
    }

    [Fact]
    public void ADisplayChangeDropsAnEnvelopeDrawnUnderAnotherTransform()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            PeakHold = true,
            SmoothingInverseOctaves = 6
        };
        using LiveSpectrumSession session = Create(options);
        session.PeakHold.Drawn(session.Display.PeakHoldKey);
        session.PeakHold.Hold([new SignalPoint(1000.0, 85.0)]);

        session.ApplyDisplayOptions();
        Assert.NotNull(session.PeakHold.Points);

        options.SmoothingInverseOctaves = 12;
        session.ApplyDisplayOptions();
        Assert.Null(session.PeakHold.Points);
    }

    // Everything the plot reads from the analyzer comes through Setup, so it must be what the analyzer runs.
    [Fact]
    public void TheSetupIsWhatTheAnalyzerRuns()
    {
        var options = new LiveSpectrumOptions
        {
            NoiseColor = NoiseColor.PinkPeriodic,
            WindowType = WindowType.Hann,
            OverlapPercent = 50
        };
        using var analyzer = new NoiseMeasurement(new FakeAudioSessionFactory());
        analyzer.Init(
            48_000, 24, 0.5, PlaybackChannel.Mono, sequenceLength: 4096,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1, liveSpectrumOptions: options);

        LiveCaptureSetup periodic = analyzer.Setup;
        Assert.Equal(48_000, periodic.SampleRate);
        Assert.Equal(4096, periodic.SequenceLength);
        // A whole periodic-pink period per frame: rectangular, no overlap.
        Assert.Equal(WindowType.Rectangular, periodic.WindowType);
        Assert.Equal(Windowing.EquivalentNoiseBandwidthBins(WindowType.Rectangular, 4096), periodic.WindowEnbwBins);
        Assert.Equal(4096, periodic.HopSize);
        Assert.False(periodic.IsMicOnly);
        Assert.Equal(analyzer.CurrentInputIdentity(), periodic.Input);

        options.NoiseColor = NoiseColor.Pink;
        LiveCaptureSetup random = analyzer.Setup;
        Assert.Equal(WindowType.Hann, random.WindowType);
        Assert.Equal(Windowing.MainLobeWidthBins(WindowType.Hann), random.WindowMainLobeBins);
        Assert.Equal(2048, random.HopSize);

        var filter = new ProtectiveHighPassConfiguration(ProtectiveHighPassKind.Butterworth, 80, 24);
        CalibrationFile curve = CalibrationFile.FromPoints(
            [new CalibrationPoint(20, 1.0), new CalibrationPoint(20_000, 1.0)]);
        analyzer.SetCaptureProtectiveHighPass(filter);
        analyzer.SetCaptureMicrophoneCalibration(new CapturedMicrophoneCalibration("id", "capsule", curve));
        LiveCaptureSetup frozen = analyzer.Setup;
        Assert.Equal(filter, frozen.ProtectiveHighPass);
        Assert.Same(curve, frozen.MicrophoneCalibration);
        Assert.Equal("capsule", frozen.MicrophoneCalibrationName);
        Assert.Equal(analyzer.CaptureSessionId, frozen.CaptureSessionId);

        analyzer.Init(48_000, 24, 0.5, PlaybackChannel.Mono, waveInputChannelOffset: 0);
        Assert.True(analyzer.Setup.IsMicOnly);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AReconfigureRepeatingTheRouteKeepsTheCaptureSession_AndANewRouteStartsOne()
    {
        using var analyzer = new NoiseMeasurement(new FakeAudioSessionFactory());
        analyzer.Init(
            48_000, 24, 0.5, PlaybackChannel.Mono, sequenceLength: 4096,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1, liveSpectrumOptions: new LiveSpectrumOptions());
        Guid first = analyzer.CaptureSessionId;

        analyzer.Init(
            48_000, 24, 60, PlaybackChannel.Mono, sequenceLength: 4096,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1,
            liveSpectrumOptions: new LiveSpectrumOptions { WindowType = WindowType.Rectangular });
        Assert.Equal(first, analyzer.CaptureSessionId);

        analyzer.Init(
            96_000, 24, 60, PlaybackChannel.Mono, sequenceLength: 4096,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1, liveSpectrumOptions: new LiveSpectrumOptions());
        Assert.NotEqual(first, analyzer.CaptureSessionId);
    }

    private static LiveSpectrumSession Create(
        LiveSpectrumOptions options,
        Func<string?, CapturedMicrophoneCalibration>? resolveCalibration = null)
    {
        var analyzer = new NoiseMeasurement(new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(
                framesToRaise: 20,
                failAfterFrames: false,
                microphonePeak: 0.5f)));
        analyzer.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono, sequenceLength: 1024,
            waveInputChannelOffset: 0, waveLoopbackInputChannelOffset: 1, liveSpectrumOptions: options);
        return new LiveSpectrumSession(
            analyzer,
            options,
            () => null,
            resolveCalibration ?? (_ => CapturedMicrophoneCalibration.None));
    }

    private static async Task FirstFrameAsync(LiveSpectrumSession session)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            if (session.ReadFrame(session.Display) != null)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The analyzer read no frame.");
    }

    private static LiveCaptureDocument LoadedMmm(int? clippedFrames) => new()
    {
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            AveragedFrameCount = 25,
            ClippedFrameCount = clippedFrames,
            IntegratedSeconds = 17.07
        }
    };
}
