using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqDoubleSkirtCheckTests
{
    private const int Rate = 48_000;

    // Issue #218: REW exports of one ATF target, one cut with a 200/2900 Hz crossover and one left whole.
    private static readonly (double Hz, double Db)[] MidrangeCut =
    [
        (10, -94.079), (20, -69.997), (40, -46.927), (63, -33.216), (100, -19.605), (125, -13.559),
        (160, -7.733), (200, -4.019), (250, -1.973), (315, -0.309), (400, 0.470), (630, 0.893),
        (1_000, 0.865), (1_600, 0.227), (2_000, -1.752), (2_500, -3.810), (3_150, -7.586),
        (4_000, -13.364), (5_000, -20.040), (6_300, -27.709), (8_000, -36.091), (10_000, -44.233),
        (16_000, -63.629), (25_000, -84.979)
    ];

    private static readonly (double Hz, double Db)[] WholeTarget =
    [
        (25, 10), (40, 9), (63, 7), (100, 5), (160, 3), (200, 2), (250, 1), (1_600, 1), (2_000, 0),
        (10_000, 0), (12_500, -1), (20_000, -1)
    ];

    private static readonly EqTargetSlope Midrange = new(
        new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_900, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24)),
        null);

    [Fact]
    public void ATargetCutWithTheSameCrossover_RepeatsBothSkirts()
    {
        IReadOnlyList<double> skirts = EqDoubleSkirtCheck.RepeatedSkirts(Spec(MidrangeCut), Midrange, Rate);

        Assert.Equal(2, skirts.Count);
        Assert.InRange(skirts[0], 200, 300);
        Assert.InRange(skirts[1], 2_000, 2_900);
        Assert.Contains(
            "Crossover in target",
            EqDoubleSkirtCheck.Warning(Spec(MidrangeCut), Midrange, crossoverInTarget: true, Rate));
    }

    [Fact]
    public void TheWholeTarget_RepeatsNothing()
    {
        Assert.Empty(EqDoubleSkirtCheck.RepeatedSkirts(Spec(WholeTarget), Midrange, Rate));
    }

    [Fact]
    public void TheSteepestPresetShelf_IsNotReadAsASkirt()
    {
        // A sub low-passed inside the Car Bass shelf: the shelf falls into the stopband, but not like a crossover.
        var sub = new EqTargetSlope(
            new CrossoverSpec(
                CrossoverKind.LowPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24)),
            null);
        TargetCurveSpec carBass = TargetCurveSpec.FromPreset(TargetPreset.CarBass);
        var points = new List<(double Hz, double Db)>();
        for (double hz = 10; hz <= 24_000; hz *= 1.05)
        {
            points.Add((hz, carBass.Evaluate(hz)));
        }

        Assert.Empty(EqDoubleSkirtCheck.RepeatedSkirts(Spec([.. points]), sub, Rate));
    }

    [Fact]
    public void WithoutTheCrossoverInTheTarget_ThereIsNothingToWarnOf()
    {
        Assert.Null(EqDoubleSkirtCheck.Warning(Spec(MidrangeCut), Midrange, crossoverInTarget: false, Rate));
        Assert.Null(EqDoubleSkirtCheck.Warning(Spec(MidrangeCut), slope: null, crossoverInTarget: true, Rate));
        Assert.Null(EqDoubleSkirtCheck.Warning(
            TargetCurveSpec.FromPreset(TargetPreset.Car), Midrange, crossoverInTarget: true, Rate));
    }

    [Fact]
    public void AFirCrossoverKernel_IsReadLikeAnIirOne()
    {
        // A windowed-sinc skirt falls 15 dB in a tenth of an octave, over which the LR24 cut in the file barely moves.
        var design = new FirCrossoverDesign(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_900, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24),
            FirCrossoverMethod.WindowedSinc,
            FirWindow.Kaiser,
            8,
            4_095,
            Rate);
        var fir = new EqTargetSlope(null, design.Build());

        Assert.Equal(2, EqDoubleSkirtCheck.RepeatedSkirts(Spec(MidrangeCut), fir, Rate).Count);
        Assert.Empty(EqDoubleSkirtCheck.RepeatedSkirts(Spec(WholeTarget), fir, Rate));
    }

    [Fact]
    public void TheCache_AnswersAgainOnlyWhenAnInputChanges()
    {
        var cache = new EqDoubleSkirtCheck.Cache();
        TargetCurveSpec cut = Spec(MidrangeCut);

        string? first = cache.Warning(cut, Midrange, crossoverInTarget: true, Rate);
        Assert.NotNull(first);
        Assert.Same(first, cache.Warning(cut, Midrange with { }, crossoverInTarget: true, Rate));
        Assert.Null(cache.Warning(cut, Midrange, crossoverInTarget: false, Rate));
        Assert.Null(cache.Warning(Spec(WholeTarget), Midrange, crossoverInTarget: true, Rate));
        Assert.Equal(first, cache.Warning(cut, Midrange, crossoverInTarget: true, Rate));
    }

    [Fact]
    public void TheLevelOffer_ReadsTheTargetAsDrawn()
    {
        // A file peaking at +40 dB at 20 Hz, on a tweeter's channel: the high-pass keeps that peak off the plot.
        var session = new EqWizardSession();
        session.Load(new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch T",
            Description = "test",
            PreviewChain = DspChannelChain.Identity,
            TargetCrossover = new CrossoverSpec(
                CrossoverKind.HighPass,
                HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24))
        });
        session.SetTarget(session.Target with
        {
            Spec = Spec([(20, 40.0), (200, 0.0), (20_000, 0.0)])
        });

        Assert.Equal(0, EqWizardRender.TargetShapePeakDb(session)!.Value, 0.5);
        session.SetCrossoverInTarget(false);
        Assert.Equal(40, EqWizardRender.TargetShapePeakDb(session)!.Value, 1e-9);
    }

    private static TargetCurveSpec Spec((double Hz, double Db)[] points) =>
        TargetCurveSpec.FromPreset(TargetPreset.Flat) with
        {
            Imported = ImportedTargetCurve.FromPoints(
                "target.txt",
                points.Select(point => new OverlayPoint(point.Hz, point.Db)))
        };
}
