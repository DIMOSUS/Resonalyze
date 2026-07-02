using System.Numerics;

namespace Resonalyze.Dsp;

public static class BiquadResponse
{
    /// <summary>
    /// Complex response H(e^{jw}) of a digital biquad at the given frequency. Uses
    /// the miniDSP sign convention of <see cref="BiquadCoefficients"/> (the stored
    /// a1/a2 are added in the feedback path):
    /// H(z) = (b0 + b1 z^-1 + b2 z^-2) / (1 - a1 z^-1 - a2 z^-2).
    /// Evaluating the actual digital filter (rather than an analog prototype) keeps
    /// the predicted response identical to what a DSP running these coefficients
    /// produces, including the bilinear-transform behavior near Nyquist.
    /// </summary>
    public static Complex Evaluate(
        BiquadCoefficients coefficients,
        double frequencyHz,
        double sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        double omega = Math.Tau * frequencyHz / sampleRateHz;
        Complex z1 = Complex.Exp(new Complex(0, -omega));
        Complex z2 = z1 * z1;

        Complex numerator = coefficients.B0 + coefficients.B1 * z1 + coefficients.B2 * z2;
        Complex denominator = 1.0 - coefficients.A1 * z1 - coefficients.A2 * z2;
        return numerator / denominator;
    }
}
