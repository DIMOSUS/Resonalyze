using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>The magnitude-only and impulse-response compensations must remove exactly the same thing.</summary>
public sealed class ProtectiveHighPassMagnitudeTests
{
    private const double SampleRate = 48_000;
    private const double MaximumBoostDb = 40.0;
    private const int Length = 8192;

    [Theory]
    [InlineData(CrossoverFilterFamily.Butterworth, 2000.0, 24)]
    [InlineData(CrossoverFilterFamily.Butterworth, 1500.0, 12)]
    [InlineData(CrossoverFilterFamily.LinkwitzRiley, 2500.0, 24)]
    public void MagnitudeCorrection_MatchesWhatTheImpulseResponsePathRemoves(
        CrossoverFilterFamily family,
        double cornerHz,
        int slopeDbPerOctave)
    {
        var edge = new CrossoverEdge(family, cornerHz, slopeDbPerOctave);

        // A unit impulse's flat spectrum: what remains per bin IS the correction.
        var delta = new Complex[Length];
        delta[0] = Complex.One;
        Complex[] compensated = ProtectiveHighPassCompensation
            .RemoveFromImpulseResponse(delta, edge, SampleRate, MaximumBoostDb)
            .ImpulseResponse;
        Fourier.Forward(compensated, FourierOptions.Matlab);

        double binWidth = SampleRate / Length;
        var frequencies = new double[Length / 2 + 1];
        for (int bin = 0; bin < frequencies.Length; bin++)
        {
            frequencies[bin] = bin * binWidth;
        }

        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge, SampleRate, MaximumBoostDb, frequencies);

        int compared = 0;
        for (int bin = 1; bin < frequencies.Length; bin++)
        {
            double fromIr = compensated[bin].Magnitude;
            if (double.IsNaN(correction[bin]))
            {
                Assert.True(
                    fromIr < 1e-9,
                    $"bin {bin} ({frequencies[bin]:0} Hz) kept {fromIr:0.000e+0} where the " +
                    "magnitude path reports no data");
                continue;
            }

            Assert.Equal(
                20.0 * Math.Log10(fromIr),
                correction[bin],
                precision: 6);
            compared++;
        }

        // An all-NaN correction would satisfy the loop above.
        Assert.True(compared > frequencies.Length / 2, $"only {compared} bins compared");
    }

    [Fact]
    public void TheCorrectionIsTheFilterSlopeWhereItIsRecoverable()
    {
        // Under a 2 kHz / 24 dB/oct corner an uncompensated capture sits ~28 dB low at 900 Hz.
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2000.0, 24);
        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge, SampleRate, MaximumBoostDb, [900.0, 2000.0, 8000.0]);

        Assert.InRange(correction[0], 26.0, 30.0);
        Assert.InRange(correction[1], 2.5, 3.5);
        Assert.InRange(correction[2], -0.05, 0.05);
    }

    [Fact]
    public void DeepBelowTheCornerThereIsNothingToRecover()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2000.0, 24);
        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge, SampleRate, MaximumBoostDb, [200.0, 400.0]);

        // Boosting past the cap would turn noise into a plausible curve; NaN breaks the line.
        Assert.All(correction, value => Assert.True(double.IsNaN(value)));
    }

    /// <summary>Only a monotonic high-pass is interchangeable: a rippled passband would part the two paths by the ripple depth.</summary>
    [Fact]
    public void ItRefusesTheFamiliesTheImpulseResponsePathRefuses()
    {
        var rippled = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 2_000, 24);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
                rippled, 48_000, 40.0, [1_000.0]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtectiveHighPassCompensation.RemoveFromImpulseResponse(
                new Complex[8], rippled, 48_000, 40.0));
    }
}
