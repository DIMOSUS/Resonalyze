using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>One period of a multitone with exactly the requested bin magnitudes and phases chosen for a low crest factor.
/// See docs/tech/live-spectrum.md#periodic-pink-excitation.</summary>
public static class PeriodicNoiseSynthesis
{
    public const int DefaultIterations = 60;

    /// <param name="magnitudes">Bin k's magnitude for k = 0..length/2; bin 0 is ignored (no DC).</param>
    /// <returns>The period, unnormalised; its spectrum has exactly <paramref name="magnitudes"/>.</returns>
    public static double[] Synthesize(IReadOnlyList<double> magnitudes, int length, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(magnitudes);
        if (length < 2 || magnitudes.Count != (length / 2) + 1)
        {
            throw new ArgumentException("There must be one magnitude per bin from 0 to length/2.", nameof(magnitudes));
        }

        double[] phases = SchroederPhases(magnitudes, length);
        double[] best = Render(magnitudes, phases, length);
        double bestCrest = CrestFactor(best);
        var spectrum = new Complex[length];

        // Clip toward a shrinking ceiling, keep the phases the clipped signal implies, restore the magnitudes: each pass trades
        // a little spectral error, which the restore removes, for a lower peak.
        double[] current = best;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            double rms = Rms(current);
            double ceiling = rms * Math.Max(1.2, CrestFactor(current) * 0.9);
            for (int i = 0; i < length; i++)
            {
                spectrum[i] = new Complex(Math.Clamp(current[i], -ceiling, ceiling), 0.0);
            }

            Fourier.Forward(spectrum, FourierOptions.NoScaling);
            for (int k = 1; k <= length / 2; k++)
            {
                phases[k] = spectrum[k].Phase;
            }

            current = Render(magnitudes, phases, length);
            double crest = CrestFactor(current);
            if (crest < bestCrest)
            {
                bestCrest = crest;
                best = current;
            }
        }

        return best;
    }

    public static double CrestFactorDb(IReadOnlyList<double> period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return 20.0 * Math.Log10(CrestFactor(period));
    }

    /// <summary>Schroeder's low-crest phases for an arbitrary power spectrum: a chirp that visits each bin once per period.</summary>
    private static double[] SchroederPhases(IReadOnlyList<double> magnitudes, int length)
    {
        int half = length / 2;
        double total = 0;
        for (int k = 1; k <= half; k++)
        {
            total += magnitudes[k] * magnitudes[k];
        }

        var phases = new double[half + 1];
        if (!(total > 0))
        {
            return phases;
        }

        // phi_k = -2π Σ_{l<k} (k - l)·p_l, with p the power fractions, carried as two running sums.
        double sumP = 0, sumLP = 0;
        for (int k = 1; k <= half; k++)
        {
            phases[k] = -2.0 * Math.PI * ((k * sumP) - sumLP);
            double p = magnitudes[k] * magnitudes[k] / total;
            sumP += p;
            sumLP += k * p;
        }

        return phases;
    }

    private static double[] Render(IReadOnlyList<double> magnitudes, double[] phases, int length)
    {
        var spectrum = new Complex[length];
        int half = length / 2;
        for (int k = 1; k <= half; k++)
        {
            double magnitude = magnitudes[k];
            if (!(magnitude > 0))
            {
                continue;
            }

            if (k == length - k)
            {
                // Nyquist is its own mirror, so it can only be real; a sign keeps its magnitude exact.
                spectrum[k] = new Complex(Math.Cos(phases[k]) >= 0 ? magnitude : -magnitude, 0.0);
                continue;
            }

            Complex value = Complex.FromPolarCoordinates(magnitude, phases[k]);
            spectrum[k] = value;
            spectrum[length - k] = Complex.Conjugate(value);
        }

        Fourier.Inverse(spectrum, FourierOptions.NoScaling);
        var period = new double[length];
        for (int i = 0; i < length; i++)
        {
            period[i] = spectrum[i].Real;
        }

        return period;
    }

    private static double CrestFactor(IReadOnlyList<double> period)
    {
        double rms = Rms(period);
        double peak = 0;
        for (int i = 0; i < period.Count; i++)
        {
            peak = Math.Max(peak, Math.Abs(period[i]));
        }

        return rms > 0 ? peak / rms : 1.0;
    }

    private static double Rms(IReadOnlyList<double> period)
    {
        double sum = 0;
        for (int i = 0; i < period.Count; i++)
        {
            sum += period[i] * period[i];
        }

        return Math.Sqrt(sum / period.Count);
    }
}
