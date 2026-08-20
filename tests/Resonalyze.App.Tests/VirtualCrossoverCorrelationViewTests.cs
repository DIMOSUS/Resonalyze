using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The correlation view's data build (<see cref="VirtualCrossoverPanel.BuildCorrelationView"/>):
/// the channels' <see cref="ValidSampleRange"/> must reach the front
/// detections behind the "PHAT direct" cuts and the score sweep — shifted
/// into the crop's frame, since the view crops the responses first — so the
/// diagnostic curves read the same fronts the Auto search reads.
/// </summary>
public sealed class VirtualCrossoverCorrelationViewTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 32_768;
    // Far enough in that the shared direct-sound crop removes a non-zero
    // prefix (peak − 8192), so an unshifted original-frame range could not
    // quietly pass for a shifted one.
    private const int FrontSample = 14_000;

    private static ProcessedChannel Channel(
        string name, Complex[] ir, ValidSampleRange range)
    {
        var channel = new VirtualCrossoverChannel(name) { SampleRate = SampleRate };
        return new ProcessedChannel(
            channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir),
            OxyColors.White, range);
    }

    [Fact]
    public void BuildCorrelationView_DirectCurveHonorsTheValidRanges()
    {
        // The upper channel carries an in-band artifact 10 ms AHEAD of its
        // valid range — the shape a chain delay's padding or a capture glitch
        // leaves. With the range honored, the direct cut windows the real
        // front and the "PHAT direct" comb peaks at the pair's true (aligned)
        // timing; with the range dropped, the artifact anchors the cut, the
        // real fronts sit 10 ms apart — far outside the ±3 ms view — and no
        // strong lobe survives near zero.
        var lowerIr = new Complex[IrLength];
        lowerIr[FrontSample] = 1.0;
        var upperIr = new Complex[IrLength];
        upperIr[FrontSample] = 1.0;
        upperIr[FrontSample - 480] = 0.6; // 10 ms early, in-band
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
        // The arrival marker is the same read the search anchors on: with the
        // range honored it sees the aligned fronts, not the artifact.
        Assert.InRange(guardedView.ArrivalLagMs, -0.5, 0.5);

        // The discriminating control: the SAME channels with an
        // all-permissive range on the artifact-bearing one. If the view ever
        // stops passing ranges (or shifts them wrongly enough to void them),
        // this is what the guarded read degenerates to.
        JunctionCorrelationView blindView = View(wide);
        SignalPoint blind = blindView.WhitenedDirect
            .MaxBy(point => Math.Abs(point.Y));
        Assert.True(
            Math.Abs(blind.Y) < 0.5,
            $"with the artifact anchoring the cut no strong lobe should " +
            $"survive in view, got r {blind.Y:0.00} at {blind.X:0.00} ms");
        // ...and the blind marker parks on the artifact, 10 ms early on the
        // upper side — the false front a dropped range would draw.
        Assert.InRange(blindView.ArrivalLagMs, 9.0, 11.0);
    }
}
