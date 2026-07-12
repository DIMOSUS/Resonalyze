namespace Resonalyze.Dsp.Tests;

// Pins the distortion-aware crossover bounds: the band estimate reads each driver's
// distortion-clean sub-band, a tweeter's low handover then follows its measured
// distortion knee instead of the fixed class floor, and no driver is crossed up
// into its breakup region.
public sealed class CrossoverDistortionTests
{
    private const double SampleRate = 48_000;

    private static List<SignalPoint> BandCurve(double lowHz, double highHz, double levelDb = 0)
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

    // THD (dB) that is dirty below the knee and clean above — a tweeter's excursion
    // limit.
    private static List<SignalPoint> DistortionDirtyBelow(double kneeHz) =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, f < kneeHz ? -8.0 : -45.0))
            .ToList();

    // THD clean below the breakup onset and dirty above — a woofer's cone breakup.
    private static List<SignalPoint> DistortionDirtyAbove(double breakupHz) =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, f > breakupHz ? -8.0 : -45.0))
            .ToList();

    [Fact]
    public void EstimateBand_ReadsTheTweeterDistortionKnee()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_000, 20_000), coherence: null, DistortionDirtyBelow(1_500));
        Assert.InRange(band.DistortionLowHz, 1_350, 1_700);
    }

    [Fact]
    public void EstimateBand_ReadsTheWooferBreakup()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(
            BandCurve(40, 2_000), coherence: null, DistortionDirtyAbove(800));
        Assert.InRange(band.DistortionHighHz, 650, 950);
    }

    [Fact]
    public void EstimateBand_WithoutDistortion_LeavesTheCleanEdgesNaN()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(BandCurve(1_000, 20_000));
        Assert.True(double.IsNaN(band.DistortionLowHz));
        Assert.True(double.IsNaN(band.DistortionHighHz));
    }

    private static double MidTweeterCrossover(bool withDistortion)
    {
        var thd = DistortionDirtyBelow(1_100);
        var channels = new List<AutoSetupSource>
        {
            new(BandCurve(40, 800), DriverType.Woofer),
            new(BandCurve(200, 4_000), DriverType.Midrange),
            new(BandCurve(1_000, 20_000), DriverType.Tweeter, null, withDistortion ? thd : null)
        };
        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            channels, CrossoverAutoSetupOptions.Default(SampleRate));
        return proposals[2].HighPassEdge!.Value.FrequencyHz;
    }

    [Fact]
    public void Propose_TweeterCrossesBelowTheFixedFloorWhenDistortionAllows()
    {
        double without = MidTweeterCrossover(withDistortion: false);
        double with = MidTweeterCrossover(withDistortion: true);

        // Without distortion the tweeter is pinned at the fixed 1.7 kHz class floor;
        // with a clean measured knee at 1.1 kHz it may cross lower.
        Assert.True(without >= 1_700 - 1, $"expected the fixed floor, was {without:0} Hz.");
        Assert.True(with < 1_700, $"distortion should let the tweeter cross lower, was {with:0} Hz.");
        Assert.True(with < without, "the distortion-aware crossover should sit below the fixed one.");
    }
}
