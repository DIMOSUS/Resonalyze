using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Computes analytic-signal envelopes via the Hilbert transform.
/// </summary>
public static class SignalEnvelope
{
    /// <summary>
    /// Computes the magnitude envelope of a real-valued signal.
    /// </summary>
    /// <param name="signal">Input samples.</param>
    /// <returns>Envelope samples with the same length as <paramref name="signal"/>.</returns>
    public static double[] Envelope(IReadOnlyList<double> signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.Count == 0)
        {
            throw new ArgumentException(
                "Signal must not be empty.",
                nameof(signal));
        }

        int length = signal.Count;
        var spectrum = new Complex[length];

        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        if ((length & 1) == 0)
        {
            for (int bin = 1; bin < length / 2; bin++)
            {
                spectrum[bin] *= 2.0;
            }

            for (int bin = length / 2 + 1; bin < length; bin++)
            {
                spectrum[bin] = Complex.Zero;
            }
        }
        else
        {
            for (int bin = 1; bin <= (length - 1) / 2; bin++)
            {
                spectrum[bin] *= 2.0;
            }

            for (int bin = (length + 1) / 2; bin < length; bin++)
            {
                spectrum[bin] = Complex.Zero;
            }
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var envelope = new double[length];
        for (int i = 0; i < length; i++)
        {
            envelope[i] = spectrum[i].Magnitude;
        }

        return envelope;
    }
}
