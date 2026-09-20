using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqTargetCrossoverTests
{
    private const int Rate = 48_000;

    private static readonly CrossoverSpec BandPass = new(
        CrossoverKind.BandPass,
        new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
        new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24));

    [Fact]
    public void TheShape_IsFlatInThePassbandAndFallsDownEachSkirt()
    {
        // Not exactly 0: two 24 dB/oct skirts 2.6 octaves apart leave the middle of the band half a dB down, which the
        // measured curve through the same chain carries too, so target and source agree there.
        Assert.Equal(0, EqTargetCrossover.ShapeDb(BandPass, 200, Rate), 0.6);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 200, Rate) <= 0);
        // Linkwitz-Riley crosses 6 dB down at its corner, and a 24 dB/oct skirt an octave out is far below.
        Assert.Equal(-6, EqTargetCrossover.ShapeDb(BandPass, 80, Rate), 0.5);
        Assert.Equal(-6, EqTargetCrossover.ShapeDb(BandPass, 500, Rate), 0.5);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 40, Rate) < -20);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 1_000, Rate) < -20);
        // Never a target of minus infinity, and never a lift.
        Assert.InRange(EqTargetCrossover.ShapeDb(BandPass, 20, Rate), -40, 0);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 0, Rate) <= 0);
    }

    [Fact]
    public void TheSlopeWindow_ReachesWhereTheSkirtHasFallenEighteenDb()
    {
        (double minHz, double maxHz) = EqTargetCrossover.SlopeWindow(
            BandPass, 80, 500, Rate, measuredLowHz: null, measuredHighHz: null);

        Assert.InRange(minHz, 20, 79);
        Assert.InRange(maxHz, 501, 20_000);
        Assert.Equal(-EqTargetCrossover.SlopeWindowFallDb, EqTargetCrossover.ShapeDb(BandPass, minHz, Rate), 1.5);
        Assert.Equal(-EqTargetCrossover.SlopeWindowFallDb, EqTargetCrossover.ShapeDb(BandPass, maxHz, Rate), 1.5);
    }

    [Fact]
    public void TheSlopeWindow_StopsAtTheMeasuredBand()
    {
        (double minHz, double maxHz) = EqTargetCrossover.SlopeWindow(
            BandPass, 80, 500, Rate, measuredLowHz: 70, measuredHighHz: 600);

        Assert.Equal(70, minHz);
        Assert.Equal(600, maxHz);
    }

    [Fact]
    public void TheNoBoostBands_CoverTheSkirtsAndLeaveThePassbandAlone()
    {
        IReadOnlyList<EqNoBoostBand> bands = EqTargetCrossover.NoBoostBands(BandPass, 40, 1_000, Rate);

        Assert.Equal(2, bands.Count);
        Assert.True(Forbidden(bands, 45), "the low skirt may not be lifted.");
        Assert.True(Forbidden(bands, 900), "the high skirt may not be lifted.");
        Assert.False(Forbidden(bands, 200), "the passband is the fit's business.");
        // The band reaches to the corner, where the skirt is exactly 6 dB down, and no further in.
        Assert.False(Forbidden(bands, 120), "a hundred and twenty is passband.");
        Assert.True(Forbidden(bands, 70), "below the corner the skirt is the filter's doing.");
    }

    [Fact]
    public void WithoutACrossoverBehindTheSource_ThereIsNothingToFollow()
    {
        Assert.Null(EqTargetCrossover.Of(null));
        Assert.Null(EqTargetCrossover.Of(Source(null)));
        Assert.Null(EqTargetCrossover.Of(Source(DspChannelChain.Identity)));
        Assert.Null(EqTargetCrossover.Of(
            Source(DspChannelChain.Identity with { Crossover = CrossoverSpec.Off })));
        Assert.Equal(
            BandPass,
            EqTargetCrossover.Of(Source(DspChannelChain.Identity with { Crossover = BandPass })));
    }

    private static EqWizardCurveSource Source(DspChannelChain? chain) => new()
    {
        Kind = EqWizardSourceKind.VirtualDspChannel,
        DisplayName = "Ch A",
        Description = "test",
        PreviewChain = chain
    };

    private static bool Forbidden(IReadOnlyList<EqNoBoostBand> bands, double hz) =>
        bands.Any(band => hz >= band.LowHz && hz <= band.HighHz);
}
