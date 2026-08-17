using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class ProtectiveHighPassCompensationTests
{
    private const double SampleRate = 48_000;
    private const int Length = 16_384;

    [Theory]
    [InlineData(CrossoverFilterFamily.Butterworth, 24)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 48)]
    public void RemoveFromImpulseResponse_RecoversMagnitudeAndPhaseInTheTrustedBand(
        CrossoverFilterFamily family,
        int slope)
    {
        var edge = new CrossoverEdge(family, 2_000, slope);
        Complex[] filtered = FilteredImpulse(edge);

        ProtectiveHighPassCompensationResult result =
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                filtered,
                edge,
                SampleRate,
                maximumBoostDb: 40.0);
        Complex[] corrected = result.ImpulseResponse;
        Fourier.Forward(corrected, FourierOptions.Matlab);

        foreach (double frequencyHz in new[] { 2_000.0, 4_000.0, 10_000.0 })
        {
            int bin = (int)Math.Round(frequencyHz * Length / SampleRate);
            Assert.Equal(1.0, corrected[bin].Magnitude, 8);
            Assert.Equal(0.0, corrected[bin].Phase, 8);
        }
    }

    [Fact]
    public void RemoveFromImpulseResponse_SuppressesStopBandAndMarksItUnreliable()
    {
        var edge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley,
            2_000,
            48);
        Complex[] filtered = FilteredImpulse(edge);

        ProtectiveHighPassCompensationResult result =
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                filtered,
                edge,
                SampleRate,
                maximumBoostDb: 40.0);
        Complex[] corrected = result.ImpulseResponse;
        Fourier.Forward(corrected, FourierOptions.Matlab);

        int stopBandBin = (int)Math.Round(250.0 * Length / SampleRate);
        Assert.Equal(0.0, result.Reliability[stopBandBin], 12);
        Assert.True(corrected[stopBandBin].Magnitude < 1e-12);
        Assert.Equal(0.0, corrected[0].Magnitude, 12);
    }

    [Fact]
    public void RemoveFromImpulseResponse_FadesReliabilityBeforeTheBoostLimit()
    {
        var edge = new CrossoverEdge(
            CrossoverFilterFamily.Butterworth,
            3_800,
            12);
        Complex[] filtered = FilteredImpulse(edge);

        ProtectiveHighPassCompensationResult result =
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                filtered,
                edge,
                SampleRate,
                maximumBoostDb: 40.0);

        int stopBin = (int)Math.Round(300.0 * Length / SampleRate);
        int fadeBin = (int)Math.Round(450.0 * Length / SampleRate);
        int trustedBin = (int)Math.Round(700.0 * Length / SampleRate);
        Assert.Equal(0.0, result.Reliability[stopBin], 12);
        Assert.InRange(result.Reliability[fadeBin], 0.25, 0.75);
        Assert.Equal(1.0, result.Reliability[trustedBin], 12);

        Complex[] corrected = result.ImpulseResponse.ToArray();
        Fourier.Forward(corrected, FourierOptions.Matlab);
        // The IR uses the complete six-decibel reliability fade itself. A
        // second confidence-domain threshold here would collapse that smooth
        // transition back into a near-brick-wall frequency edge.
        Assert.Equal(result.Reliability[fadeBin], corrected[fadeBin].Magnitude, 8);

        double[] coherence = new double[result.Reliability.Length];
        Array.Fill(coherence, 1.0);
        double[] masked = Assert.IsType<double[]>(result.MaskCoherence(coherence));
        Assert.Equal(result.Reliability, masked);
    }

    [Theory]
    [InlineData(CrossoverFilterFamily.Butterworth, 12, 30.0)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 24, 30.0)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 48, 30.0)]
    [InlineData(CrossoverFilterFamily.Butterworth, 12, 3_800.0)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 48, 3_800.0)]
    public void RemoveFromImpulseResponse_ReliabilityFadePreservesArrival(
        CrossoverFilterFamily family,
        int slope,
        double frequencyHz)
    {
        const int longLength = 131_072;
        var edge = new CrossoverEdge(family, frequencyHz, slope);
        ProtectiveHighPassCompensationResult compensation =
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                FilteredImpulse(edge, longLength),
                edge,
                SampleRate,
                maximumBoostDb: 40.0);
        double[] corrected = Array.ConvertAll(
            compensation.ImpulseResponse,
            sample => sample.Real);

        TimeAlignmentAnalysisResult arrival = TimeAlignmentAnalysis.Analyze(
            corrected,
            (int)SampleRate,
            new TimeAlignmentAnalysisOptions
            {
                WrapPeakPositions = true
            });

        Assert.True(arrival.IsValid);
        Assert.InRange(Math.Abs(arrival.FirstArrivalDelayMilliseconds), 0.0, 0.05);

        // A dangerous zero-phase mask would leave a sidelobe above the normal
        // first-arrival threshold far from the circular impulse. Keep the
        // central ±500 ms region below that -25 dB decision level.
        int farGuard = (int)Math.Round(0.5 * SampleRate);
        double farPeak = arrival.EnvelopeSamples
            .Skip(farGuard)
            .Take(arrival.EnvelopeSamples.Length - 2 * farGuard)
            .Max();
        Assert.True(
            farPeak < arrival.StrongestEnvelopePeak * Math.Pow(10.0, -25.0 / 20.0),
            $"far ringing is {20.0 * Math.Log10(farPeak / arrival.StrongestEnvelopePeak):0.0} dB");
    }

    [Fact]
    public void RemoveFromImpulseResponse_RejectsAnUnsupportedFamily()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Bessel, 2_000, 24);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                [Complex.One],
                edge,
                SampleRate,
                maximumBoostDb: 40.0));
    }

    private static Complex[] FilteredImpulse(CrossoverEdge edge) =>
        FilteredImpulse(edge, Length);

    private static Complex[] FilteredImpulse(CrossoverEdge edge, int length)
    {
        var spectrum = new Complex[length];
        CrossoverSpec spec = new(CrossoverKind.HighPass, HighPassEdge: edge);
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            int signedBin = bin <= spectrum.Length / 2 ? bin : bin - spectrum.Length;
            double frequencyHz = signedBin * SampleRate / spectrum.Length;
            spectrum[bin] = CrossoverFilter.Response(spec, frequencyHz, SampleRate);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }
}
