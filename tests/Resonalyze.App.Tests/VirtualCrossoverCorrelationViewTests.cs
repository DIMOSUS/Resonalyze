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
    public void BuildCorrelationView_ArrivalMarkerIsTheReadTheSearchAnchorsOn()
    {
        // A midbass whose 90-360 Hz envelope latches onto a late modal build-up, against a clean mid: the search
        // re-anchors that side on its upper-half read, and the marker must draw that read, not the latched envelope.
        Complex[] midbass = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(FrontSample),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 800, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24))),
            SampleRate, SampleRate);
        int modeStart = FrontSample + (int)Math.Round(0.010 * SampleRate);
        foreach (double modeHz in new[] { 65.0, 72.0, 80.0 })
        {
            for (int i = modeStart; i < midbass.Length; i++)
            {
                double t = (i - modeStart) / (double)SampleRate;
                midbass[i] += 2.0 * (1 - Math.Exp(-t / 0.008)) * Math.Exp(-t / 0.1) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }
        var wide = new ValidSampleRange(0, IrLength);
        ProcessedChannel lower = Channel("B", midbass, wide);
        ProcessedChannel upper = Channel("C", Impulse(FrontSample), wide);

        JunctionCorrelationView view = VirtualCrossoverPanel.BuildCorrelationView(
            new AdjacentPair(lower, upper, 180, 90, 360), [lower, upper]);
        double latchedLagMs =
            VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(midbass, SampleRate, 90, 360, wide) -
            VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(upper.ImpulseResponse, SampleRate, 90, 360, wide);

        Assert.True(latchedLagMs > 20, $"the fixture's raw envelope should latch late, read {latchedLagMs:0.0} ms");
        Assert.True(view.ArrivalReAnchored, "the search re-anchors this junction, so must the marker");
        Assert.InRange(view.ArrivalLagMs, 0, 15);
    }

    private static Complex[] Impulse(int position)
    {
        var ir = new Complex[IrLength];
        ir[position] = 1.0;
        return ir;
    }

    // A soft band-limited front under a late modal build-up: the pair band's envelope latches onto the mode.
    private static Complex[] ModalLatch(int frontSample)
    {
        Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(frontSample),
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 800, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24))),
            SampleRate, SampleRate);
        int modeStart = frontSample + (int)Math.Round(0.010 * SampleRate);
        foreach (double modeHz in new[] { 65.0, 72.0, 80.0 })
        {
            for (int i = modeStart; i < ir.Length; i++)
            {
                double t = (i - modeStart) / (double)SampleRate;
                ir[i] += 2.0 * (1 - Math.Exp(-t / 0.008)) * Math.Exp(-t / 0.1) *
                    Math.Sin(2 * Math.PI * modeHz * t);
            }
        }

        return ir;
    }

    private static ProcessedChannel FrozenChannel(
        VirtualCrossoverChannel channel, Complex[] processed, Complex[] source, DspChannelChain chain) =>
        new(channel, processed, VirtualCrossoverAnalysis.FindPeakIndex(processed),
            SampleRate, OxyColors.White, new ValidSampleRange(0, IrLength),
            Chain: chain, SourceImpulseResponse: source, ProcessorSampleRate: SampleRate);

    [Fact]
    public void BuildCorrelationView_BypassedBlockDoesNotPredictThroughItsConfiguredCrossover()
    {
        // The block is bypassed, so its response is the raw driver; its settings still name a low-pass. The arrival
        // read must grade that response against a chain-free prediction: whatever the settings say, the marker is
        // one and the same.
        Complex[] raw = ModalLatch(FrontSample);
        var channel = new VirtualCrossoverChannel("B") { SampleRate = SampleRate };
        channel.Pair.Bypass = true;
        var mid = new VirtualCrossoverChannel("C") { SampleRate = SampleRate };
        ProcessedChannel upper = FrozenChannel(
            mid, Impulse(FrontSample), Impulse(FrontSample), DspChannelChain.Identity);

        double MarkerWith(double lowPassHz)
        {
            channel.Settings.CrossoverKind = CrossoverKind.LowPass;
            channel.Settings.LowPassEdge =
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, lowPassHz, 48);
            ProcessedChannel lower = FrozenChannel(channel, raw, raw, DspChannelChain.Identity);
            return VirtualCrossoverPanel.BuildCorrelationView(
                new AdjacentPair(lower, upper, 180, 90, 360), [lower, upper]).ArrivalLagMs;
        }

        double at80 = MarkerWith(80);
        double at200 = MarkerWith(200);
        double at400 = MarkerWith(400);
        Assert.Equal(at80, at200, 6);
        Assert.Equal(at80, at400, 6);
    }

    [Fact]
    public void BuildCorrelationView_ArrivalMarkerFollowsTheAppliedDelay()
    {
        // Two clean fronts; the mid is rendered through a delay block. Lag 0 is the applied alignment, so the marker
        // must sit at minus that delay and move with it. The predicted front is read from the chain-free response, and
        // that response must carry the delay too: left at zero, the honesty probe convicts the delay itself as a modal
        // latch and pins the marker to the undelayed prediction, where it no longer answers the setting at all.
        var woof = new VirtualCrossoverChannel("B") { SampleRate = SampleRate };
        var mid = new VirtualCrossoverChannel("C") { SampleRate = SampleRate };
        Complex[] source = Impulse(FrontSample);
        Complex[] lowerIr = VirtualCrossoverAnalysis.ApplyChain(
            source, DspChannelChain.Identity, SampleRate, SampleRate);
        ProcessedChannel lower = FrozenChannel(woof, lowerIr, source, DspChannelChain.Identity);

        JunctionCorrelationView ViewWith(double delayMs)
        {
            var chain = new DspChannelChain(DelayMs: delayMs);
            Complex[] upperIr = VirtualCrossoverAnalysis.ApplyChain(source, chain, SampleRate, SampleRate);
            ProcessedChannel upper = FrozenChannel(mid, upperIr, source, chain);
            return VirtualCrossoverPanel.BuildCorrelationView(
                new AdjacentPair(lower, upper, 1_000, 500, 2_000), [lower, upper]);
        }

        JunctionCorrelationView at5 = ViewWith(5.0);
        JunctionCorrelationView at11 = ViewWith(11.0);
        Assert.False(at11.ArrivalReAnchored, "a delay is not a modal latch");
        Assert.InRange(at5.ArrivalLagMs, -5.1, -4.9);
        Assert.InRange(at11.ArrivalLagMs, -11.1, -10.9);
    }

    [Fact]
    public void BuildCorrelationView_ReadsTheRenderSnapshotNotTheLiveChannel()
    {
        // The processed response, its chain and its source are frozen with the render; a channel that has moved on
        // (new source, new crossover) by the time the view is built must not change the arrival read.
        Complex[] source = ModalLatch(FrontSample);
        var chain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 24)));
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(source, chain, SampleRate, SampleRate);
        var channel = new VirtualCrossoverChannel("B") { SampleRate = SampleRate };
        channel.TransferImpulseResponse = source;
        channel.Settings.CrossoverKind = CrossoverKind.LowPass;
        channel.Settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 24);
        var mid = new VirtualCrossoverChannel("C") { SampleRate = SampleRate };
        ProcessedChannel lower = FrozenChannel(channel, processed, source, chain);
        ProcessedChannel upper = FrozenChannel(
            mid, Impulse(FrontSample), Impulse(FrontSample), DspChannelChain.Identity);
        var pair = new AdjacentPair(lower, upper, 180, 90, 360);

        JunctionCorrelationView before = VirtualCrossoverPanel.BuildCorrelationView(pair, [lower, upper]);
        channel.TransferImpulseResponse = Impulse(FrontSample + 4_800);
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        channel.Pair.Bypass = true;
        // The engine reads rates off the channel: a rebound block at another rate must not rescale the read either.
        channel.SampleRate = 96_000;
        channel.ProcessorSampleRateProvider = () => 192_000;
        JunctionCorrelationView after = VirtualCrossoverPanel.BuildCorrelationView(pair, [lower, upper]);

        Assert.Equal(before.ArrivalLagMs, after.ArrivalLagMs, 6);
        Assert.Equal(before.ArrivalReAnchored, after.ArrivalReAnchored);
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
