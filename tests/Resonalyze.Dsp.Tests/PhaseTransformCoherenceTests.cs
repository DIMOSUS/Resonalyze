using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>γ² weighting of GCC-PHAT: absent/flat γ² is a bit-exact no-op, the fold stays Hermitian, a corrupt band is pulled back.</summary>
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

        Assert.Equal(unweighted.LagSamples, nullCoherence.LagSamples);
        Assert.Equal(unweighted.PeakCorrelation, nullCoherence.PeakCorrelation);
        Assert.Equal(unweighted.Refined, nullCoherence.Refined);
    }

    [Fact]
    public void FlatUnityCoherence_IsBitIdenticalToTheUnweightedResult()
    {
        // 1 - (1-floor)*(1-1) = 1.0 exactly in IEEE-754, so bandWeight *= 1.0 is the identity.
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
        // A constant γ² cancels in the argmax and in confidence = peak/normalizer.
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
        // A wrong-length γ² belongs to another grid: ignore it, never throw.
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
        // A half-only weighting would inject an imaginary part.
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
        // PHAT whitens the corrupt band's wrong delay into sidelobe bias; low γ² there pulls the refinement back.
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

        Assert.True(unweighted.Refined);
        Assert.True(weighted.Refined);

        double unweightedError = Math.Abs(unweighted.LagSamples - TrueDelay);
        double weightedError = Math.Abs(weighted.LagSamples - TrueDelay);

        Assert.True(
            unweightedError > 0.1,
            $"Expected the corrupt band to bias the unweighted refine; error was {unweightedError:0.000}.");
        Assert.True(
            weightedError < unweightedError * 0.5,
            $"Coherence weighting did not improve the fit enough: {unweightedError:0.000} -> {weightedError:0.000}.");
        Assert.InRange(weighted.LagSamples, TrueDelay - 0.15, TrueDelay + 0.15);
    }

    [Fact]
    public void RepeatableCorruption_IsNotSuppressed_DocumentsTheDistortionLimit()
    {
        // Repeatable distortion reads γ²≈1: the weighting cannot help, and the test pins that limitation.
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
