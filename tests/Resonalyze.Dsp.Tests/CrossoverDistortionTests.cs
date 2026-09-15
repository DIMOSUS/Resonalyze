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
