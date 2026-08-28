using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The measurement layer's array path against a fake sound card: which
/// microphones a run produces, what level each of their curves lands at, and
/// what happens to a microphone that fails.
/// </summary>
public sealed class ArrayMicrophoneTests
{
    private const int SampleRate = 44_100;

    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static ExpSweepMeasurement CreateSweep(
        IAudioSessionFactory factory,
        int runs = 1,
        IReadOnlyList<int>? arrayChannels = null,
        ProtectiveHighPassConfiguration? protectiveHighPass = null)
    {
        var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                SampleRate,
                24,
                0.2,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1,
                WaveArrayInputChannelOffsets: arrayChannels),
            new SweepAveragingConfiguration(runs),
            protectiveHighPass));
        return measurement;
    }

    private static int BandOf(double frequencyHz)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Grid.Count; i++)
        {
            double distance = Math.Abs(Math.Log2(Grid[i] / frequencyHz));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    // The measured level in the middle of the band, where a short synthetic
    // sweep has plenty of excitation and no edge effects.
    private static double MidBandDb(ArrayMicrophoneCurve microphone) =>
        microphone.LevelsDb[BandOf(1_000)];

    [Fact]
    public async Task EveryPositionBecomesOneCurve()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.25f, 0.125f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2, 3]);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        Assert.Equal(3, measurement.ArrayMicrophones.Count);

        // The measurement microphone leads: it is the anchor the others are
        // levelled onto, and it is a position in the volume like they are.
        Assert.True(measurement.ArrayMicrophones[0].IsMeasurementMicrophone);
        Assert.Equal(0, measurement.ArrayMicrophones[0].ChannelOffset);
        Assert.All(
            measurement.ArrayMicrophones.Skip(1),
            microphone => Assert.False(microphone.IsMeasurementMicrophone));
        Assert.Equal([2, 3], measurement.ArrayMicrophones.Skip(1).Select(m => m.ChannelOffset));
    }

    [Fact]
    public async Task ThePassedRoutingCarriesTheArrayToTheDevice()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(SyntheticCapture.WithArray(s, tail, 0.25f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        Assert.NotNull(factory.LastRequest);
        Assert.Equal([2], factory.LastRequest!.Routing.ArrayChannels);
    }

    [Fact]
    public async Task EachMicrophoneReadsItsOwnTransferLevel()
    {
        // Microphone 0.5 against a 0.25 loopback is |H| = 2 (+6.02 dB); the two
        // array microphones at 0.125 and 0.0625 are 0.5 and 0.25 (-6.02 and
        // -12.04 dB). The levels are transfer levels, so they are set by the
        // loopback and not by the playback level.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.125f, 0.0625f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2, 3]);

        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        Assert.Equal(6.02, MidBandDb(measurement.ArrayMicrophones[0]), 1);
        Assert.Equal(-6.02, MidBandDb(measurement.ArrayMicrophones[1]), 1);
        Assert.Equal(-12.04, MidBandDb(measurement.ArrayMicrophones[2]), 1);
    }

    [Fact]
    public async Task TheProtectiveHighPassIsDividedOutOfTheArrayToo()
    {
        // The filter is in the hardware ahead of the loudspeaker, so every
        // microphone carries it and the loopback does not. Left in an array
        // curve, a tweeter would sit a whole filter slope under its own impulse
        // response — so compensated, the array curve must read the same level
        // above the corner as it would have with no filter at all.
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200.0, 24);
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, edge, SampleRate, 0.125f))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory,
            arrayChannels: [2],
            protectiveHighPass: new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Butterworth,
                200.0,
                24));

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        ArrayMicrophoneCurve arrayMicrophone = measurement.ArrayMicrophones[1];
        Assert.Equal(-6.02, MidBandDb(arrayMicrophone), 1);

        // An octave below the corner the filter costs 24 dB, and the
        // compensation gives all of it back.
        Assert.Equal(-6.02, arrayMicrophone.LevelsDb[BandOf(100)], 0);
    }

    [Fact]
    public async Task AClippedArrayMicrophoneStopsTheMeasurement()
    {
        // The owner's rule: a run that compromised ANY microphone of the array is not
        // a run this measurement can use. It used to drop that position from that run
        // and keep going, which buys a measurement that looks complete and is not —
        // the array keeps only the curve each position produced, so a position that
        // lost its runs is simply absent from the average, and an average of one
        // position where two were set up is a different measurement wearing the same
        // name.
        int attempt = 0;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) =>
                {
                    attempt++;
                    // Driven past full scale on the first attempt only: the sweep
                    // itself peaks at 0.5, so 2.5 puts the channel over. A later
                    // capture would be clean — and is never taken, because the run
                    // that compromised a position stops the measurement.
                    return Task.FromResult(SyntheticCapture.WithArray(
                        s, tail, attempt == 1 ? 2.5f : 0.125f));
                }));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, runs: 2, arrayChannels: [2]);

        Assert.False(await measurement.RunAsync());
        Assert.Contains("array microphone on input 3", measurement.LastError!.Message);
        Assert.Contains("clipped", measurement.LastError.Message);

        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        Assert.True(report.IsDegraded);
        string described = report.Describe();
        Assert.Contains("array microphone on input 3", described);
        Assert.Contains("stopped the measurement", described);
    }

    [Fact]
    public async Task AnArrayMicrophoneThatIsWrongOnONERunStopsTheMeasurement()
    {
        // The intermittent fault, and the one an averaged verdict cannot see. Four
        // runs, and the array microphone records noise on the third capture only —
        // it is not silent, not clipped and not short, so every level check passes.
        //
        // Averaged over four frames it hides, and the noise is deliberately quiet
        // (0.005 against the sweep's 0.4) so that it does: three good runs still put
        // an arrival in the H1 total, the averaged shape stays compact, and a verdict
        // taken at the end says yes. Measured with only that verdict in place, this
        // measurement succeeds with four accepted runs and nothing reported.
        //
        // What the bad run does is not add noise to the curve. Its reference power
        // stays in the denominator of ΣGxy/ΣGxx while contributing nothing to the
        // numerator, so the position comes out scaled by 3/4 — exactly −2.50 dB,
        // measured, and INDEPENDENT of how quiet the noise was. That is why the
        // averaged backstop cannot be the whole rule: what it can see depends on the
        // noise level, and what the error costs does not.
        //
        // So the verdict belongs where the level checks are, on the RUN — and a bad
        // run stops the measurement, because the fault these checks catch is
        // configuration and the next sweep reproduces it exactly.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (capture, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArrayMicrophoneNoisyOnThisCapture(
                        s, tail, noisy: capture == 3, peak: 0.005))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, runs: 4, arrayChannels: [2]);

        Assert.False(await measurement.RunAsync());
        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        // Two clean runs were taken before the bad one, and none of them is published:
        // an average of two where four were asked for is a different measurement.
        Assert.Equal(2, report.AcceptedRuns);
        SweepRunRejection rejection = Assert.Single(report.Rejections);
        Assert.Equal(3, rejection.Run);
        string issue = Assert.Single(rejection.Issues);
        Assert.Contains("array microphone on input 3", issue);
        Assert.Contains("credible response", issue);
    }

    [Fact]
    public void TheRunFloorGivesBackExactlyWhatAveragingWouldHaveAdded()
    {
        // Not a chosen number. Averaging N runs leaves the coherent arrival alone and
        // divides the uncorrelated part of everything around it by N, so an average of
        // N runs each reading R lands in [R, R + 10·log₁₀N] — at R when what surrounds
        // the arrival is the room's own decay, at the top when it is noise. A single
        // run judged against the AVERAGED floor would therefore refuse runs whose
        // average would have passed.
        //
        // It matters because the margin is not generous: measured over the archived
        // cabins (80 channel measurements) genuine records read 27.7 dB at worst
        // against a floor of 22, and four runs of averaging can account for 6 of that
        // on its own. What the run floor gives away costs nothing against the fault it
        // looks for, which reads about 0 dB.
        Assert.Equal(
            TransferIrDiagnostics.MinimumCompactnessDb,
            ArrayMicrophoneAnalysis.RunFloorDb(1),
            9);
        Assert.Equal(
            TransferIrDiagnostics.MinimumCompactnessDb - 10.0 * Math.Log10(4),
            ArrayMicrophoneAnalysis.RunFloorDb(4),
            9);
        // Never stricter than the verdict it defers to, whatever it is handed.
        foreach (int runs in new[] { 0, 1, 2, 4, 8, 64 })
        {
            Assert.True(
                ArrayMicrophoneAnalysis.RunFloorDb(runs) <=
                    TransferIrDiagnostics.MinimumCompactnessDb);
        }
    }

    [Fact]
    public async Task TheMEASUREMENTMicrophoneIsJudgedOnTheRunToo()
    {
        // The same rule, on the channel it matters most for. A measurement that came
        // out 2.5 dB low because one run of four recorded noise is not obviously
        // wrong anywhere: the impulse response looks exactly as it should, and the
        // number it is out by is the number every other channel is levelled against.
        //
        // No array here at all — this is the ordinary measurement pair.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (capture, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithMeasurementMicrophoneNoisyOnThisCapture(
                        s, tail, noisy: capture == 3))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 4);

        Assert.False(await measurement.RunAsync());
        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        Assert.Equal(2, report.AcceptedRuns);
        SweepRunRejection rejection = Assert.Single(report.Rejections);
        Assert.Equal(3, rejection.Run);
        string issue = Assert.Single(rejection.Issues);
        Assert.Contains("the microphone recorded a signal", issue);
        Assert.Contains("credible response", issue);
    }

    [Fact]
    public async Task AFaultOnTheMEASUREMENTMicrophoneKeepsItsOwnDiagnosis()
    {
        // The array's credibility verdict is cruder than the measurement's own: it
        // knows a response has no shape, while RequireCredibleTransferIr also knows
        // that a quiet loopback means bleed instead of the wire, and which channel's
        // distortion is the culprit. The measurement microphone is a position in the
        // array too, so a check written for the array can reach it first — and then
        // an unusable REFERENCE is reported as a fault on an input that is working.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithNoisyMeasurementMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.False(await measurement.RunAsync());
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("The transfer function did not form a credible impulse", message);
        Assert.DoesNotContain("array microphone", message);
    }

    [Fact]
    public async Task AnArrayMicrophoneThatIsLiveButWrongFailsTheMeasurement()
    {
        // The dangerous one, because every level check passes: an unused preamp
        // hissing at an ordinary level is not silent, not clipped and not short. It
        // divides into an H1 estimate with no arrival in it — and the spatial average
        // would then trim its median level onto the measurement microphone's and give
        // it a full share of the result. A plausible curve, no exception, and a tune
        // fitted partly to a channel that measured nothing.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithNoisyArrayMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.False(await measurement.RunAsync());
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("array microphone on input 3", message);
        Assert.Contains("credible response", message);
    }

    [Fact]
    public async Task AnArrayMicrophoneThatNeverWorkedFailsTheMeasurement()
    {
        // Every attempt has a silent array microphone, so every run is rejected and
        // the measurement fails — loudly, naming the input. The alternative was a
        // successful measurement whose spatial average quietly had one position
        // fewer than the user set up.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.125f, 0.0f))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, arrayChannels: [2, 3]);

        Assert.False(await measurement.RunAsync());
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("array microphone on input 4", message);
        Assert.Contains("silent", message);
    }

    [Fact]
    public async Task WithoutAnArrayNothingIsProduced()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory);

        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        Assert.Empty(measurement.ArrayMicrophones);
        Assert.NotNull(factory.LastRequest);
        Assert.Empty(factory.LastRequest!.Routing.ArrayChannels);
    }

    [Fact]
    public void AChannelAlreadyInUseIsRefusedRatherThanDropped()
    {
        var factory = new FakeAudioSessionFactory();

        // Repairing this silently would run the measurement and produce an array
        // one microphone short of the one that was set up.
        Assert.Throws<InvalidOperationException>(
            () => CreateSweep(factory, arrayChannels: [1]));
        Assert.Throws<InvalidOperationException>(
            () => CreateSweep(factory, arrayChannels: [2, 2]));
    }
}
