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
    [Trait("Category", "Slow")]
    [InlineData(65_536, 48_000, 3.0)]
    [InlineData(16_384, 192_000, 4.5)]
    public void Synthesize_PinkPeriodHasALowCrestFactor(int length, int sampleRate, double maximumDb)
    {
        // Unoptimised random phases give ~13 dB here; see docs/tech/live-spectrum.md#periodic-pink-excitation.
        var magnitudes = new double[(length / 2) + 1];
        for (int k = 1; k < magnitudes.Length; k++)
        {
            magnitudes[k] = k * (double)sampleRate / length >= 10.0 ? 1.0 / Math.Sqrt(k) : 0.0;
        }

        double[] period = PeriodicNoiseSynthesis.Synthesize(magnitudes, length);

        Assert.InRange(PeriodicNoiseSynthesis.CrestFactorDb(period), 0.0, maximumDb);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Synthesize_SpreadsEveryFrequencyOverThePeriodRatherThanSweepingIt()
    {
        // Schroeder's phases reach a similar crest with a chirp, which is audible and reads each frequency from a different point
        // of a moving microphone's path. Every octave band must keep sounding through the whole period instead.
        const int length = 32_768;
        const int sampleRate = 48_000;
        const int slices = 8;
        var magnitudes = new double[(length / 2) + 1];
        for (int k = 1; k < magnitudes.Length; k++)
        {
            magnitudes[k] = k * (double)sampleRate / length >= 10.0 ? 1.0 / Math.Sqrt(k) : 0.0;
        }

        double[] period = PeriodicNoiseSynthesis.Synthesize(magnitudes, length);

        foreach (double centre in new[] { 250.0, 500.0, 1000.0, 2000.0, 4000.0, 8000.0 })
        {
            double[] shares = BandEnergyPerSlice(period, sampleRate, centre, slices);
            // An even spread is 1/8; the chirp put 0.71 to 0.97 of a band into one slice, this period 0.14 to 0.25.
            Assert.InRange(shares.Max(), 1.0 / slices, 0.4);
        }
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

    /// <summary>How one octave band's energy divides between equal slices of the period; each share of an even spread is 1/slices.</summary>
    private static double[] BandEnergyPerSlice(double[] period, int sampleRate, double centre, int slices)
    {
        double low = centre / Math.Sqrt(2.0), high = centre * Math.Sqrt(2.0);
        int size = period.Length / slices;
        var energies = new double[slices];
        for (int slice = 0; slice < slices; slice++)
        {
            var samples = new Complex[size];
            for (int i = 0; i < size; i++)
            {
                double window = 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / size));
                samples[i] = new Complex(period[(slice * size) + i] * window, 0.0);
            }

            Fourier.Forward(samples, FourierOptions.NoScaling);
            for (int k = 1; k <= size / 2; k++)
            {
                double frequency = k * (double)sampleRate / size;
                if (frequency >= low && frequency <= high)
                {
                    energies[slice] += samples[k].Magnitude * samples[k].Magnitude;
                }
            }
        }

        double total = energies.Sum();
        return energies.Select(energy => energy / Math.Max(total, 1e-30)).ToArray();
    }

    private static double[] BinMagnitudes(double[] period)
    {
        var spectrum = period.Select(sample => new Complex(sample, 0.0)).ToArray();
        Fourier.Forward(spectrum, FourierOptions.NoScaling);
        // Both unscaled transforms round-trip with a factor of N.
        return spectrum.Take((period.Length / 2) + 1).Select(value => value.Magnitude / period.Length).ToArray();
    }
}
