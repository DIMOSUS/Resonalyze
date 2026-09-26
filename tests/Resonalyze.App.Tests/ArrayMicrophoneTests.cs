using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

    private static double MidBandDb(ArrayMicrophoneCurve microphone) =>
        microphone.LevelsDb[BandOf(1_000)];

    [Fact]
    [Trait("Category", "Slow")]
    public async Task EveryPositionBecomesOneCurve()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.25f, 0.125f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2, 3]);

        MeasurementResult? result = await measurement.RunAsync();

        bool success = result != null;

        Assert.True(success, measurement.LastError?.ToString());
        Assert.Equal(3, result!.ArrayMicrophones.Count);

        Assert.True(result!.ArrayMicrophones[0].IsMeasurementMicrophone);
        Assert.Equal(0, result!.ArrayMicrophones[0].ChannelOffset);
        Assert.All(
            result!.ArrayMicrophones.Skip(1),
            microphone => Assert.False(microphone.IsMeasurementMicrophone));
        Assert.Equal([2, 3], result!.ArrayMicrophones.Skip(1).Select(m => m.ChannelOffset));
    }

    [Fact]
    public async Task ThePassedRoutingCarriesTheArrayToTheDevice()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(SyntheticCapture.WithArray(s, tail, 0.25f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        MeasurementResult? result = await measurement.RunAsync();

        Assert.True(result != null, measurement.LastError?.ToString());

        Assert.NotNull(factory.LastRequest);
        Assert.Equal([2], factory.LastRequest!.Routing.ArrayChannels);
    }

    [Fact]
    public async Task EachMicrophoneReadsItsOwnTransferLevel()
    {
        // Mic 0.5 over a 0.25 loopback is |H| = 2 (+6.02 dB): levels are set by the loopback, not playback.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.125f, 0.0625f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2, 3]);

        MeasurementResult? result = await measurement.RunAsync();

        Assert.True(result != null, measurement.LastError?.ToString());

        Assert.Equal(6.02, MidBandDb(result!.ArrayMicrophones[0]), 1);
        Assert.Equal(-6.02, MidBandDb(result!.ArrayMicrophones[1]), 1);
        Assert.Equal(-12.04, MidBandDb(result!.ArrayMicrophones[2]), 1);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task TheProtectiveHighPassIsDividedOutOfTheArrayToo()
    {
        // The hardware filter is on every mic but not the loopback; compensated, the level above the corner is unfiltered.
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

        MeasurementResult? result = await measurement.RunAsync();

        bool success = result != null;

        Assert.True(success, measurement.LastError?.ToString());
        ArrayMicrophoneCurve arrayMicrophone = result!.ArrayMicrophones[1];
        Assert.Equal(-6.02, MidBandDb(arrayMicrophone), 1);

        Assert.Equal(-6.02, arrayMicrophone.LevelsDb[BandOf(100)], 0);
    }

    [Fact]
    public async Task AClippedArrayMicrophoneStopsTheMeasurement()
    {
        // A run that compromised any array microphone is unusable: an average missing a position is a different measurement.
        int attempt = 0;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) =>
                {
                    attempt++;
                    // The sweep peaks at 0.5, so 2.5 clips the first attempt.
                    return Task.FromResult(SyntheticCapture.WithArray(
                        s, tail, attempt == 1 ? 2.5f : 0.125f));
                }));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, runs: 2, arrayChannels: [2]);

        Assert.Null(await measurement.RunAsync());
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
    [Trait("Category", "Slow")]
    public async Task AnArrayMicrophoneThatIsWrongOnONERunStopsTheMeasurement()
    {
        // Noise on one run of four hides in the average but scales the position by 3/4 (-2.50 dB) regardless of noise level,
        // so the verdict is per run and a bad run stops the measurement (configuration faults reproduce).
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (capture, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArrayMicrophoneNoisyOnThisCapture(
                        s, tail, noisy: capture == 3, peak: 0.005))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, runs: 4, arrayChannels: [2]);

        Assert.Null(await measurement.RunAsync());
        SweepRunQualityReport report = Assert.IsType<SweepRunQualityReport>(
            measurement.QualityReport);
        Assert.Equal(2, report.AcceptedRuns);
        SweepRunRejection rejection = Assert.Single(report.Rejections);
        Assert.Equal(3, rejection.Run);
        string issue = Assert.Single(rejection.Issues);
        Assert.Contains("array microphone on input 3", issue);
        Assert.Contains("credible response", issue);
    }

    [Fact]
    public void TheRunCheckIsSkippedWhereItsTransformWouldNotFit()
    {
        // The verdict needs an H1 padded to twice the capture; 384 kHz x 100 s would cost gigabytes. 20 s at 96 kHz fits (bound just past 2 M).
        Assert.True(ExpSweepMeasurement.RunCredibilityDiagnosisFits(1_920_000));
        Assert.True(ExpSweepMeasurement.RunCredibilityDiagnosisFits(
            (1 << 21)));
        Assert.False(ExpSweepMeasurement.RunCredibilityDiagnosisFits(
            (1 << 21) + 1));
        Assert.False(ExpSweepMeasurement.RunCredibilityDiagnosisFits(38_400_000));
        Assert.False(ExpSweepMeasurement.RunCredibilityDiagnosisFits(int.MaxValue));
        Assert.False(ExpSweepMeasurement.RunCredibilityDiagnosisFits(0));
    }

    [Fact]
    public void TheRunFloorGivesBackExactlyWhatAveragingWouldHaveAdded()
    {
        // Averaging N runs lifts compactness by up to 10·log10(N), so a single run is judged against a lower floor.
        // Archived cabins: genuine records read 27.7 dB at worst against 22; the fault reads about 0 dB.
        Assert.Equal(
            TransferIrDiagnostics.MinimumCompactnessDb,
            ArrayMicrophoneAnalysis.RunFloorDb(1),
            9);
        Assert.Equal(
            TransferIrDiagnostics.MinimumCompactnessDb - 10.0 * Math.Log10(4),
            ArrayMicrophoneAnalysis.RunFloorDb(4),
            9);
        foreach (int runs in new[] { 0, 1, 2, 4, 8, 64 })
        {
            Assert.True(
                ArrayMicrophoneAnalysis.RunFloorDb(runs) <=
                    TransferIrDiagnostics.MinimumCompactnessDb);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task TheMEASUREMENTMicrophoneIsJudgedOnTheRunToo()
    {
        // One noisy run of four leaves the measurement mic 2.5 dB low, the level every other channel is levelled against.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (capture, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithMeasurementMicrophoneNoisyOnThisCapture(
                        s, tail, noisy: capture == 3))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, runs: 4);

        Assert.Null(await measurement.RunAsync());
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
        // The measurement's own diagnosis (bleed, distorting channel) must speak before the array's cruder verdict.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithNoisyMeasurementMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.Null(await measurement.RunAsync());
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("The transfer function did not form a credible impulse", message);
        Assert.DoesNotContain("array microphone", message);
    }

    [Fact]
    public async Task AnArrayMicrophoneThatIsLiveButWrongFailsTheMeasurement()
    {
        // A hissing preamp passes every level check, and the spatial average would give it a full share.
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithNoisyArrayMicrophone(s, tail))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.Null(await measurement.RunAsync());
        Assert.NotNull(measurement.LastError);
        string message = measurement.LastError!.Message;
        Assert.Contains("array microphone on input 3", message);
        Assert.Contains("credible response", message);
    }

    [Fact]
    public async Task AnArrayMicrophoneThatNeverWorkedFailsTheMeasurement()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.125f, 0.0f))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, arrayChannels: [2, 3]);

        Assert.Null(await measurement.RunAsync());
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

        MeasurementResult? result = await measurement.RunAsync();

        Assert.True(result != null, measurement.LastError?.ToString());

        Assert.Empty(result!.ArrayMicrophones);
        Assert.NotNull(factory.LastRequest);
        Assert.Empty(factory.LastRequest!.Routing.ArrayChannels);
    }

    [Fact]
    public void AChannelAlreadyInUseIsRefusedRatherThanDropped()
    {
        var factory = new FakeAudioSessionFactory();

        Assert.Throws<InvalidOperationException>(
            () => CreateSweep(factory, arrayChannels: [1]));
        Assert.Throws<InvalidOperationException>(
            () => CreateSweep(factory, arrayChannels: [2, 2]));
    }
}
