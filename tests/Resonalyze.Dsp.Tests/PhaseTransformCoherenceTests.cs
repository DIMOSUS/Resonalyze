using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Coherence weighting of the GCC-PHAT correlation: γ² de-weights bins whose phase
/// does not repeat across averages. These pin the two load-bearing invariants (a
/// flat/absent γ² is a byte-for-byte no-op; the fold stays Hermitian) and prove the
/// point of the feature — a corrupted band biases the unweighted refinement, and the
/// weighting pulls it back toward the true delay.
/// </summary>
public sealed class PhaseTransformCoherenceTests
{
    private const int Length = 4096;
    private const int HalfLength = Length / 2;          // Nyquist index
    private const int CoherenceLength = HalfLength + 1; // DC..Nyquist inclusive
    private const int InBandMaxBin = Length * 2 / 5;    // matches BandLimitedPulse's band
    private const double TrueDelay = 50.35;

    [Fact]
    public void NullCoherence_IsBitIdenticalToTheUnweightedOverload()
    {
        double[] pulse = BandLimitedPulse(Length, TrueDelay);

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay nullCoherence = TransferFunction
            .ComputePhaseTransformFromResponse(pulse, coherence: null)
            .RefineAround(50, searchRadiusSamples: 4);

        // Exact equality, not InRange: the null default must not perturb a single bit.
        Assert.Equal(unweighted.LagSamples, nullCoherence.LagSamples);
        Assert.Equal(unweighted.PeakCorrelation, nullCoherence.PeakCorrelation);
        Assert.Equal(unweighted.Refined, nullCoherence.Refined);
    }

    [Fact]
    public void FlatUnityCoherence_IsBitIdenticalToTheUnweightedResult()
    {
        // γ²==1 at every bin must reproduce the unweighted result exactly. This is the
        // complement-form guarantee: 1 - (1-floor)*(1-1) = 1.0 in IEEE-754 for any
        // floor, so bandWeight *= 1.0 is the identity.
        double[] pulse = BandLimitedPulse(Length, TrueDelay);
        double[] ones = Enumerable.Repeat(1.0, CoherenceLength).ToArray();

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay weighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse, coherence: ones)
            .RefineAround(50, searchRadiusSamples: 4);

