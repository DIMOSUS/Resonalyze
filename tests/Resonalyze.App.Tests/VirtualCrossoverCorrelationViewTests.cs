using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Valid sample ranges must reach the correlation view's front detections, shifted into the crop's frame.</summary>
public sealed class VirtualCrossoverCorrelationViewTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 32_768;
    // Far enough in that the crop removes a non-zero prefix, so an unshifted range cannot pass.
    private const int FrontSample = 14_000;

    private static ProcessedChannel Channel(
        string name, Complex[] ir, ValidSampleRange range)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = SampleRate };
        return new ProcessedChannel(
            channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir),
            SampleRate, OxyColors.White, range);
    }

    [Fact]
    public void BuildCorrelationView_DirectCurveHonorsTheValidRanges()
    {
        // In-band artifact 10 ms ahead of the valid range: dropped ranges anchor the cut on it.
        var lowerIr = new Complex[IrLength];
        lowerIr[FrontSample] = 1.0;
        var upperIr = new Complex[IrLength];
        upperIr[FrontSample] = 1.0;
        upperIr[FrontSample - 480] = 0.6;
        var honest = new ValidSampleRange(FrontSample - 96, IrLength);
        var wide = new ValidSampleRange(0, IrLength);

        JunctionCorrelationView View(ValidSampleRange upperRange)
        {
            ProcessedChannel lower = Channel("C", lowerIr, wide);
            ProcessedChannel upper = Channel("D", upperIr, upperRange);
            return VirtualCrossoverPanel.BuildCorrelationView(
                new AdjacentPair(lower, upper, 1_500, 750, 3_000),
                [lower, upper]);
        }

        JunctionCorrelationView guardedView = View(honest);
        SignalPoint guarded = guardedView.WhitenedDirect
            .MaxBy(point => Math.Abs(point.Y));
        Assert.InRange(guarded.X, -0.2, 0.2);
        Assert.True(
            guarded.Y > 0.8,
            $"with the range honored the aligned fronts should cohere " +
            $"strongly, got r {guarded.Y:0.00} at {guarded.X:0.00} ms");
        Assert.InRange(guardedView.ArrivalLagMs, -0.5, 0.5);

        JunctionCorrelationView blindView = View(wide);
        SignalPoint blind = blindView.WhitenedDirect
            .MaxBy(point => Math.Abs(point.Y));
        Assert.True(
            Math.Abs(blind.Y) < 0.5,
            $"with the artifact anchoring the cut no strong lobe should " +
            $"survive in view, got r {blind.Y:0.00} at {blind.X:0.00} ms");
        Assert.InRange(blindView.ArrivalLagMs, 9.0, 11.0);
    }

    [Fact]
    public void BuildCorrelationView_CropKeepsAPreRingLongerThanItsPrePeakBudget()
    {
        // The designer's longest kernel at a 48 kHz processor on a 96 kHz record: 171 ms of pre-ring against the crop's 85.
        const int Taps = 16_383;
        const int Record = 96_000;
        const int Processor = 48_000;
        const double CornerHz = 80;
        const int DriverSample = 12_000;
        int length = DspMath.NextPowerOfTwo(DriverSample + 2 * Taps + 65_536);
        (ProcessedChannel Channel, Complex[] Ir, ValidSampleRange Range) Branch(
            string name, CrossoverKind kind, double driverCornerHz, double delayMs, int? reflectionSample = null)
        {
            var driverEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, driverCornerHz, 12);
            var impulse = new Complex[length];
            impulse[DriverSample] = 1.0;
            if (reflectionSample is { } reflection)
            {
                impulse[reflection] = 0.7;
            }
            Complex[] driver = VirtualCrossoverAnalysis.ApplyChain(
                impulse,
                new DspChannelChain(Crossover: kind == CrossoverKind.LowPass
                    ? new CrossoverSpec(kind, LowPassEdge: driverEdge)
                    : new CrossoverSpec(kind, HighPassEdge: driverEdge)),
                Record, Processor)[..length];
            var edge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, CornerHz, 24);
            FirFilter fir = new FirCrossoverDesign(
                kind, edge, edge, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, Taps, Processor).Build();
            Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                driver, new DspChannelChain(DelayMs: delayMs, Fir: fir), Record, Processor,
                out ValidSampleRange range);
            var channel = new VirtualCrossoverChannel(name) { SampleRate = Record };
            return (new ProcessedChannel(
                channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), Record, OxyColors.White, range), ir, range);
        }

        var lower = Branch("C", CrossoverKind.LowPass, 2.5 * CornerHz, 0);
        // A reflection peaking past the window's reach from the pre-ring start, inside it from a crop that lost 85 ms of pre-ring.
        var upper = Branch("D", CrossoverKind.HighPass, 0.7 * CornerHz, 1.0, DriverSample + 23_500);
        Assert.True(lower.Range.LeadSamples > AlignmentReprocessor.SearchCropPrePeakSamples(Record));

        var pair = new AdjacentPair(lower.Channel, upper.Channel, CornerHz, CornerHz / 2, CornerHz * 2);
        JunctionCorrelationView view = VirtualCrossoverPanel.BuildCorrelationView(
            pair, [lower.Channel, upper.Channel]);

        // The whole records through the same own-front windows: the crop must change nothing.
        double firstMs = view.ScoreNormal[0].X;
        double stepMs = view.ScoreNormal[1].X - firstMs;
        (List<VirtualCrossoverAnalysis.JunctionSweepPoint> normal,
            List<VirtualCrossoverAnalysis.JunctionSweepPoint> inverted) =
            VirtualCrossoverAnalysis.JunctionLossSweepBothPolarities(
                upper.Ir, lower.Ir, Record, pair.BandLowHz, pair.BandHighHz,
                firstMs, view.ScoreNormal[^1].X, stepMs, gateAnchorSample: null, levelMatch: true,
                variableValidRange: upper.Range, fixedValidRange: lower.Range);
        double Score(VirtualCrossoverAnalysis.JunctionSweepPoint point) =>
            point.LossDb + VirtualCrossoverAnalysis.DipExcessPenaltyWeight * (point.DipDb - point.LossDb);
        double worst = view.ScoreNormal.Zip(normal, (drawn, whole) => Math.Abs(drawn.Y - Score(whole)))
            .Concat(view.ScoreInverted.Zip(inverted, (drawn, whole) => Math.Abs(drawn.Y - Score(whole))))
            .Max();

        Assert.Equal(view.ScoreNormal.Count, normal.Count);
        Assert.True(worst <= 0.02, $"the cropped surface departs from the whole records' by up to {worst:0.00} dB");
    }

    [Fact]
    public void DrawCorrelation_AddsEnvelopeGuidesOutsideTheLegend()
    {
        var ir = new Complex[IrLength];
        ir[FrontSample] = 1.0;
        var range = new ValidSampleRange(FrontSample - 96, IrLength);
        ProcessedChannel lower = Channel("C", ir, range);
        ProcessedChannel upper = Channel("D", (Complex[])ir.Clone(), range);
        JunctionCorrelationView view = VirtualCrossoverPanel.BuildCorrelationView(
            new AdjacentPair(lower, upper, 1_500, 750, 3_000),
            [lower, upper]);

        using var plotView = new OxyPlot.WindowsForms.PlotView();
        var plot = new VirtualCrossoverDspChainPlot(
            plotView, DspPlotMode.Correlation);
        plot.DrawCorrelation(view);
        var model = (PlotModel)plotView.Model;

        List<OxyPlot.Series.LineSeries> guides = model.Series
            .OfType<OxyPlot.Series.LineSeries>()
            .Where(series => series.Title is "PHAT envelope" or "PHAT direct envelope")
            .ToList();
        Assert.Equal(4, guides.Count);
        Assert.All(guides, series => Assert.False(series.RenderInLegend));
        foreach (string title in new[] { "PHAT envelope", "PHAT direct envelope" })
        {
            List<OxyPlot.Series.LineSeries> pair = guides
                .Where(series => series.Title == title)
                .ToList();
            Assert.Equal(2, pair.Count);
            Assert.Equal(
                pair[0].Points.Select(point => point.Y),
                pair[1].Points.Select(point => -point.Y));
        }

        Assert.Equal(
            ["PHAT", "PHAT direct", "score", "score inv"],
            model.Series
                .Where(series => series.RenderInLegend)
                .Select(series => series.Title)
                .ToList());
    }

    [Fact]
    public void DrawCoherence_DrawsOneKindOfOptimumMarkerAndClaimsNoPolarity()
    {
        // No polarity: the 2/3-octave probe is 4.3x wider than lobe spacing, so the carrier sign is noise (0-6% contrast measured).
        var view = new JunctionCoherenceView("C-D", "D", 65, 33, 130,
        [
            new VirtualCrossoverAnalysis.ArrivalCoherencePoint(
                103.2, 1.27, 0.981, 0.979, 4.85),
            new VirtualCrossoverAnalysis.ArrivalCoherencePoint(
                115.8, -0.40, 0.981, 0.500, 4.32)
        ]);

        using var plotView = new OxyPlot.WindowsForms.PlotView();
        var plot = new VirtualCrossoverDspChainPlot(plotView, DspPlotMode.Coherence);
        plot.DrawCoherence(view);
        var model = (PlotModel)plotView.Model;

        OxyPlot.Series.ScatterSeries markers = Assert.Single(
            model.Series.OfType<OxyPlot.Series.ScatterSeries>());
        Assert.Equal("optimum", markers.Title);
        Assert.Equal([103.2, 115.8], markers.Points.Select(point => point.X));
        Assert.DoesNotContain(
            model.Series.Where(series => series.RenderInLegend),
            series => series.Title?.Contains("polarity") == true ||
                series.Title?.Contains("inverted") == true);
    }

    [Fact]
    public void DrawCoherence_RefitsTheFrequencyAxisWhenTheBandChanges()
    {
        // Changing the crossover keeps title and 1 ms lag floor, so invalidation must include the band.
        static JunctionCoherenceView View(double lowHz, double highHz) =>
            new("C-D", "D", Math.Sqrt(lowHz * highHz), lowHz, highHz,
            [
                new VirtualCrossoverAnalysis.ArrivalCoherencePoint(
                    lowHz, 0.0, 0.9, 0.9, 500.0 / lowHz),
                new VirtualCrossoverAnalysis.ArrivalCoherencePoint(
                    highHz, 0.0, 0.9, 0.9, 500.0 / highHz)
            ]);

        using var plotView = new OxyPlot.WindowsForms.PlotView();
        var plot = new VirtualCrossoverDspChainPlot(plotView, DspPlotMode.Coherence);
        plot.DrawCoherence(View(750, 3_000));
        plot.DrawCoherence(View(1_500, 6_000));

        OxyPlot.Axes.Axis frequency = ((PlotModel)plotView.Model).Axes
            .Single(axis => axis.Key == PlotModelFactory.FrequencyAxisKey);
        Assert.InRange(frequency.Minimum, 1_300, 1_400);
        Assert.InRange(frequency.Maximum, 6_600, 6_800);
    }

    [Fact]
    public void BuildCoherenceView_ReadsTheProcessedPairInTheCorrelationFrame()
    {
        var ir = new Complex[IrLength];
        ir[FrontSample] = 1.0;
        var range = new ValidSampleRange(FrontSample - 96, IrLength);
        ProcessedChannel lower = Channel("C", ir, range);
        ProcessedChannel upper = Channel("D", (Complex[])ir.Clone(), range);

        JunctionCoherenceView view = VirtualCrossoverPanel.BuildCoherenceView(
            new AdjacentPair(lower, upper, 1_500, 750, 3_000),
            [lower, upper]);

        Assert.Equal("C-D", view.PairTitle);
        Assert.NotEmpty(view.Ladder);
        Assert.All(view.Ladder, point =>
        {
            Assert.InRange(point.FrequencyHz, 750, 3_100);
            Assert.True(
                Math.Abs(point.LagMs) < 0.05,
                $"band {point.FrequencyHz:0} Hz optimum at {point.LagMs:0.000} ms " +
                "on an aligned pair");
            Assert.True(
                point.PeakR - point.CurrentR < 0.05,
                $"band {point.FrequencyHz:0} Hz leaves coherence on the table " +
                "while aligned");
        });
    }
}
