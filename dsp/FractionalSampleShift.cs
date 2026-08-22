using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Moving a signal along its own sample grid by an amount that need not be a whole
/// sample.
/// </summary>
/// <remarks>
/// Rounding a shift to the nearest sample costs up to half a sample — 5 µs at 96 kHz,
/// 1.8 mm of path — which is the same order as the arrival times this library resolves,
/// so anything that places several captures on one grid has to move them exactly. For a
/// signal band-limited below Nyquist, and a sweep measured to 20 kHz at 96 kHz is one,
/// a fractional shift is not an approximation at all: a linear phase ramp on the
/// spectrum is sinc interpolation done exactly, with no kernel truncation to trade off.
/// </remarks>
public static class FractionalSampleShift
{
    /// <summary>
    /// <c>y[k] = x[k + shiftSamples]</c> on a circular buffer, the shift given in
    /// samples and allowed to be fractional. A whole-sample shift is a plain rotation
    /// and returns the samples bit for bit; a fractional one goes through the spectrum.
    /// </summary>
    /// <remarks>
    /// Circular, not zero-padded, and deliberately so: the caller is re-referencing a
    /// buffer whose ends already meet — the pre-roll of a deconvolved sweep holds the
    /// harmonic images that belong at negative time — so what leaves one end is exactly
    /// what should arrive at the other.
    /// </remarks>
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

        // Exactly on the grid: leave the samples untouched rather than send them through
        // a transform that would only add rounding noise.
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

        // Advancing by s multiplies bin k by exp(+i2*pi*k*s/n). Only the lower half is
        // computed and the upper half mirrored, so the result stays real to the last bit
        // instead of relying on an imaginary residue cancelling.
        int half = n / 2;
        for (int k = 1; k < (n + 1) / 2; k++)
        {
            Complex ramp = Complex.FromPolarCoordinates(1.0, 2.0 * Math.PI * k * fraction / n);
            spectrum[k] *= ramp;
            spectrum[n - k] = Complex.Conjugate(spectrum[k]);
        }

        if (n % 2 == 0)
        {
            // The Nyquist bin has no partner to be the conjugate of: a real signal's
            // value there is real, and the ramp can only scale it by cos(pi*fraction).
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
