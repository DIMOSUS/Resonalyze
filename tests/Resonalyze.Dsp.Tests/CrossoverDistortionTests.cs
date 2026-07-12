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

    private static readonly List<SignalPoint> TweeterCurve = BandCurve(1_000, 20_000);

    private static CrossoverEdge MidTweeterHighPass(IReadOnlyList<SignalPoint>? tweeterDistortion)
    {
        var channels = new List<AutoSetupSource>
        {
            new(BandCurve(40, 800), DriverType.Woofer),
            new(BandCurve(200, 4_000), DriverType.Midrange),
            new(TweeterCurve, DriverType.Tweeter, null, tweeterDistortion)
        };
        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            channels, CrossoverAutoSetupOptions.Default(SampleRate));
        return proposals[2].HighPassEdge!.Value;
    }

    // The resonance-protection invariant: whatever (frequency, slope) the optimizer
    // chose, the tweeter's high-pass crosses no lower than Fs·2^(target/slope), with
    // Fs estimated from its own measured roll-off (floored at 1.2 kHz). A lower
    // crossover is only ever reached by a steeper slope.
    private static void AssertProtectsResonance(CrossoverEdge highPass)
    {
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(TweeterCurve).LowHz);
        double floor = CrossoverAutoSetup.TweeterMinCrossoverHz(
            resonance, highPass.SlopeDbPerOctave);
        Assert.True(
            highPass.FrequencyHz >= floor - 1,
            $"tweeter at {highPass.FrequencyHz:0} Hz / {highPass.SlopeDbPerOctave} dB-oct " +
            $"is below its resonance floor {floor:0} Hz.");
    }

    [Fact]
    public void Propose_TweeterCrossoverProtectsItsResonance()
    {
        // No distortion supplied: the crossover still respects the resonance bound,
        // so a low handover is paired with a slope steep enough to protect Fs.
        AssertProtectsResonance(MidTweeterHighPass(tweeterDistortion: null));
    }

    [Fact]
    public void Propose_CleanDistortionDoesNotLowerTheTweeterCrossover()
    {
        // A distortion knee BELOW the resonance floor (clean down to 1.1 kHz) is
        // protective-only: it can never pull the tweeter's handover below where the
        // resonance bound already holds it, so the result is identical with or
        // without the clean curve.
        CrossoverEdge without = MidTweeterHighPass(tweeterDistortion: null);
        CrossoverEdge with = MidTweeterHighPass(DistortionDirtyBelow(1_100));

        Assert.Equal(without.FrequencyHz, with.FrequencyHz, 0);
        Assert.Equal(without.SlopeDbPerOctave, with.SlopeDbPerOctave);
        AssertProtectsResonance(with);
    }

    [Fact]
    public void Propose_DistortionRaisesTheFloorForATweeterDirtyLow()
    {
        // A tweeter that measures dirty up to 2.5 kHz — above its resonance floor —
        // IS protected further: its handover is raised to keep it out of the
        // distorting region.
        CrossoverEdge without = MidTweeterHighPass(tweeterDistortion: null);
        CrossoverEdge with = MidTweeterHighPass(DistortionDirtyBelow(2_500));

        Assert.True(
            with.FrequencyHz >= 2_400,
            $"a dirty-low tweeter should be held higher, was {with.FrequencyHz:0} Hz.");
        Assert.True(
            with.FrequencyHz > without.FrequencyHz,
            "distortion should raise the floor for a dirty tweeter.");
    }
}
