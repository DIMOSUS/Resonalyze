using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class PeriodicNoiseSynthesisTests
{
    [Fact]
    public void Synthesize_KeepsEveryBinMagnitudeExactly()
    {
        const int length = 4096;
        var magnitudes = new double[(length / 2) + 1];
        var random = new Random(7);
        for (int k = 1; k < magnitudes.Length; k++)
        {
            // Gaps and the Nyquist bin are the cases a phase search could smear energy into.
            magnitudes[k] = k % 5 == 0 ? 0.0 : 0.2 + random.NextDouble();
        }
        magnitudes[length / 2] = 0.7;

        double[] period = PeriodicNoiseSynthesis.Synthesize(magnitudes, length);

        double[] actual = BinMagnitudes(period);
        for (int k = 1; k < magnitudes.Length; k++)
        {
            Assert.Equal(magnitudes[k], actual[k], 9);
        }
        Assert.Equal(0.0, period.Average(), 12);
    }

    [Theory]
    [InlineData(65_536, 48_000, 3.0)]
    [InlineData(16_384, 192_000, 4.5)]
    public void Synthesize_PinkPeriodHasALowCrestFactor(int length, int sampleRate, double maximumDb)
    {
        // Random phases give ~13 dB here; see docs/tech/live-spectrum.md#periodic-pink-excitation.
        var magnitudes = new double[(length / 2) + 1];
        for (int k = 1; k < magnitudes.Length; k++)
        {
            magnitudes[k] = k * (double)sampleRate / length >= 10.0 ? 1.0 / Math.Sqrt(k) : 0.0;
        }

        double[] period = PeriodicNoiseSynthesis.Synthesize(magnitudes, length);

        Assert.InRange(PeriodicNoiseSynthesis.CrestFactorDb(period), 0.0, maximumDb);
    }

    [Fact]
    public void Synthesize_IsDeterministic()
    {
        const int length = 2048;
        double[] magnitudes = Enumerable.Range(0, (length / 2) + 1)
            .Select(k => k == 0 ? 0.0 : 1.0 / Math.Sqrt(k))
            .ToArray();

        Assert.Equal(
            PeriodicNoiseSynthesis.Synthesize(magnitudes, length),
            PeriodicNoiseSynthesis.Synthesize(magnitudes, length));
    }

    [Fact]
    public void Synthesize_RejectsAMagnitudeCountThatIsNotHalfTheLength()
    {
        Assert.Throws<ArgumentException>(
            () => PeriodicNoiseSynthesis.Synthesize(new double[1024], 2048));
    }

    private static double[] BinMagnitudes(double[] period)
    {
        var spectrum = period.Select(sample => new Complex(sample, 0.0)).ToArray();
        Fourier.Forward(spectrum, FourierOptions.NoScaling);
        // Both unscaled transforms round-trip with a factor of N.
        return spectrum.Take((period.Length / 2) + 1).Select(value => value.Magnitude / period.Length).ToArray();
    }
}
