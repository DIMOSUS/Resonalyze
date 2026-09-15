using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Exact fractional shifts: rounding costs up to half a sample, the order of the arrival times resolved here.</summary>
public static class FractionalSampleShift
{
    /// <summary><c>y[k] = x[k + shiftSamples]</c>, circular on purpose: a deconvolved sweep's pre-roll belongs at negative time.
    /// Whole-sample shifts are bit-exact rotations.</summary>
    public static double[] AdvanceCircular(IReadOnlyList<double> samples, double shiftSamples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Cannot shift an empty signal.", nameof(samples));
        }

        if (!double.IsFinite(shiftSamples))
        {
            throw new ArgumentException("The shift must be a finite number of samples.", nameof(shiftSamples));
        }

        int n = samples.Count;
        int whole = (int)Math.Floor(shiftSamples);
        double fraction = shiftSamples - whole;

        var rotated = new double[n];
        for (int i = 0; i < n; i++)
        {
            int source = (int)(((long)i + whole) % n);
            if (source < 0)
            {
                source += n;
            }

            rotated[i] = samples[source];
        }

        if (Math.Abs(fraction) < 1e-12)
        {
            return rotated;
        }

        var spectrum = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            spectrum[i] = new Complex(rotated[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        // Lower half computed, upper half mirrored, so the result is real to the bit.
        int half = n / 2;
        for (int k = 1; k < (n + 1) / 2; k++)
        {
            Complex ramp = Complex.FromPolarCoordinates(1.0, 2.0 * Math.PI * k * fraction / n);
            spectrum[k] *= ramp;
            spectrum[n - k] = Complex.Conjugate(spectrum[k]);
        }

        if (n % 2 == 0)
        {
            // Nyquist has no conjugate partner: scale by cos(pi*fraction).
            spectrum[half] = new Complex(spectrum[half].Real * Math.Cos(Math.PI * fraction), 0.0);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var shifted = new double[n];
        for (int i = 0; i < n; i++)
        {
            shifted[i] = spectrum[i].Real;
        }

        return shifted;
    }
}
