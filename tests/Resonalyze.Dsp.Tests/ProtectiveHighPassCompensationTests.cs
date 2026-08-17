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
    public void RemoveFromImpulseResponse_RecoversMagnitudeAndPhaseAboveTheBoostLimit(
        CrossoverFilterFamily family,
        int slope)
    {
        var edge = new CrossoverEdge(family, 2_000, slope);
        Complex[] filtered = FilteredImpulse(edge);

        Complex[] corrected = ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
            filtered,
            edge,
            SampleRate,
            maximumBoostDb: 40.0);
        Fourier.Forward(corrected, FourierOptions.Matlab);

        foreach (double frequencyHz in new[] { 2_000.0, 4_000.0, 10_000.0 })
        {
            int bin = (int)Math.Round(frequencyHz * Length / SampleRate);
            Assert.Equal(1.0, corrected[bin].Magnitude, 8);
            Assert.Equal(0.0, corrected[bin].Phase, 8);
        }
    }

    [Fact]
    public void RemoveFromImpulseResponse_CapsStopBandRecoveryAtFortyDecibels()
    {
        var edge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley,
            2_000,
            48);
        Complex[] filtered = FilteredImpulse(edge);

        Complex[] corrected = ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
            filtered,
            edge,
            SampleRate,
            maximumBoostDb: 40.0);
        Fourier.Forward(corrected, FourierOptions.Matlab);

        int stopBandBin = (int)Math.Round(250.0 * Length / SampleRate);
        Complex originalResponse = CrossoverFilter.Response(
            new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge),
            stopBandBin * SampleRate / Length,
            SampleRate);
        double expectedMagnitude = originalResponse.Magnitude * 100.0;

        Assert.Equal(expectedMagnitude, corrected[stopBandBin].Magnitude, 8);
        Assert.True(corrected[stopBandBin].Magnitude < 0.01);
        Assert.Equal(0.0, corrected[0].Magnitude, 12);
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

    private static Complex[] FilteredImpulse(CrossoverEdge edge)
    {
        var spectrum = new Complex[Length];
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
