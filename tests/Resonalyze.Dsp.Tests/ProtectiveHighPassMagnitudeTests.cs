using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Resonalyze.Dsp;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The magnitude-only compensation exists so a reference-free capture, which
/// CARRIES the protective high-pass, can be compared against a swept impulse
/// response, which has it divided out. That is worth nothing unless the two remove
/// exactly the same thing, so what is pinned here is their equality — not each
/// separately.
/// </summary>
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

        // A unit impulse has a flat unity spectrum, so whatever the impulse-response
        // compensation leaves in each bin IS the correction it applies there.
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
                // Unrecoverable: the impulse-response path zeroes the bin, and the
                // magnitude path must say "nothing to plot" rather than a level.
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

        // Guard the guard: an all-NaN correction would satisfy the loop above.
        Assert.True(compared > frequencies.Length / 2, $"only {compared} bins compared");
    }

    [Fact]
    public void TheCorrectionIsTheFilterSlopeWhereItIsRecoverable()
    {
        // The number that matters in practice: under a 2 kHz / 24 dB per octave
        // corner an uncompensated capture sits ~28 dB low at 900 Hz, which is the
        // gap that would otherwise be read as a real difference between a tweeter's
        // spatial average and its impulse response.
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2000.0, 24);
        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge, SampleRate, MaximumBoostDb, [900.0, 2000.0, 8000.0]);

        Assert.InRange(correction[0], 26.0, 30.0);
        // At the corner a Butterworth is 3 dB down.
        Assert.InRange(correction[1], 2.5, 3.5);
        // Far above it the filter does nothing, so neither does the compensation.
        Assert.InRange(correction[2], -0.05, 0.05);
    }

    [Fact]
    public void DeepBelowTheCornerThereIsNothingToRecover()
    {
        var edge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2000.0, 24);
        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            edge, SampleRate, MaximumBoostDb, [200.0, 400.0]);

        // Boosting by more than the cap would amplify whatever noise is there into a
        // curve that looks like a measurement; NaN breaks the line instead.
        Assert.All(correction, value => Assert.True(double.IsNaN(value)));
    }
}
