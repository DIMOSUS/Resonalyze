using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Where a junction measurement opens its direct-sound window
/// (<see cref="VirtualCrossoverAnalysis.FindGateAnchor"/>). The rule it
/// replaced — the earliest PEAK of the channels in play — answers "where is
/// this loudest", which a crossover's group delay and a strong late feature
/// both move away from the front the window has to hold; these pin that the
/// front is what the anchor now reads, and that neither guard around it lets
/// the answer land later than the peak it replaces.
/// </summary>
public sealed class JunctionGateAnchorTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int FrontSample = 2_048;

    // The alignment gate's fade at 48 kHz (see the gate remarks in
    // VirtualCrossoverAnalysis): the window's plateau starts this far behind
    // its anchor, so content before that point is what a placement discards.
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

    // The share of a response's energy that falls AHEAD of a placement's
    // plateau (dB against its total): what that window throws away of the very
    // channel it is measuring.
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
        // A direct front with a stronger reflection 6 ms behind it — the shape
        // of every channel whose peak is not its arrival, whether the delay
        // comes from a cabin boundary or from the channel's own crossover.
        Complex[] ir = Taps((0, 1.0), (6.0, 2.0));
        int peak = VirtualCrossoverAnalysis.FindPeakIndex(ir);
        Assert.Equal(FrontSample + Samples(6.0), peak);

        int anchor = VirtualCrossoverAnalysis.FindGateAnchor(
            ir, peak, SampleRate, bandLowHz: 1_000, bandHighHz: 4_000);

        // The front, to a hundredth of a millisecond — six milliseconds ahead
        // of the peak the old rule would have anchored on.
        Assert.InRange(
            (anchor - FrontSample) * 1000.0 / SampleRate, -0.10, 0.10);
        // And that is what it buys: anchored on the peak this window opens
        // past the front and discards a fifth of the channel's own energy
        // (-7 dB against what it keeps); anchored on the front it discards
        // none of it.
        Assert.InRange(EnergyAheadOfPlateauDb(ir, peak), -8.0, -6.0);
        Assert.True(
            EnergyAheadOfPlateauDb(ir, anchor) < -100.0,
            "the front-anchored window discarded part of the response");
    }

    [Fact]
    public void Anchor_OnAChannelWhosePeakIsItsFront_StaysWhereThePeakIs()
    {
        // The other half of the claim: this is not a blanket shift. A clean
        // band-passed arrival peaks at its own front, and the anchor agrees
        // with the peak rule to a fraction of a millisecond.
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
        // A tweeter read in a band it only leaks residue into: the envelope
        // there fronts 0.4 ms BEHIND the channel's peak. A window opening
        // after the peak is exactly what this whole placement exists to
        // prevent — at a low junction the same late read is what a room mode
        // produces — so the peak caps the answer.
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

        // A band narrower than MinimumArrivalBandRatio is refused by the
        // detector rather than silently widened, and silence carries no
        // arrival at all. Both fall back to the peak — the rule this
        // replaced — instead of anchoring on a fabricated front.
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
        // Two equal arrivals 12.2 ms apart cancel completely at 41 Hz (and at
        // every odd multiple), which a 33-130 Hz junction band must report as
        // a deep dip. Padded only to the 85 ms gate, the bins sat 11.7 Hz
        // apart at 96 kHz — 35.2, 46.9, 58.6 Hz — and this notch fell straight
        // between two of them, so the dip read a fraction of its depth. The
        // measurement is the same; only how densely it is sampled changed.
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
