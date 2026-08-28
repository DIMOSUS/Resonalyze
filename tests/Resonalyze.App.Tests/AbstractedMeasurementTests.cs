using Resonalyze.Audio;
using Resonalyze.Dsp;
using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace Resonalyze.App.Tests;

/// <summary>
/// Verifies the measurement layer runs end-to-end against a fake audio session
/// with no NAudio and no hardware — the core acceptance criterion of the audio
/// refactor.
/// </summary>
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

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.True(measurement.HasImpulseResponse);
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

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.NotNull(measurement.TransferImpulseResponse);
        Assert.NotNull(measurement.SweepDeconvolutionImpulseResponse);
        Assert.Equal(runCount > 1, measurement.TransferCoherence != null);

        using ExpSweepMeasurement uncompensated = CreateSweep(factory);
        bool uncompensatedSuccess = await uncompensated.RunAsync();
        Assert.True(uncompensatedSuccess, uncompensated.LastError?.ToString());

        Complex[] transfer = measurement.TransferImpulseResponse!.ToArray();
        Complex[] rawTransfer = uncompensated.TransferImpulseResponse!.ToArray();
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

        if (measurement.TransferCoherence is { } coherence)
        {
            int coherenceFftLength = (coherence.Length - 1) * 2;
            int coherenceStopBin = (int)Math.Round(
                250.0 * coherenceFftLength / measurement.SampleRate);
            int coherenceCornerBin = (int)Math.Round(
                2_000.0 * coherenceFftLength / measurement.SampleRate);
            // The two synthetic runs are identical, so raw MSC is one everywhere.
            // The protective-filter validity must still reject the unrecoverable
            // stopband while leaving the correction's trusted band untouched.
            Assert.Equal(0.0, coherence[coherenceStopBin], 12);
            Assert.InRange(coherence[coherenceCornerBin], 0.99, 1.0);
        }
        Assert.Equal(
            uncompensated.SweepDeconvolutionImpulseResponse,
            measurement.SweepDeconvolutionImpulseResponse);
    }

    [Fact]
    public async Task SubscriberExceptions_DoNotChangeSuccessfulOutcomeOrSkipOthers()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);
        bool impulseObserverCalled = false;
        bool completionObserverCalled = false;
        measurement.ImpulseResponseChanged += () =>
            throw new InvalidOperationException("broken impulse observer");
        measurement.ImpulseResponseChanged += () => impulseObserverCalled = true;
        measurement.Completed += _ =>
            throw new InvalidOperationException("broken completion observer");
        measurement.Completed += success => completionObserverCalled = success;

        bool result = await measurement.RunAsync();

        Assert.True(result);
        Assert.Null(measurement.LastError);
        Assert.True(impulseObserverCalled);
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

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.Equal(1, factory.DuplexOpenCount);
        Assert.NotNull(opened);
        Assert.Equal(3, opened!.CaptureCount);
        Assert.Equal(3, measurement.AcceptedAverageRunCount);
    }

    [Fact]
    public async Task RejectedRunStopsTheMeasurement()
    {
        // There used to be one automatic retry per bad run. The field answer is that
        // it never recovered anything: what these checks catch is a gain set wrong, a
        // cable in the wrong socket, a channel that is not there — configuration,
        // which the next sweep reproduces exactly. So the second sweep is not spent,
        // and the user is told at once instead of after the rest of the runs.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.SilentMicrophone(s, tail)
                    : SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        Assert.False(await measurement.RunAsync());
        Assert.Contains("silent", measurement.LastError!.Message);
        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        Assert.Equal(0, report.AcceptedRuns);
        Assert.Equal(1, Assert.Single(report.Rejections).Run);
    }

    // A cleanly attenuated wire at ~-41 dBFS must MEASURE: transfer
    // estimation is scale-invariant, and the readme itself tells the user to
    // turn the playback level well down. Level alone is no verdict — the
    // shape gate below owns the usable/garbage distinction.
    [Fact]
    public async Task QuietCleanLoopbackStillMeasures()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.QuietCleanLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.True(measurement.HasImpulseResponse);
    }

    // The field failure behind the shape gate: the "loopback" input picked
    // up bleed (~-41 dBFS of content uncorrelated with the sweep), every
    // per-run check passes, and the measurement must fail naming both the
    // non-compact shape and the suspicious reference level.
    [Fact]
    public async Task BleedLoopbackFailsNamingTheLevel()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.BleedLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.False(measurement.HasImpulseResponse);
        Assert.NotNull(measurement.LastError);
        Assert.Contains(
            "transfer function did not form a credible impulse response",
            measurement.LastError!.Message);
        Assert.Contains("bleed instead of the wire", measurement.LastError.Message);
        Assert.Contains("dBFS", measurement.LastError.Message);
    }

    // The field session that cost an evening: correct wiring, a loopback at a
    // perfectly normal -14.6 dBFS, and a refusal that said "check the wiring and
    // levels". The capture that actually causes it must be named — an overdriven
    // loopback INPUT, with the fix (attenuate what reaches it) — and the generic
    // advice must step aside when there is a real culprit to report.
    [Fact]
    public async Task DistortingLoopbackFailsNamingTheReference()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.DistortingLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.False(measurement.HasImpulseResponse);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("driven past its limit", message);
        Assert.Contains("Attenuate what reaches the loopback input", message);
        // The reference never came near full scale, which is the whole reason
        // the level checks and the meter missed it.
        Assert.Contains("peaked at only", message);
        Assert.DoesNotContain("Check the microphone and loopback wiring and levels", message);
    }

    // The refusal is about the AVERAGE, so the diagnosis has to be about the
    // same runs. Reading the distortion off one stored capture would describe
    // whichever run happened to be last: it would miss a bad first run entirely,
    // or report a bad last run as though the whole average carried it. The
    // reading is per run, and the message says how many runs it applies to.
    [Fact]
    public async Task DistortionDiagnosisCountsTheAffectedRuns()
    {
        // The first capture's loopback is overdriven, and that stops the measurement:
        // there is no retry, so it is also the only capture the diagnosis reads. The
        // message must still name the culprit and quote ITS levels rather than a
        // generic complaint about the wiring.
        //
        // Two companion tests lived here — one for the count across several runs, one
        // for scoping the verdict to the runs whose loopback could actually be read.
        // Neither is reachable any more: a measurement stops on its first bad run, so
        // the total-failure diagnosis has exactly one capture to describe and drops
        // the run-count clause. That clause is still live for the other path, where
        // runs WERE accepted and the average came out incredible, and these fixtures
        // never reached it.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.DistortingLoopback(s, tail)
                    : SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 2);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("its harmonic packets read -8.2 dB", message);
        Assert.Contains("it peaked at only -18.1 dBFS", message);
    }


    // The loopback diagnosis deconvolution is skipped above a size bound — it
    // exists only to phrase a refusal, and a hint must not add FFT-sized
    // allocations to every run of a long sweep. Pin the arithmetic: a field
    // sweep fits with orders of magnitude to spare; a long sweep does not, and
    // absurd lengths must read as "does not fit" rather than overflow.
    [Theory]
    [InlineData(396_000, 300_000, true)]           // ~3 s sweep at 96 kHz with tail
    [InlineData(2_100_000, 2_097_153, false)]      // just past the 2^22 bound
    [InlineData(int.MaxValue, int.MaxValue, false)] // must not overflow into a throw
    public void LoopbackDiagnosisFits_BoundsTheDiagnosisFft(
        int recordedSamples,
        int inverseSamples,
        bool expected)
    {
        Assert.Equal(
            expected,
            ExpSweepMeasurement.LoopbackDiagnosisFits(recordedSamples, inverseSamples));
    }

    // The tally's "judged" must mean the diagnosis could have confirmed OR
    // excluded a threshold-level fault. The third case is the reviewer's
    // counter-example: nothing detected, but the floors are high enough to
    // hide a harmonic well above the accusation threshold — certifying that
    // run clean is the bug, and it must read as no verdict. (A Fact, not a
    // Theory: the verdict enum is internal and cannot appear in a public
    // test signature.)
    [Fact]
    public void ClassifyDistortionReading_RequiresTheCeilingForACleanVerdict()
    {
        // A detection over the threshold accuses regardless of the ceiling.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Distorting,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-8.1, -8.0, CompleteCoverage: true)));
        // ...and regardless of coverage: what was found was found, and the
        // unread orders could only add to it. This is what keeps the
        // diagnosis alive on narrow-band sweeps whose high orders do not fit.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Distorting,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-8.1, -8.0, CompleteCoverage: false)));
        // An electrical wire: nothing detected, floors that could hide
        // nothing of consequence, every order read — the genuinely
        // certified-clean run.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.JudgedClean,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, -80.0, CompleteCoverage: true)));
        // Nothing detected, but a -15 dB harmonic could hide under these
        // floors: no verdict, never a clean certificate.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, -9.5, CompleteCoverage: true)));
        // A small detection does not certify either when the ceiling says a
        // threshold-level fault could still be hiding.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(-40.0, -20.0, CompleteCoverage: true)));
        // The review's partial-geometry hole: a spotless ceiling that covers
        // only some of the orders certifies nothing — an unread order can
        // hide anything.
        Assert.Equal(
            ExpSweepMeasurement.DistortionVerdict.Unjudged,
            ExpSweepMeasurement.ClassifyDistortionReading(
                new EssHarmonicEnergy(null, double.NegativeInfinity, CompleteCoverage: false)));
    }

    // The refusal quotes companion facts next to the worst distortion figure —
    // the run's microphone reading and its loopback peak. Those must all come
    // from the SAME run: the aggregate loopback peak is a maximum over runs,
    // and here it belongs to the loud CLEAN run (-0.9 dBFS), not to the
    // distorting one (-18 dBFS). Quoting the aggregate would juxtapose facts
    // no single capture showed — and would drop the "the meter had nothing to
    // show" note exactly when it applies.
    [Fact]
    public async Task DistortionDiagnosisQuotesTheLevelsOfTheWorstRun()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (attempt, s, tail, _) => Task.FromResult(attempt == 1
                    ? SyntheticCapture.DistortingLoopback(s, tail)
                    : SyntheticCapture.NoiseMicrophoneLoudLoopback(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 2);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("on that run it peaked at only -18", message);
    }

    // When both inputs are overdriven the refusal leads with the reference —
    // every analysis is divided by it — but it must not stay silent about the
    // microphone having crossed the threshold too.
    [Fact]
    public async Task DistortionDiagnosisNamesBothChannelsWhenBothAreOverdriven()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.DistortingBothInputs(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("LOOPBACK REFERENCE is distorting", message);
        Assert.Contains("microphone path crossed the distortion threshold as well", message);
    }


    // The mirror image: a clean reference and a distorting acoustic path must
    // not be blamed on the loopback.
    [Fact]
    public async Task CleanLoopbackIsNotAccusedOfDistortion()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.DoesNotContain("is distorting", measurement.LastError!.Message);
    }

    // The fail-closed side of the shape gate: one NaN in the capture slips
    // every level comparison and poisons the transfer IR into NaN, where
    // "compactness < threshold" would be false. An UNMEASURABLE shape must
    // refuse the measurement, not publish it.
    [Fact]
    public async Task NaNCaptureFailsClosed()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NaNMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.False(measurement.HasImpulseResponse);
        Assert.NotNull(measurement.LastError);
        Assert.Contains(
            "its shape could not be measured at all",
            measurement.LastError!.Message);
    }

    // The second garbage class: every level check passes (mic and loopback
    // both carry plausible signal), but the microphone recorded noise
    // uncorrelated with the sweep, so the transfer function divides into
    // stationary noise. The shape gate must fail the measurement with the
    // reason instead of publishing a garbage transfer IR.
    [Fact]
    public async Task NoiseTransferFailsTheMeasurementWithTheReason()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.NoiseMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.False(measurement.HasImpulseResponse);
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

        Task<bool> running = measurement.RunAsync();
        await captureStarted.Task;
        await measurement.AbortAsync();

        Assert.False(await running);
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

        bool success = await measurement.RunAsync();

        Assert.False(success);
        Assert.NotNull(measurement.LastError);
        Assert.Contains("device unplugged", measurement.LastError!.Message);
    }

    [Fact]
    public async Task OpenFailureSurfacesInResult()
    {
        var factory = new ThrowingOpenFactory();
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        bool success = await measurement.RunAsync();

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
