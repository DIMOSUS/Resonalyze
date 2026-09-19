using Resonalyze.Audio;
using Resonalyze.Dsp;
using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace Resonalyze.App.Tests;

public sealed class AbstractedMeasurementTests
{
    private static ExpSweepMeasurement CreateSweep(IAudioSessionFactory factory, int runs = 1)
    {
        var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                44_100,
                24,
                0.05,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(runs)));
        return measurement;
    }

    [Fact]
    public async Task SweepMeasurementRunsAgainstFakeSession()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        MeasurementResult? result = await measurement.RunAsync();

        Assert.True(result != null, measurement.LastError?.ToString());
        Assert.Equal(TimingReference.SynchronizedLoopback, result.TimingReference);
        Assert.Equal(1, factory.DuplexOpenCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProtectiveHighPass_IsRemovedFromThePublishedTransferIrOnly(
        int runCount)
    {
        var edge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley,
            2_000,
            24);
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.ProtectedLoudspeaker(s, tail, edge, 44_100))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                44_100,
                24,
                0.05,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(runCount),
            new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.LinkwitzRiley,
                2_000,
                24)));

        MeasurementResult? compensated = await measurement.RunAsync();

        Assert.True(compensated != null, measurement.LastError?.ToString());
        Assert.NotNull(compensated.Transfer);
        Assert.Equal(runCount > 1, compensated.TransferCoherence != null);

        using ExpSweepMeasurement uncompensated = CreateSweep(factory);
        MeasurementResult? raw = await uncompensated.RunAsync();
        Assert.True(raw != null, uncompensated.LastError?.ToString());

        Complex[] transfer = compensated.Transfer!.ImpulseResponse.ToArray();
        Complex[] rawTransfer = raw.Transfer!.ImpulseResponse.ToArray();
        Fourier.Forward(transfer, FourierOptions.Matlab);
        Fourier.Forward(rawTransfer, FourierOptions.Matlab);
        int cornerBin = (int)Math.Round(2_000.0 * transfer.Length / measurement.SampleRate);
        int passBandBin = (int)Math.Round(8_000.0 * transfer.Length / measurement.SampleRate);
        int stopBandBin = (int)Math.Round(250.0 * transfer.Length / measurement.SampleRate);
        double transferCornerRelativeDb = 20.0 * Math.Log10(
            transfer[cornerBin].Magnitude / transfer[passBandBin].Magnitude);
        double rawCornerRelativeDb = 20.0 * Math.Log10(
            rawTransfer[cornerBin].Magnitude / rawTransfer[passBandBin].Magnitude);

        Assert.InRange(transferCornerRelativeDb, -0.2, 0.2);
        Assert.InRange(rawCornerRelativeDb, -6.3, -5.7);
        Assert.InRange(Math.Abs(transfer[cornerBin].Phase), 0.0, 0.05);
        double gatedStopBandRelativeDb = 20.0 * Math.Log10(
            transfer[stopBandBin].Magnitude / transfer[passBandBin].Magnitude);
        Assert.True(
            gatedStopBandRelativeDb < -140.0,
            $"unreliable stopband remained at {gatedStopBandRelativeDb:0.0} dB");

        if (compensated.TransferCoherence is { } coherence)
        {
            int coherenceFftLength = (coherence.Length - 1) * 2;
            int coherenceStopBin = (int)Math.Round(
                250.0 * coherenceFftLength / measurement.SampleRate);
            int coherenceCornerBin = (int)Math.Round(
                2_000.0 * coherenceFftLength / measurement.SampleRate);
            // Identical runs give MSC one everywhere; validity must still reject the unrecoverable stopband.
            Assert.Equal(0.0, coherence[coherenceStopBin], 12);
            Assert.InRange(coherence[coherenceCornerBin], 0.99, 1.0);
        }
        Assert.Equal(
            raw.SweepDeconvolution.ImpulseResponse,
            compensated.SweepDeconvolution.ImpulseResponse);
    }

    [Fact]
    public async Task SubscriberExceptions_DoNotChangeSuccessfulOutcomeOrSkipOthers()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);
        bool levelsObserverCalled = false;
        bool completionObserverCalled = false;
        measurement.LevelsAvailable += _ =>
            throw new InvalidOperationException("broken levels observer");
        measurement.LevelsAvailable += _ => levelsObserverCalled = true;
        measurement.Completed += _ =>
            throw new InvalidOperationException("broken completion observer");
        measurement.Completed += result => completionObserverCalled = result != null;

        MeasurementResult? result = await measurement.RunAsync();

        Assert.NotNull(result);
        Assert.Null(measurement.LastError);
        Assert.True(levelsObserverCalled);
        Assert.True(completionObserverCalled);
    }

    [Fact]
    public async Task AveragingReusesTheOpenSession()
    {
        RecordingDuplexSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => opened = new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 3);

        MeasurementResult? result = await measurement.RunAsync();

        Assert.True(result != null, measurement.LastError?.ToString());
        Assert.Equal(1, factory.DuplexOpenCount);
        Assert.NotNull(opened);
        Assert.Equal(3, opened!.CaptureCount);
        Assert.Equal(3, result.AcceptedAverageRunCount);
    }

    [Fact]
    public async Task RejectedRunStopsTheMeasurement()
    {
        // No retry: what these checks catch is configuration, which the next sweep reproduces.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.SilentMicrophone(s, tail)
                    : SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        Assert.Null(await measurement.RunAsync());
        Assert.Contains("silent", measurement.LastError!.Message);
        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        Assert.Equal(0, report.AcceptedRuns);
        Assert.Equal(1, Assert.Single(report.Rejections).Run);
    }

    // Transfer estimation is scale-invariant; level alone is no verdict.
    [Fact]
    public async Task QuietCleanLoopbackStillMeasures()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.QuietCleanLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.True(success, measurement.LastError?.ToString());
    }

    [Fact]
    public async Task BleedLoopbackFailsNamingTheLevel()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.BleedLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains(
            "transfer function did not form a credible impulse response",
            measurement.LastError!.Message);
        Assert.Contains("bleed instead of the wire", measurement.LastError.Message);
        Assert.Contains("dBFS", measurement.LastError.Message);
    }

    // Field case: loopback at a normal -14.6 dBFS but an overdriven input; the refusal must name it, not generic wiring advice.
    [Fact]
    public async Task DistortingLoopbackFailsNamingTheReference()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.DistortingLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("driven past its limit", message);
        Assert.Contains("Attenuate what reaches the loopback input", message);
        Assert.Contains("peaked at only", message);
        Assert.DoesNotContain("Check the microphone and loopback wiring and levels", message);
    }

    [Fact]
    public async Task DistortionDiagnosisCountsTheAffectedRuns()
    {
        // The first bad capture stops the measurement, so it is the only one the diagnosis reads.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.DistortingLoopback(s, tail)
                    : SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 2);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("its harmonic packets read -8.2 dB", message);
        Assert.Contains("it peaked at only -18.1 dBFS", message);
    }


    // The diagnosis deconvolution only phrases a refusal, so it is skipped above a size bound.
    [Theory]
    [InlineData(396_000, 300_000, true)]
    [InlineData(2_100_000, 2_097_153, false)] // just past the 2^22 bound
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void LoopbackDiagnosisFits_BoundsTheDiagnosisFft(
        int recordedSamples,
        int inverseSamples,
        bool expected)
    {
        Assert.Equal(
            expected,
            ExpSweepMeasurement.LoopbackDiagnosisFits(recordedSamples, inverseSamples));
    }

    // Clean needs a ceiling low enough to exclude a threshold-level fault. A Fact: the verdict enum is internal.
    [Fact]
    public void ClassifyDistortionReading_RequiresTheCeilingForACleanVerdict()
    {
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Distorting,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-8.1, -8.0, CompleteCoverage: true)));
        // Detection accuses regardless of coverage: keeps the diagnosis alive on narrow-band sweeps.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Distorting,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-8.1, -8.0, CompleteCoverage: false)));
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.JudgedClean,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, -80.0, CompleteCoverage: true)));
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, -9.5, CompleteCoverage: true)));
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-40.0, -20.0, CompleteCoverage: true)));
        // Partial coverage certifies nothing: an unread order can hide anything.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, double.NegativeInfinity, CompleteCoverage: false)));
    }

    // The aggregate loopback peak belongs to the loud clean run; quoted facts must come from the distorting run.
    [Fact]
    public async Task DistortionDiagnosisQuotesTheLevelsOfTheWorstRun()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.DistortingLoopback(s, tail)
                    : SyntheticCapture.NoiseMicrophoneLoudLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 2);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("on that run it peaked at only -18", message);
    }

    [Fact]
    public async Task DistortionDiagnosisNamesBothChannelsWhenBothAreOverdriven()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.DistortingBothInputs(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("microphone path crossed the distortion threshold as well", message);
    }


    [Fact]
    public async Task CleanLoopbackIsNotAccusedOfDistortion()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.DoesNotContain("is distorting", measurement.LastError!.Message);
    }

    // NaN makes "compactness < threshold" false: an unmeasurable shape must refuse.
    [Fact]
    public async Task NaNCaptureFailsClosed()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NaNMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains(
            "its shape could not be measured at all",
            measurement.LastError!.Message);
    }

    [Fact]
    public async Task NoiseTransferFailsTheMeasurementWithTheReason()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains(
            "transfer function did not form a credible impulse response",
            measurement.LastError!.Message);
    }

    [Fact]
    public async Task CancellationDisposesTheSession()
    {
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingDuplexSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => opened = new RecordingDuplexSession(
                signal,
                async (_, _, _, ct) =>
                {
                    captureStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return null!;
                }));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        Task<MeasurementResult?> running = measurement.RunAsync();
        await captureStarted.Task;
        await measurement.AbortAsync();

        Assert.Null(await running);
        Assert.Null(measurement.LastError);
        Assert.False(measurement.InProgress);
        Assert.NotNull(opened);
        Assert.True(opened!.Disposed);
    }

    [Fact]
    public async Task DeviceErrorSurfacesInResult()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, _, _, _) => throw new InvalidOperationException("device unplugged")));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains("device unplugged", measurement.LastError!.Message);
    }

    [Fact]
    public async Task OpenFailureSurfacesInResult()
    {
        var factory = new ThrowingOpenFactory();
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync() != null;

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
    }

    [Fact]
    public async Task LiveSpectrumFinishesOnDeviceFailure()
    {
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(framesToRaise: 2, failAfterFrames: true));
        using var measurement = new NoiseMeasurement(factory);
        measurement.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
    }

    [Fact]
    public async Task LiveSpectrumProducesSnapshotThenStops()
    {
        RecordingStreamingSession? opened = null;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => opened = new RecordingStreamingSession(
                framesToRaise: 40, failAfterFrames: false));
        using var measurement = new NoiseMeasurement(factory);
        measurement.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);

        Task<bool> running = measurement.RunAsync();
        LiveSpectrumSnapshot? snapshot = null;
        for (int i = 0; i < 200 && snapshot == null; i++)
        {
            await Task.Delay(20);
            snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        }
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.NotNull(snapshot);
        LiveSpectrumSnapshot? withoutInputMagnitude =
            measurement.GetAccumulatedSpectrumSnapshot(includeInputMagnitude: false);
        LiveSpectrumSnapshot? withInputMagnitude =
            measurement.GetAccumulatedSpectrumSnapshot(includeInputMagnitude: true);
        Assert.NotNull(withoutInputMagnitude);
        Assert.Null(withoutInputMagnitude.InputMagnitude);
        Assert.NotNull(withInputMagnitude?.InputMagnitude);
        Assert.NotNull(opened);
        Assert.True(opened!.Disposed);
    }

    [Theory]
    [InlineData(1.0f, true)]
    [InlineData(0.5f, false)]
    public async Task LiveSpectrumCountsTheAveragedFramesWhoseMicrophoneReachedFullScale(
        float microphonePeak, bool clips)
    {
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => new RecordingStreamingSession(
                framesToRaise: 40, failAfterFrames: false, microphonePeak));
        using var measurement = new NoiseMeasurement(factory);
        measurement.Init(
            44_100, 24, 0.5, PlaybackChannel.Mono,
            sequenceLength: 1024,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);

        Task<bool> running = measurement.RunAsync();
        LiveSpectrumSnapshot? snapshot = null;
        for (int i = 0; i < 200 && (snapshot?.FrameCount ?? 0) < 5; i++)
        {
            await Task.Delay(20);
            snapshot = measurement.GetAccumulatedSpectrumSnapshot();
        }
        await measurement.AbortAsync();

        Assert.True(await running, measurement.LastError?.ToString());
        Assert.NotNull(snapshot);
        Assert.True(snapshot.FrameCount >= 5);
        // Every frame of the tone peaks at the same level, so a clipping run counts exactly the frames it averaged.
        Assert.Equal(clips ? snapshot.FrameCount : 0, snapshot.ClippedFrameCount);
    }

    [Theory]
    [InlineData(1, true, 0.25)]
    [InlineData(2, true, 0.25)]
    [InlineData(40, true, 0.25)]
    [InlineData(2, false, 0.25)]
    [InlineData(4, false, 0.25)]
    [InlineData(40, false, 0.25)]
    [InlineData(400, false, 0.25)]
    [InlineData(4, false, 0.0211)]
    [InlineData(100, false, 0.0211)]
    [InlineData(40, false, 1.0)]
    public void TheCoherenceNoiseFloorIsTheSquaredWeightsTheAccumulatorHolds(
        int frames, bool infinite, double alpha)
    {
        // Independent model of the accumulator: the first frame enters whole, every later one with weight alpha.
        var weights = new List<double> { 1.0 };
        for (int frame = 2; frame <= frames; frame++)
        {
            double step = infinite ? 1.0 / frame : alpha;
            for (int i = 0; i < weights.Count; i++)
            {
                weights[i] *= 1.0 - step;
            }
            weights.Add(step);
        }

        Assert.Equal(1.0, weights.Sum(), 12);
        Assert.Equal(
            weights.Sum(weight => weight * weight),
            NoiseMeasurement.CoherenceNoiseFloor(frames, infinite, alpha),
            12);
    }

    [Fact]
    public void TheCoherenceNoiseFloorFallsWellShortOfTheSteadyStateWhileAnExponentialAverageRampsUp()
    {
        // The seed keeps 12% of the weight after 100 frames at Medium/2048/48 kHz with 50% overlap.
        Assert.Equal(0.0252, NoiseMeasurement.CoherenceNoiseFloor(100, infinite: false, 0.0211), 4);
        Assert.Equal(0.0107, NoiseMeasurement.CoherenceNoiseFloor(10_000, infinite: false, 0.0211), 4);
    }

    private sealed class ThrowingOpenFactory : IAudioSessionFactory
    {
        public IReadOnlyList<AudioBackendDescriptor> Backends { get; } =
            Array.Empty<AudioBackendDescriptor>();

        public AudioBackendDescriptor GetDescriptor(AudioBackend backend) =>
            new(backend, backend.ToString(), AudioBackendCapabilities.None);

        public ValueTask<IAudioDuplexSession> OpenDuplexAsync(
            AudioSessionRequest request, AudioPlaybackSignal signal, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public ValueTask<IAudioStreamingSession> OpenStreamingAsync(
            AudioSessionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public ValueTask<IAudioPlaybackSession> OpenPlaybackAsync(
            AudioSessionRequest request, AudioPlaybackSignal signal, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("cannot open device");

        public Task WarmUpAsync(AudioSessionRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