        Assert.Equal(unweighted.LagSamples, weighted.LagSamples);
        Assert.Equal(unweighted.PeakCorrelation, weighted.PeakCorrelation);
        Assert.Equal(unweighted.Refined, weighted.Refined);
    }

    [Fact]
    public void FlatNonUnityCoherence_LeavesLagAndNormalizedConfidenceUnchanged()
    {
        // A spatially flat γ²=c<1 scales every whitened phasor by the same constant.
        // That cannot move the argmax, and it cancels in confidence = peak/normalizer,
        // so both the refined lag and the [0,1] confidence are unchanged even though
        // the raw correlation amplitudes differ.
        double[] pulse = BandLimitedPulse(Length, TrueDelay);
        double[] half = Enumerable.Repeat(0.5, CoherenceLength).ToArray();

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay weighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse, coherence: half)
            .RefineAround(50, searchRadiusSamples: 4);

        Assert.Equal(unweighted.LagSamples, weighted.LagSamples, precision: 12);
        Assert.Equal(unweighted.PeakCorrelation, weighted.PeakCorrelation, precision: 12);
    }

    [Fact]
    public void WrongLengthCoherence_IsIgnoredRatherThanMisIndexed()
    {
        // A length that is not fftLength/2+1 belongs to a different frequency grid;
        // folding it by this FFT would misattribute SNR. The strict length gate must
        // ignore it and reproduce the unweighted result, never throw.
        double[] pulse = BandLimitedPulse(Length, TrueDelay);
        double[] mismatched = Enumerable.Repeat(0.3, 123).ToArray();

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay weighted = TransferFunction
            .ComputePhaseTransformFromResponse(pulse, coherence: mismatched)
            .RefineAround(50, searchRadiusSamples: 4);

        Assert.Equal(unweighted.LagSamples, weighted.LagSamples);
        Assert.Equal(unweighted.PeakCorrelation, weighted.PeakCorrelation);
    }

    [Fact]
    public void ArbitraryCoherence_KeepsTheCorrelationRealAndTheRefinementFinite()
    {
        // Per-bin varying γ² must be folded to both Hermitian halves identically so the
        // whitened spectrum stays conjugate-symmetric and the inverse transform stays
        // real. A half-only weighting bug would inject an imaginary part and bias or
        // NaN the peak. The refined lag must stay finite and inside the search window.
        double[] pulse = BandLimitedPulse(Length, TrueDelay);
        var random = new Random(12345);
        double[] coherence = new double[CoherenceLength];
        for (int i = 0; i < coherence.Length; i++)
        {
            coherence[i] = random.NextDouble();
        }

        PhaseTransformDelay result = TransferFunction
            .ComputePhaseTransformFromResponse(pulse, coherence: coherence)
            .RefineAround(50, searchRadiusSamples: 4);

        Assert.True(double.IsFinite(result.LagSamples));
        Assert.True(double.IsFinite(result.PeakCorrelation));
        Assert.InRange(result.LagSamples, 46.0, 54.0);
    }

    [Fact]
    public void CorruptedBand_BiasesUnweightedRefinementAndCoherenceWeightingCorrectsIt()
    {
        // A transfer IR whose upper in-band 60% carries a WRONG delay (a non-repeating
        // band) while the rest is clean. PHAT whitens every in-band bin to unit
        // magnitude, so the corrupt band's phase disagreement biases the whitened peak
        // away from the true delay through sidelobe interference. Reporting low γ²
        // there — exactly what a real multi-average transfer does for non-repeatable
        // content — de-weights those bins and pulls the refinement back.
        const double wrongDelay = TrueDelay + 12.0;
        int corruptFrom = InBandMaxBin - InBandMaxBin * 3 / 5; // upper 60% of the band
        double[] corrupted = TwoBandPulse(Length, TrueDelay, wrongDelay, corruptFrom);

        double[] coherence = Enumerable.Repeat(1.0, CoherenceLength).ToArray();
        for (int bin = corruptFrom; bin <= InBandMaxBin; bin++)
        {
            coherence[bin] = 0.05; // the corrupt band does not repeat across averages
        }

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(corrupted)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay weighted = TransferFunction
            .ComputePhaseTransformFromResponse(corrupted, coherence: coherence)
            .RefineAround(50, searchRadiusSamples: 4);

        // Both refine cleanly inside the window (the integer peak never pins to an edge).
        Assert.True(unweighted.Refined);
        Assert.True(weighted.Refined);

        double unweightedError = Math.Abs(unweighted.LagSamples - TrueDelay);
        double weightedError = Math.Abs(weighted.LagSamples - TrueDelay);

        // The corrupt band biases the unweighted refinement by the better part of a
        // sample...
        Assert.True(
            unweightedError > 0.1,
            $"Expected the corrupt band to bias the unweighted refine; error was {unweightedError:0.000}.");
        // ...the coherence weighting cuts that error by more than half...
        Assert.True(
            weightedError < unweightedError * 0.5,
            $"Coherence weighting did not improve the fit enough: {unweightedError:0.000} -> {weightedError:0.000}.");
        // ...and lands the refinement close to the true delay.
        Assert.InRange(weighted.LagSamples, TrueDelay - 0.15, TrueDelay + 0.15);
    }

    [Fact]
    public void RepeatableCorruption_IsNotSuppressed_DocumentsTheDistortionLimit()
    {
        // The same corrupt band, but reported as HIGH coherence (γ²≈1) — the signature
        // of repeatable harmonic distortion rather than random noise. Coherence
        // weighting targets non-repeating content only, so here the weighted refine
        // must stay essentially as biased as the unweighted one. This pins the honest
        // limitation rather than pretending the feature fixes distortion.
        const double wrongDelay = TrueDelay + 12.0;
        int corruptFrom = InBandMaxBin - InBandMaxBin * 3 / 5;
        double[] corrupted = TwoBandPulse(Length, TrueDelay, wrongDelay, corruptFrom);

        double[] coherence = Enumerable.Repeat(1.0, CoherenceLength).ToArray();
        for (int bin = corruptFrom; bin <= InBandMaxBin; bin++)
        {
            coherence[bin] = 1.0; // repeatable: coherence cannot tell it apart from signal
        }

        PhaseTransformDelay unweighted = TransferFunction
            .ComputePhaseTransformFromResponse(corrupted)
            .RefineAround(50, searchRadiusSamples: 4);
        PhaseTransformDelay weighted = TransferFunction
            .ComputePhaseTransformFromResponse(corrupted, coherence: coherence)
            .RefineAround(50, searchRadiusSamples: 4);

        Assert.Equal(unweighted.LagSamples, weighted.LagSamples, precision: 9);
    }

    // A band-limited pulse whose low sub-band encodes trueDelay and whose upper
    // sub-band (from corruptFrom to the in-band edge) encodes wrongDelay. Both sub-
    // bands are flat unit magnitude, so the soft energy gate treats them identically
    // and only their phase (delay) differs — the corrupt band's only distinguishing
    // mark is the very thing coherence weighting acts on.
    private static double[] TwoBandPulse(
        int length,
        double trueDelay,
        double wrongDelay,
        int corruptFrom)
    {
        var spectrum = new Complex[length];
        spectrum[0] = Complex.One;
        int maxBin = length * 2 / 5;
        for (int k = 1; k <= maxBin; k++)
        {
            double delay = k >= corruptFrom ? wrongDelay : trueDelay;
            double angle = -2.0 * Math.PI * k * delay / length;
            Complex bin = Complex.FromPolarCoordinates(1.0, angle);
            spectrum[k] = bin;
            spectrum[length - k] = Complex.Conjugate(bin);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var pulse = new double[length];
        for (int i = 0; i < length; i++)
        {
            pulse[i] = spectrum[i].Real;
        }

        return pulse;
    }

    // Single-delay band-limited pulse (mirrors the helper in TransferFunctionTests):
    // a flat-magnitude linear-phase spectrum up to bin length*2/5, exact known delay.
    private static double[] BandLimitedPulse(int length, double delaySamples)
    {
        var spectrum = new Complex[length];
        spectrum[0] = Complex.One;
        int maxBin = length * 2 / 5;
        for (int k = 1; k <= maxBin; k++)
        {
            double angle = -2.0 * Math.PI * k * delaySamples / length;
            Complex bin = Complex.FromPolarCoordinates(1.0, angle);
            spectrum[k] = bin;
            spectrum[length - k] = Complex.Conjugate(bin);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var pulse = new double[length];
        for (int i = 0; i < length; i++)
        {
            pulse[i] = spectrum[i].Real;
        }

        return pulse;
    }
}
