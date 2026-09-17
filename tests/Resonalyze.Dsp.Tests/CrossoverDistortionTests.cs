namespace Resonalyze.Dsp.Tests;

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

    private static List<SignalPoint> DistortionDirtyBelow(double kneeHz) =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, f < kneeHz ? -8.0 : -45.0))
            .ToList();

    private static List<SignalPoint> DistortionDirtyAbove(double breakupHz) =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, f > breakupHz ? -8.0 : -45.0))
            .ToList();

    private static List<SignalPoint> DistortionAllDirty() =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, -8.0))
            .ToList();

    private static List<SignalPoint> DistortionAllMasked() =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 512)
            .Select(f => new SignalPoint(f, double.NaN))
            .ToList();

    private static CrossoverAutoSetupOptions Options(
        IReadOnlyList<JunctionSearchWindow>? windows = null) =>
        new(
            [CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth],
            20,
            20_000,
            IndependentSlopes: false,
            SampleRate,
            SampleRate,
            SubElevationDb: null,
            windows);

    [Fact]
    public void AFloorAndACapThatCrossEachOther_LeaveTheFloorStanding()
    {
        // Both bounds sit INSIDE the window the drivers leave, so neither one crosses it on its own — they cross
        // each other. That is the case the policy is about: overexcursion is damage, breakup is a worse sound.
        List<SignalPoint> wooferCurve = BandCurve(40, 4_000);
        List<SignalPoint> tweeterCurve = BandCurve(1_000, 20_000);
        var channels = new AutoSetupSource[]
        {
            new(wooferCurve, DriverType.Woofer, DistortionDb: DistortionDirtyAbove(2_500)),
            new(tweeterCurve, DriverType.Tweeter, DistortionDb: DistortionDirtyBelow(3_000))
        };

        double cap = CrossoverAutoSetup.EstimateBand(
            wooferCurve, coherence: null, DistortionDirtyAbove(2_500)).DistortionHighHz;
        double knee = CrossoverAutoSetup.EstimateBand(
            tweeterCurve, coherence: null, DistortionDirtyBelow(3_000)).DistortionLowHz;
        Assert.True(
            knee > cap,
            $"This fixture needs the knee ({knee:0} Hz) above the cap ({cap:0} Hz) to test anything.");

        JunctionWindowResolution window = CrossoverAutoSetup.ResolveJunctionWindow(
            channels, 0, Options());

        Assert.True(
            window.LowHz >= knee - 1,
            $"The window starts at {window.LowHz:0} Hz, under the {knee:0} Hz knee: the cap won and the " +
            "bound that protects the tweeter was the one thrown away.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASplitCorner_StaysInsideTheDistortionCleanBand(bool independentSlopes)
    {
        // The window places the CORNER, and the knee and the breakup onset are bounds on it. An offset moves the
        // edges off the corner — 0.917 of it for the high-pass at the widest, 1.091 for the low-pass — so the
        // corner can be clean while the edge that actually filters the driver is not.
        List<SignalPoint> wooferCurve = BandCurve(40, 5_000);
        List<SignalPoint> tweeterCurve = BandCurve(900, 20_000);
        List<SignalPoint> breakup = DistortionDirtyAbove(4_000);
        List<SignalPoint> dirty = DistortionDirtyBelow(2_400);
        var channels = new AutoSetupSource[]
        {
            new(wooferCurve, DriverType.Woofer, DistortionDb: breakup),
            new(tweeterCurve, DriverType.Tweeter, DistortionDb: dirty)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            channels,
            Options([new JunctionSearchWindow(AllowSplitCorners: true)]) with
            {
                IndependentSlopes = independentSlopes
            });

        double lowPass = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double highPass = proposals[1].HighPassEdge!.Value.FrequencyHz;
        double knee = CrossoverAutoSetup.EstimateBand(
            tweeterCurve, coherence: null, dirty).DistortionLowHz;
        double cap = CrossoverAutoSetup.EstimateBand(
            wooferCurve, coherence: null, breakup).DistortionHighHz;
        Assert.True(
            highPass >= knee - 1,
            $"The tweeter is high-passed at {highPass:0} Hz, below its {knee:0} Hz distortion knee.");
        Assert.True(
            lowPass <= cap + 1,
            $"The woofer is low-passed at {lowPass:0} Hz, past its {cap:0} Hz breakup onset.");
    }

    [Fact]
    public void AFloorThatOverrulesTheClassWindow_StillLeavesTheCapStanding()
    {
        // The floor empties the window on its own, so the window has to move — but the cap it moves past is not in
        // conflict with the floor at all: 4.5-5.9 kHz satisfies both. Only a safety bound that CANNOT be met
        // alongside the other may be dropped, and "this bound is what emptied the window" is a different question.
        List<SignalPoint> midrangeCurve = BandCurve(200, 8_000);
        List<SignalPoint> tweeterCurve = BandCurve(1_700, 20_000);
        List<SignalPoint> breakup = DistortionDirtyAbove(6_000);
        List<SignalPoint> dirty = DistortionDirtyBelow(4_500);
        var channels = new AutoSetupSource[]
        {
            new(midrangeCurve, DriverType.Midrange, DistortionDb: breakup),
            new(tweeterCurve, DriverType.Tweeter, DistortionDb: dirty)
        };

        double knee = CrossoverAutoSetup.EstimateBand(
            tweeterCurve, coherence: null, dirty).DistortionLowHz;
        double cap = CrossoverAutoSetup.EstimateBand(
            midrangeCurve, coherence: null, breakup).DistortionHighHz;
        Assert.True(
            knee < cap,
            $"This fixture needs the knee ({knee:0} Hz) UNDER the cap ({cap:0} Hz): the two must be " +
            "satisfiable together for the test to mean anything.");

        JunctionWindowResolution window = CrossoverAutoSetup.ResolveJunctionWindow(
            channels, 0, Options());

        Assert.True(
            window.LowHz >= knee - 1,
            $"The window starts at {window.LowHz:0} Hz, under the {knee:0} Hz knee.");
        Assert.True(
            window.HighHz <= cap + 1,
            $"The window runs to {window.HighHz:0} Hz, past the {cap:0} Hz breakup onset. The floor " +
            "moved the window, and the cap was dropped along with it although it never conflicted.");
    }

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
    public void EstimateBand_WithoutDistortion_IsUnavailableWithNaNEdges()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(BandCurve(1_000, 20_000));
        Assert.Equal(DistortionBandStatus.Unavailable, band.DistortionStatus);
        Assert.True(double.IsNaN(band.DistortionLowHz));
        Assert.True(double.IsNaN(band.DistortionHighHz));
    }

    [Fact]
    public void EstimateBand_CleanSubBand_IsCleanBandFound()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_000, 20_000), coherence: null, DistortionDirtyBelow(1_500));
        Assert.Equal(DistortionBandStatus.CleanBandFound, band.DistortionStatus);
    }

    // 'Dirty everywhere' is NoCleanBand with finite protective edges, not the NaN 'no data' result.
    [Fact]
    public void EstimateBand_DirtyEverywhere_IsNoCleanBandNotUnavailable()
    {
        DriverBandEstimate dirty = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_000, 20_000), coherence: null, DistortionAllDirty());
        DriverBandEstimate none = CrossoverAutoSetup.EstimateBand(BandCurve(1_000, 20_000));

        Assert.Equal(DistortionBandStatus.NoCleanBand, dirty.DistortionStatus);
        Assert.False(double.IsNaN(dirty.DistortionLowHz));
        Assert.False(double.IsNaN(dirty.DistortionHighHz));
        Assert.True(double.IsNaN(none.DistortionLowHz));

        // The floor sits at the TOP of the dirt, above a dirty-low tweeter's knee.
        DriverBandEstimate dirtyLow = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_000, 20_000), coherence: null, DistortionDirtyBelow(2_500));
        Assert.True(dirty.DistortionLowHz > dirtyLow.DistortionLowHz);
    }

    // All masked (NaN) is Unreliable and falls back like 'no data'.
    [Fact]
    public void EstimateBand_AllMasked_IsUnreliableWithNaNEdges()
    {
        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(
            BandCurve(1_000, 20_000), coherence: null, DistortionAllMasked());
        Assert.Equal(DistortionBandStatus.Unreliable, band.DistortionStatus);
        Assert.True(double.IsNaN(band.DistortionLowHz));
        Assert.True(double.IsNaN(band.DistortionHighHz));
    }

    [Fact]
    public void Propose_DirtyEverywhereTweeter_IsHeldHigherThanDirtyLow()
    {
        CrossoverEdge without = MidTweeterHighPass(tweeterDistortion: null);
        CrossoverEdge dirtyLow = MidTweeterHighPass(DistortionDirtyBelow(2_500));
        CrossoverEdge dirtyAll = MidTweeterHighPass(DistortionAllDirty());

        Assert.True(
            dirtyAll.FrequencyHz >= dirtyLow.FrequencyHz,
            $"dirty-everywhere tweeter at {dirtyAll.FrequencyHz:0} Hz must be held no " +
            $"lower than dirty-low at {dirtyLow.FrequencyHz:0} Hz.");
        Assert.True(
            dirtyAll.FrequencyHz > without.FrequencyHz,
            "dirty-everywhere must not silently match the no-distortion fallback.");
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
            channels, CrossoverAutoSetupOptions.Default(SampleRate, SampleRate));
        return proposals[2].HighPassEdge!.Value;
    }

    // The tweeter high-pass never crosses below Fs·2^(target/slope), Fs from its measured roll-off (floored at 1.2 kHz).
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
        AssertProtectsResonance(MidTweeterHighPass(tweeterDistortion: null));
    }

    [Fact]
    public void Propose_CleanDistortionDoesNotLowerTheTweeterCrossover()
    {
        // A knee below the resonance floor is protective-only: identical result with or without it.
        CrossoverEdge without = MidTweeterHighPass(tweeterDistortion: null);
        CrossoverEdge with = MidTweeterHighPass(DistortionDirtyBelow(1_100));

        Assert.Equal(without.FrequencyHz, with.FrequencyHz, 0);
        Assert.Equal(without.SlopeDbPerOctave, with.SlopeDbPerOctave);
        AssertProtectsResonance(with);
    }

    [Fact]
    public void Propose_DistortionRaisesTheFloorForATweeterDirtyLow()
    {
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
