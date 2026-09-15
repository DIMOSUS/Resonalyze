using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>Pins <see cref="VirtualCrossoverAnalysis.FindGateAnchor"/> reading the front, never later than the peak it replaced.</summary>
public sealed class JunctionGateAnchorTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int FrontSample = 2_048;

    // The alignment gate's fade at 48 kHz: content before the plateau is what a placement discards.
    private const int GateFadeSamples = 256;

    private static int Samples(double milliseconds) =>
        (int)Math.Round(milliseconds / 1000.0 * SampleRate);

    private static Complex[] Taps(params (double OffsetMs, double Amplitude)[] taps)
    {
        var ir = new Complex[IrLength];
        foreach ((double offsetMs, double amplitude) in taps)
        {
            ir[FrontSample + Samples(offsetMs)] += amplitude;
        }

        return ir;
    }

    private static Complex[] Filtered(CrossoverSpec crossover) =>
        VirtualCrossoverAnalysis.ApplyChain(
            Taps((0, 1.0)),
            new DspChannelChain(Crossover: crossover),
            SampleRate,
            SampleRate);

    private static double EnergyAheadOfPlateauDb(Complex[] impulseResponse, int anchor)
    {
        double ahead = 0;
        double total = 0;
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double energy = impulseResponse[i].Real * impulseResponse[i].Real;
            total += energy;
            if (i < anchor - GateFadeSamples)
            {
                ahead += energy;
            }
        }

        return 10 * Math.Log10(Math.Max(ahead / total, 1e-15));
    }

    [Fact]
    public void Anchor_MarksTheFront_WhereThePeakIsALaterFeature()
    {
        Complex[] ir = Taps((0, 1.0), (6.0, 2.0));
        int peak = VirtualCrossoverAnalysis.FindPeakIndex(ir);
        Assert.Equal(FrontSample + Samples(6.0), peak);

        int anchor = VirtualCrossoverAnalysis.FindGateAnchor(
            ir, peak, SampleRate, bandLowHz: 1_000, bandHighHz: 4_000);

        Assert.InRange(
            (anchor - FrontSample) * 1000.0 / SampleRate, -0.10, 0.10);
        // Peak-anchored the window discards a fifth of the channel's energy (-7 dB); front-anchored, none.
        Assert.InRange(EnergyAheadOfPlateauDb(ir, peak), -8.0, -6.0);
        Assert.True(
            EnergyAheadOfPlateauDb(ir, anchor) < -100.0,
            "the front-anchored window discarded part of the response");
    }

    [Fact]
    public void Anchor_OnAChannelWhosePeakIsItsFront_StaysWhereThePeakIs()
    {
        Complex[] midrange = Filtered(new CrossoverSpec(
            CrossoverKind.BandPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 250, 24)));
        int peak = VirtualCrossoverAnalysis.FindPeakIndex(midrange);

        int anchor = VirtualCrossoverAnalysis.FindGateAnchor(
            midrange, peak, SampleRate, bandLowHz: 1_000, bandHighHz: 4_000);

        Assert.InRange((peak - anchor) * 1000.0 / SampleRate, 0.0, 0.1);
    }

    [Fact]
    public void Anchor_IsNeverLaterThanThePeak()
    {
        // An envelope fronting 0.4 ms behind the peak: the peak caps the answer.
        Complex[] tweeter = Filtered(new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 2_000, 48)));
        int peak = VirtualCrossoverAnalysis.FindPeakIndex(tweeter);
        double lateArrivalMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            tweeter, SampleRate, 32.5, 130);
        Assert.True(
            lateArrivalMs > peak * 1000.0 / SampleRate,
            $"the arrival ({lateArrivalMs:0.00} ms) was expected behind the peak");

        Assert.Equal(
            peak,
            VirtualCrossoverAnalysis.FindGateAnchor(
                tweeter, peak, SampleRate, bandLowHz: 32.5, bandHighHz: 130));
    }

    [Fact]
    public void Anchor_WithoutAMeasurableArrival_FallsBackToThePeak()
    {
        Complex[] tweeter = Filtered(new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, 2_000, 48)));
        int peak = VirtualCrossoverAnalysis.FindPeakIndex(tweeter);

        // A too-narrow band or silence falls back to the peak rather than a fabricated front.
        Assert.Equal(
            peak,
            VirtualCrossoverAnalysis.FindGateAnchor(
                tweeter, peak, SampleRate, bandLowHz: 1_000, bandHighHz: 1_050));
        Assert.Equal(
            7,
            VirtualCrossoverAnalysis.FindGateAnchor(
                new Complex[IrLength], peakIndex: 7, SampleRate,
                bandLowHz: 1_000, bandHighHz: 4_000));
    }

    [Fact]
    public void SumLoss_FindsACancellationNotchBetweenTheOldBins()
    {
        // Padded only to the 85 ms gate, 96 kHz bins (11.7 Hz apart) straddled the 41 Hz notch and missed its depth.
        const int Rate = 96_000;
        var first = new Complex[65_536];
        var second = new Complex[65_536];
        first[16_384] = 1.0;
        second[16_384 + (int)Math.Round(0.0122 * Rate)] = 1.0;

        (double LossDb, double DipDb)? loss = VirtualCrossoverAnalysis.MeasureSumLoss(
            second, [first], Rate, 33, 130);

        Assert.NotNull(loss);
        Assert.True(
            loss.Value.DipDb < -20.0,
            $"the 41 Hz cancellation read only {loss.Value.DipDb:0.0} dB deep");
    }
}
