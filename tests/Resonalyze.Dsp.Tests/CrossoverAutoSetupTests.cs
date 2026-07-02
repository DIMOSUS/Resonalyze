namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverAutoSetupTests
{
    // A synthetic driver curve on a log grid: flat at `levelDb` inside the band,
    // rolling off at 24 dB/octave beyond both edges — the shape the band and
    // crossover analysis has to read.
    private static List<SignalPoint> BandCurve(
        double lowHz,
        double highHz,
        double levelDb)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = levelDb;
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    [Fact]
    public void EstimateBand_ReadsEdgesLevelAndType()
    {
        DriverBandEstimate woofer = CrossoverAutoSetup.EstimateBand(
            BandCurve(35, 600, -12));
        Assert.Equal(DriverType.Woofer, woofer.SuggestedType);
        Assert.InRange(woofer.LowHz, 20, 60);
        Assert.InRange(woofer.HighHz, 500, 900);
        Assert.InRange(woofer.LevelDb, -13, -11);

        DriverBandEstimate midrange = CrossoverAutoSetup.EstimateBand(
            BandCurve(150, 4_000, 0));
        Assert.Equal(DriverType.Midrange, midrange.SuggestedType);

        DriverBandEstimate tweeter = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_800, 20_000, -3));
        Assert.Equal(DriverType.Tweeter, tweeter.SuggestedType);
        Assert.InRange(tweeter.LowHz, 1_200, 2_000);
    }

    [Fact]
    public void Propose_TwoWay_SplitsAtTheCurveIntersection()
    {
        // Equal levels: the aligned curves cross where the woofer's roll-off
        // meets the tweeter's — between the band edges.
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose([woofer, tweeter]);

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[1].Kind);
        Assert.Null(proposals[0].HighPassEdge);
        Assert.Null(proposals[1].LowPassEdge);

        double lowPassHz = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double highPassHz = proposals[1].HighPassEdge!.Value.FrequencyHz;
        Assert.Equal(lowPassHz, highPassHz);
        Assert.InRange(lowPassHz, 1_000, 2_000);
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley,
            proposals[0].LowPassEdge!.Value.Family);
        Assert.Equal(24, proposals[0].LowPassEdge!.Value.SlopeDbPerOctave);
    }

    [Fact]
    public void Propose_GainsAreCutOnly_AndLevelToTheQuietestChannel()
    {
        // The tweeter plays 6 dB louder; it gets the cut, the woofer stays put.
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, -6), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose([woofer, tweeter]);

        Assert.Equal(0.0, proposals[0].GainDb, 1.0);
        Assert.InRange(proposals[1].GainDb, -7.5, -4.5);
        Assert.True(proposals.All(proposal => proposal.GainDb <= 0));
    }

    [Fact]
    public void Propose_ThreeWay_GivesTheMiddleChannelABandPass()
    {
        // Input deliberately out of band order; results come back in input order.
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose([tweeter, woofer, midrange]);

        Assert.Equal(CrossoverKind.HighPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.LowPass, proposals[1].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[2].Kind);

        double lowSplit = proposals[1].LowPassEdge!.Value.FrequencyHz;
        double highSplit = proposals[0].HighPassEdge!.Value.FrequencyHz;
        Assert.Equal(lowSplit, proposals[2].HighPassEdge!.Value.FrequencyHz);
        Assert.Equal(highSplit, proposals[2].LowPassEdge!.Value.FrequencyHz);
        Assert.True(lowSplit < highSplit);
    }

    [Fact]
    public void Propose_ClampsTheCrossoverAwayFromTheTweeterEdge()
    {
        // The tweeter barely reaches down; the split must stay at least an
        // octave above its low edge even though the level intersection sits lower.
        var woofer = new AutoSetupSource(BandCurve(40, 8_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_500, 20_000, 6), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose([woofer, tweeter]);

        DriverBandEstimate tweeterBand = CrossoverAutoSetup.EstimateBand(
            tweeter.MagnitudeDb);
        Assert.True(
            proposals[1].HighPassEdge!.Value.FrequencyHz >= tweeterBand.LowHz * 2 - 1,
            "The crossover must keep an octave of margin above the tweeter's low edge.");
    }

    [Fact]
    public void Propose_RejectsDuplicateTypesAndTooFewChannels()
    {
        var a = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var b = new AutoSetupSource(BandCurve(50, 2_500, 0), DriverType.Woofer);

        Assert.Throws<ArgumentException>(() => CrossoverAutoSetup.Propose([a, b]));
        Assert.Throws<ArgumentException>(() => CrossoverAutoSetup.Propose([a]));
    }
}
