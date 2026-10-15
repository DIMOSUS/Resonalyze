using System.Numerics;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The Sum loss a chain handoff draws: the edited channel re-filtered with the bank, the others as they stood.</summary>
public sealed class EqWizardSumLossTests
{
    private const int SampleRate = 48_000;
    private const int ArrivalSample = 480;
    private const double CrossoverHz = 300;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Slow")]
    public void AnAllPassOnTheEditedChannelDigsTheJunction_InBothWindows(bool direct)
    {
        // LR24 halves sum in phase: no loss. An all-pass on one of them turns the junction into a cancellation.
        EqWizardSumLossRequest request = Request(bank: null, direct);

        double before = MeanJunctionLossDb(EqWizardSumLossRender.Render(request)!);
        double after = MeanJunctionLossDb(EqWizardSumLossRender.Render(request with
        {
            Bank = new EqualizationCurve(
                [new PeqBand(CrossoverHz, 0.7, 0, PeqBandType.AllPassSecondOrder)])
        })!);

        Assert.True(before > -0.5, $"Aligned halves should lose nothing; they read {before:0.00} dB.");
        Assert.True(after < -3, $"The all-pass should dig the junction; it reads {after:0.00} dB.");
    }

    [Fact]
    public void TheCurveIsAtOrBelowZero_AndStopsWhereNoChannelMeasured()
    {
        EqWizardSumLossRequest request = Request(bank: null, direct: false) with
        {
            Band = new MeasuredBand(100, 5_000),
            Others = [Other(HighPass()) with { Band = new MeasuredBand(100, 5_000) }]
        };

        List<SignalPoint> curve = EqWizardSumLossRender.Render(request)!;

        Assert.Contains(curve, point => double.IsFinite(point.Y));
        // Inside the band: the resample on either side of a mask edge leaves the first bins a few tenths above zero.
        Assert.All(
            curve.Where(point => double.IsFinite(point.Y) && point.X > 150 && point.X < 4_000),
            point => Assert.True(point.Y <= 1e-6, $"{point.X:0} Hz reads {point.Y:0.000} dB"));
        Assert.All(curve.Where(point => point.X < 90 || point.X > 6_000), point => Assert.True(double.IsNaN(point.Y)));
    }

    [Fact]
    public void TheCurveIsOffByDefault_AndTheChoiceIsNotSaved()
    {
        // A diagnostic curve: every launch starts without it, whatever the last session ticked.
        var saved = new EqWizardSession();
        Assert.False(saved.ShowSumLoss);

        saved.SetShowSumLoss(true);
        var restored = new EqWizardSession();
        restored.ApplySettings(saved.CaptureSettings());

        Assert.False(restored.ShowSumLoss);
    }

    [Fact]
    public void TheCurveIsDrawnOnTheEqAxis_WithThePanelsWindowInItsName()
    {
        var session = new EqWizardSession();
        session.Load(Source(SumLossWindow.Direct));
        session.SetShowSumLoss(true);

        PlotModel model = EqWizardTestPlots.Draw(session, sumLoss: LossPoints());

        LineSeries series = Assert.Single(
            model.Series.OfType<LineSeries>(), series => series.Title == "Sum loss (direct)");
        Assert.Equal(EqWizardPlot.EqGainAxisKey, series.YAxisKey);
        Assert.Equal("EQ / Sum loss (dB)", EqAxis(model).Title);
        // The axis holds the deepest point as it holds a bank past its budget.
        Assert.True(EqAxis(model).ActualMinimum <= -18, $"axis bottom {EqAxis(model).ActualMinimum}");
    }

