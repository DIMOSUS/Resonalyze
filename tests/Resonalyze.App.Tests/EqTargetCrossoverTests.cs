using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqTargetCrossoverTests
{
    private const int Rate = 48_000;

    private static readonly CrossoverSpec BandPassSpec = new(
        CrossoverKind.BandPass,
        new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
        new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24));

    private static readonly EqTargetSlope BandPass = new(BandPassSpec, null);

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
        // No floor: another octave down the skirt is another 24 dB down.
        Assert.Equal(
            EqTargetCrossover.ShapeDb(BandPass, 20, Rate) - 24,
            EqTargetCrossover.ShapeDb(BandPass, 10, Rate),
            1.0);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 10, Rate) < -60);
        Assert.True(EqTargetCrossover.ShapeDb(BandPass, 0, Rate) < -120);
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
    public void AWindowThatIsSkirtFromEdgeToEdge_RefusesBoostsThroughout()
    {
        // A band-pass crossed past itself: nowhere in the window does the pair rise within 6 dB of its plateau.
        EqTargetSlope crossed = new(
            new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24),
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24)),
            null);

        IReadOnlyList<EqNoBoostBand> bands = EqTargetCrossover.NoBoostBands(crossed, 300, 1_500, Rate);

        Assert.True(EqTargetCrossover.ShapeDb(crossed, 600, Rate) < -EqTargetCrossover.NoBoostFallDb);
        Assert.True(Forbidden(bands, 300), "the low edge of the window is skirt.");
        Assert.True(Forbidden(bands, 600), "so is its middle.");
        Assert.True(Forbidden(bands, 1_500), "and its high edge.");
    }

    [Fact]
    public void TheShapeFollowsTheChannelsOwnCrossover_IncludingAFirCrossoversCorners()
    {
        Assert.Null(EqTargetCrossover.Of(null));
        Assert.Null(EqTargetCrossover.Of(Source(null)));
        Assert.Null(EqTargetCrossover.Of(Source(CrossoverSpec.Off)));
        Assert.Equal(BandPass, EqTargetCrossover.Of(Source(BandPassSpec)));

        // A designed FIR crossover answers with its KERNEL: a windowed sinc's slope is its window and length, which
        // the corners the design carries do not describe, so reading them would draw an IIR that does not exist.
        var sinc = new FirCrossoverDesign(
            CrossoverKind.HighPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24),
            FirCrossoverMethod.WindowedSinc,
            FirWindow.Kaiser,
            8,
            1_023,
            Rate);
        var settings = new VirtualCrossoverChannelSettings { Fir = sinc.Build(), FirDesign = sinc };
        // As the handoff carries it: with the IIR crossover off, only the kernel travels — EffectiveCrossover stands
        // in for the design there, and sending it too would count the same filter twice.
        EqWizardCurveSource source = Source(null) with { TargetCrossoverFir = settings.Fir };

        EqTargetSlope slope = Assert.IsType<EqTargetSlope>(EqTargetCrossover.Of(source));
        Assert.Null(slope.Crossover);
        Assert.NotNull(slope.Fir);
        // The kernel's own brick wall is far steeper than the 24 dB/oct the corners claim.
        double kernel = EqTargetCrossover.ShapeDb(slope, 1_400, Rate);
        double pretendIir = EqTargetCrossover.ShapeDb(new EqTargetSlope(settings.EffectiveCrossover, null), 1_400, Rate);
        Assert.True(
            kernel < pretendIir - 3,
            $"the kernel reads {kernel:0.0} dB where the corners would claim {pretendIir:0.0} dB.");
    }

    [Fact]
    public void ACrossoverOutsideTheMeasuredBand_LeavesAnOrderedWindow()
    {
        // A band-pass at 80..500 Hz on a record that starts at 1 kHz: nothing to widen into, and no overlap to keep.
        // The window stays ordered and the fit refuses it for want of data (EqWizardFit.NoMeasuredDataRefusal).
        (double minHz, double maxHz) = EqTargetCrossover.SlopeWindow(
            BandPass, 80, 500, Rate, measuredLowHz: 1_000, measuredHighHz: 4_000);

        Assert.True(minHz < maxHz, $"window {minHz}..{maxHz} is inverted.");
        Assert.Equal(80, minHz);
        Assert.Equal(500, maxHz);
    }

    [Fact]
    public void APassbandOnlyHalfMeasured_KeepsTheHalfThatWasMeasured()
    {
        // The record starts inside the passband: the window is what both cover, not the corners.
        (double minHz, double maxHz) = EqTargetCrossover.SlopeWindow(
            BandPass, 80, 500, Rate, measuredLowHz: 300, measuredHighHz: 320);

        Assert.Equal(300, minHz);
        Assert.Equal(320, maxHz);
    }

    [Fact]
    public void AChannelRunningBothCrossovers_FollowsThemInSeries()
    {
        // Legitimate though rare: a FIR high-pass and an IIR low-pass both filter the channel.
        var iirLowPass = new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 5_000, 24));
        var design = new FirCrossoverDesign(
            CrossoverKind.HighPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 24),
            FirCrossoverMethod.WindowedSinc,
            FirWindow.Kaiser,
            8,
            1_023,
            Rate);
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 5_000, 24),
            Fir = design.Build(),
            FirDesign = design
        };
        EqWizardCurveSource source = Source(settings.EffectiveCrossover) with
        {
            TargetCrossoverFir = settings.Fir
        };

        EqTargetSlope slope = Assert.IsType<EqTargetSlope>(EqTargetCrossover.Of(source));

        Assert.NotNull(slope.Crossover);
        Assert.NotNull(slope.Fir);
        // Flat between the two, and down on BOTH skirts — the upper one is the IIR's, which used to vanish.
        Assert.Equal(0, EqTargetCrossover.ShapeDb(slope, 2_000, Rate), 0.6);
        Assert.True(EqTargetCrossover.ShapeDb(slope, 250, Rate) < -20, "the FIR's skirt should pull the target down.");
        Assert.True(EqTargetCrossover.ShapeDb(slope, 15_000, Rate) < -20, "so should the IIR's.");
        IReadOnlyList<EqNoBoostBand> bands = EqTargetCrossover.NoBoostBands(slope, 100, 18_000, Rate);
        Assert.True(Forbidden(bands, 15_000), "the IIR skirt must be protected too.");
        Assert.False(Forbidden(bands, 2_000), "the passband between them is the fit's business.");
    }

    [Fact]
    public void AStatedAcousticCrossover_DrawsTheElectricalOneBesideTheTarget()
    {
        CrossoverSpec acoustic = new(
            CrossoverKind.LowPass, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24));
        CrossoverSpec electrical = new(
            CrossoverKind.LowPass, new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 12));
        var session = new EqWizardSession();
        session.Load(Source(acoustic) with { ElectricalCrossover = electrical });

        EqWizardRenderSet render = EqWizardRender.RenderSet(session, new EqualizationCurve([]));

        EqWizardCurve drawn = Assert.IsType<EqWizardCurve>(render.ElectricalTarget);
        Assert.NotEqual(render.Target.LineStyle, drawn.LineStyle);
        Assert.NotEqual(LineStyle.Solid, drawn.LineStyle);
        Assert.Equal(render.Target.Color.A / 2, drawn.Color.A);
        Assert.Equal(render.Target.Color.R, drawn.Color.R);
        int octaveUp = Nearest(render.Target, 2_000);
        Assert.Equal(12.3, drawn.Points[octaveUp].Y - render.Target.Points[octaveUp].Y, 0.5);
        int passband = Nearest(render.Target, 100);
        Assert.Equal(render.Target.Points[passband].Y, drawn.Points[passband].Y, 0.05);

        (_, EqWizardCurve fitted) = EqWizardRender.FitCurves(session);
        Assert.Equal(render.Target.Points[octaveUp].Y, fitted.Points[octaveUp].Y, 9);

        List<string> titles = EqWizardTestPlots.Draw(session).Series
            .OfType<LineSeries>()
            .Select(series => series.Title)
            .ToList();
        Assert.True(
            titles.IndexOf(EqWizardRender.ElectricalTargetTitle) is >= 0 and var under &&
            under < titles.IndexOf("Target"),
            string.Join(", ", titles));
    }

    [Fact]
    public void TheElectricalCrossover_IsNotDrawn_WhenThereIsOnlyOneOrTheCrossoverIsLeftOut()
    {
        CrossoverSpec acoustic = new(
            CrossoverKind.LowPass, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24));
        var plain = new EqWizardSession();
        plain.Load(Source(acoustic));
        Assert.Null(EqWizardRender.RenderSet(plain, new EqualizationCurve([])).ElectricalTarget);

        var leftOut = new EqWizardSession();
        leftOut.Load(Source(acoustic) with
        {
            ElectricalCrossover = new CrossoverSpec(
                CrossoverKind.LowPass, new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 12))
        });
        leftOut.SetCrossoverInTarget(false);
        Assert.Null(EqWizardRender.RenderSet(leftOut, new EqualizationCurve([])).ElectricalTarget);
    }

    [Fact]
    public void TakingTheCrossoverIntoTheTarget_KeepsTheSourceCurve()
    {
        var session = new EqWizardSession();
        session.Load(Source(new CrossoverSpec(
            CrossoverKind.LowPass, new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24))) with
        {
            Points = Enumerable.Range(0, 20).Select(i => new SignalPoint(20 * Math.Pow(2, i * 0.5), -0.1 * i)).ToList()
        });
        EqWizardCurve? source = session.SourceCurve;

        session.SetCrossoverInTarget(false);

        Assert.NotNull(source);
        Assert.Same(source, session.SourceCurve);
    }

    private static int Nearest(EqWizardCurve curve, double hz)
    {
        int best = 0;
        for (int i = 1; i < curve.Points.Count; i++)
        {
            if (Math.Abs(Math.Log(curve.Points[i].X / hz)) < Math.Abs(Math.Log(curve.Points[best].X / hz)))
            {
                best = i;
            }
        }

        return best;
    }

    private static EqWizardCurveSource Source(CrossoverSpec? crossover) => new()
    {
        Kind = EqWizardSourceKind.VirtualDspChannel,
        DisplayName = "Ch A",
        Description = "test",
        PreviewChain = DspChannelChain.Identity,
        TargetCrossover = crossover
    };

    private static bool Forbidden(IReadOnlyList<EqNoBoostBand> bands, double hz) =>
        bands.Any(band => hz >= band.LowHz && hz <= band.HighHz);
}
