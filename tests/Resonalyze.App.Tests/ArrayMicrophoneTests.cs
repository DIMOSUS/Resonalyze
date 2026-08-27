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
    public async Task AFailedMicrophoneDropsFromThatRunAndNotFromTheMeasurement()
    {
        int run = 0;
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) =>
                {
                    run++;
                    // Driven past full scale in the first run only: the sweep
                    // itself peaks at 0.5, so 2.5 puts the channel over.
                    return Task.FromResult(SyntheticCapture.WithArray(
                        s, tail, run == 1 ? 2.5f : 0.125f));
                }));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, runs: 2, arrayChannels: [2]);

        bool success = await measurement.RunAsync();

        Assert.True(success, measurement.LastError?.ToString());
        // The run itself stands: the impulse response owes nothing to an array
        // microphone three seats away.
        Assert.Equal(2, measurement.AcceptedAverageRunCount);
        Assert.Equal(2, measurement.ArrayMicrophones[0].AcceptedRuns);
        Assert.Equal(1, measurement.ArrayMicrophones[1].AcceptedRuns);
        Assert.Contains("clipped", string.Join(" ", measurement.ArrayMicrophones[1].Issues));
    }

    [Fact]
    public async Task AMicrophoneThatNeverWorkedIsLeftOutAndSaidSoOutLoud()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.125f, 0.0f))));
        using ExpSweepMeasurement measurement = CreateSweep(
            factory, arrayChannels: [2, 3]);

        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        // Two microphones survive: the measurement one and the working array one.
        Assert.Equal(2, measurement.ArrayMicrophones.Count);
        Assert.DoesNotContain(
            measurement.ArrayMicrophones,
            microphone => microphone.ChannelOffset == 3);

        // Silently averaging one position fewer than the user set up is exactly
        // what the notice exists to prevent.
        SweepRunQualityReport? report = measurement.QualityReport;
        Assert.NotNull(report);
        Assert.True(report!.IsDegraded);
        SweepArrayMicrophoneOutcome outcome = Assert.Single(report!.ArrayMicrophones);
        Assert.Equal(3, outcome.ChannelOffset);
        Assert.Equal(0, outcome.AcceptedRuns);
        Assert.Contains("input 4", report!.Describe());
        Assert.Contains("left out of the spatial average", report.Describe());
    }

    [Fact]
    public async Task ACleanArrayKeepsTheNoticeQuiet()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(SyntheticCapture.WithArray(s, tail, 0.125f))));
        using ExpSweepMeasurement measurement = CreateSweep(factory, arrayChannels: [2]);

        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        SweepRunQualityReport? report = measurement.QualityReport;
        Assert.NotNull(report);
        Assert.Empty(report!.ArrayMicrophones);
        Assert.False(report!.IsDegraded);
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