    [Fact]
    public void WithTheBanksCurveOff_TheLossKeepsTheAxis_AndNamesItAlone()
    {
        var session = new EqWizardSession();
        session.Load(Source(SumLossWindow.Full));
        session.SetShowSumLoss(true);
        session.SetShowEqCurve(false);

        PlotModel model = EqWizardTestPlots.Draw(session, sumLoss: LossPoints());

        Assert.Contains("Sum loss", EqWizardTestPlots.CurveTitles(model));
        Assert.DoesNotContain("EQ", EqWizardTestPlots.CurveTitles(model));
        Assert.True(EqAxis(model).IsAxisVisible);
        Assert.Equal("Sum loss (dB)", EqAxis(model).Title);
    }

    [Fact]
    public void ALandedCurveReplacesItsSeriesAlone_AndLeavesTheRestOfTheModel()
    {
        var session = new EqWizardSession();
        session.Load(Source(SumLossWindow.Full));
        session.SetShowSumLoss(true);
        var plot = new EqWizardPlot();
        EqualizationCurve eq = EqWizardRender.DisplayedEq(session);
        plot.Draw(session, eq, EqWizardRender.RenderSet(session, eq), selectedBand: null, phase: null);
        int seriesBefore = plot.Model.Series.Count;
        Assert.DoesNotContain("Sum loss", EqWizardTestPlots.CurveTitles(plot.Model));

        plot.ShowSumLoss(session, LossPoints());
        plot.ShowSumLoss(session, LossPoints().Select(point => new DataPoint(point.X, point.Y / 2)).ToList());

        LineSeries landed = Assert.Single(plot.Model.Series.OfType<LineSeries>(), series => series.Title == "Sum loss");
        Assert.Equal(seriesBefore + 1, plot.Model.Series.Count);
        Assert.Equal(-7.5, landed.Points.Min(point => point.Y));
        Assert.Equal("EQ / Sum loss (dB)", EqAxis(plot.Model).Title);

        plot.ShowSumLoss(session, null);

        Assert.Equal(seriesBefore, plot.Model.Series.Count);
        Assert.Equal("EQ (dB)", EqAxis(plot.Model).Title);
    }

    [Fact]
    public void InPhaseAndWithoutASum_NothingIsDrawn()
    {
        var withContext = new EqWizardSession();
        withContext.Load(Source(SumLossWindow.Full));
        withContext.SetShowSumLoss(true);
        withContext.SetPhaseMode(true);
        Assert.DoesNotContain(
            "Sum loss", EqWizardTestPlots.CurveTitles(EqWizardTestPlots.Draw(withContext, sumLoss: LossPoints())));

        var withoutContext = new EqWizardSession();
        withoutContext.Load(Source(window: null));
        withoutContext.SetShowSumLoss(true);
        Assert.False(withoutContext.SumLossAvailable);
        Assert.DoesNotContain(
            "Sum loss", EqWizardTestPlots.CurveTitles(EqWizardTestPlots.Draw(withoutContext, sumLoss: LossPoints())));
        Assert.Equal("EQ (dB)", EqAxis(EqWizardTestPlots.Draw(withoutContext)).Title);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task TheLossRendersInTheGatedPreviewsJob_OnlyWhileDrawn_AndIsDroppedWithTheSource()
    {
        var session = new EqWizardSession();
        session.Load(Source(SumLossWindow.Full));
        var bank = new EqualizationCurve([new PeqBand(CrossoverHz, 0.7, 0, PeqBandType.AllPassSecondOrder)]);

        // Off: the preview lands alone, nothing is spent on a curve nobody sees.
        await Assert.IsType<Task>(session.Previews.RequestGatedPreview(bank), exactMatch: false);
        Assert.NotNull(session.Previews.GatedPreview);
        Assert.Null(session.Previews.SumLoss);
        Assert.Null(session.Previews.RequestGatedPreview(bank));

        // Ticked: the same bank is rendered again, this time with the loss; then nothing until the bank moves.
        session.SetShowSumLoss(true);
        await Assert.IsType<Task>(session.Previews.RequestGatedPreview(bank), exactMatch: false);
        Assert.NotNull(session.Previews.SumLoss);
        Assert.Null(session.Previews.RequestGatedPreview(bank));

        session.Load(Source(SumLossWindow.Full));

        Assert.Null(session.Previews.SumLoss);
    }

    [Fact]
    public async Task AHybridHandoffRendersTheLossWithoutAGatedMagnitude()
    {
        var session = new EqWizardSession();
        session.Load(Source(SumLossWindow.Full) with { SpatialAverage = Average() });
        session.SetShowSumLoss(true);
        Assert.False(session.Source!.IsGated);
        Assert.True(session.SumLossDrawn);

        await Assert.IsType<Task>(
            session.Previews.RequestGatedPreview(EqWizardRender.DisplayedEq(session)), exactMatch: false);

        Assert.Null(session.Previews.GatedPreview);
        Assert.NotNull(session.Previews.SumLoss);
    }

    private static LiveCaptureDocument Average()
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 64);
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "average",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = [.. grid.Select(_ => -20.0)],
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = SampleRate }
        };
    }

    [Fact]
    public void TheCheckboxReachesTheSession_AndIsLiveOnlyWithASum() => StaTest.Run(() =>
    {
        using var live = new EqWizardPanelWiringTests.LivePanel();
        Assert.False(live.Control<CheckBox>("checkBoxSumLoss").Enabled);

        live.Set<CheckBox>("checkBoxSumLoss", box => box.Checked = true);

        Assert.True(live.Session.ShowSumLoss);
    });

    private static double MeanJunctionLossDb(IReadOnlyList<SignalPoint> curve)
    {
        List<double> losses = curve
            .Where(point => point.X >= CrossoverHz / 1.5 && point.X <= CrossoverHz * 1.5)
            .Where(point => double.IsFinite(point.Y))
            .Select(point => point.Y)
            .ToList();
        Assert.NotEmpty(losses);
        return losses.Average();
    }

    private static EqWizardSumLossRequest Request(EqualizationCurve? bank, bool direct) =>
        new(
            Impulse(),
            LowPass(),
            bank,
            AnchorIndex: 456,
            SampleRate,
            SampleRate,
            Gate(),
            MeasuredBand.Everything,
            Calibration: null,
            [Other(HighPass())],
            SmoothingInverseOctaves: 0,
            direct ? new EqWizardDirectLossGate(null, 1.0, 100.0, 20.0) : null);

    private static SumLossMember Other(DspChannelChain chain)
    {
        Complex[] response = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), chain, SampleRate, SampleRate, out ValidSampleRange valid);
        return new SumLossMember(
            response,
            VirtualCrossoverAnalysis.FindPeakIndex(response),
            valid,
            SampleRate,
            MeasuredBand.Everything,
            Calibration: null);
    }

    private static EqWizardCurveSource Source(SumLossWindow? window)
    {
        Complex[] response = Impulse();
        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "source",
            Description = "A channel",
            Measurement = new ImpulseMeasurementView(response, ArrivalSample, SampleRate),
            PreviewImpulseResponse = response,
            PreviewChain = LowPass(),
            GateSettings = Gate(),
            SumLossContext = window is { } shown
                ? new EqWizardSumLossContext([Other(HighPass())], shown)
                : null,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    private static List<DataPoint> LossPoints() =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, 64)
            .Select(hz => new DataPoint(hz, hz > 200 && hz < 450 ? -15 : -1))
            .ToList();

    private static Axis EqAxis(PlotModel model) =>
        model.Axes.First(axis => axis.Key == EqWizardPlot.EqGainAxisKey);

    private static Complex[] Impulse()
    {
        var impulse = new Complex[16_384];
        impulse[ArrivalSample] = 1.0;
        return impulse;
    }

    private static DspChannelChain LowPass() =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24)));

    private static DspChannelChain HighPass() =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24)));

    private static PhaseAnalysisSettings Gate() => new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 9.5,
        LeftMs: 1.0,
        PlateauMs: 100.0,
        RightMs: 20.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);
}
