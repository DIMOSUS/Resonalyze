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
        return Evaluate(coefficients, z1);
    }

    public static Complex Evaluate(BiquadCoefficients coefficients, Complex z1)
    {
        Complex z2 = z1 * z1;
        return Evaluate(coefficients, z1, z2);
    }

    public static Complex Evaluate(BiquadCoefficients coefficients, Complex z1, Complex z2)
    {
        Complex numerator = coefficients.B0 + coefficients.B1 * z1 + coefficients.B2 * z2;
        Complex denominator = 1.0 - coefficients.A1 * z1 - coefficients.A2 * z2;
        return numerator / denominator;
    }

    /// <summary>
    /// Group delay τ_g = -dφ/dω of one section, in SAMPLES.
    /// <para>
    /// Computed in closed form. For a polynomial P(ω) = Σ c_k·e^{-jkω},
    /// -dArg(P)/dω = Re{ (Σ k·c_k·e^{-jkω}) / P(ω) }, and a section's group delay is
    /// that quantity for its numerator minus the same for its denominator.
    /// </para>
    /// <para>
    /// Exact at every frequency, which a finite difference of the phase is not: a sharp,
    /// high-Q section near Nyquist turns the phase by more than π across any usable step,
    /// and a principal-value difference wraps that into a plainly wrong — routinely
    /// negative — delay. There is no step to tune here and nothing to alias.
    /// </para>
    /// <para>
    /// Zero where the polynomial vanishes on the unit circle (a low-pass section at
    /// Nyquist, a high-pass at DC): the delay is genuinely singular at a zero, and the
    /// section passes nothing there anyway.
    /// </para>
    /// </summary>
    public static double GroupDelaySamples(
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
        if (numerator.Magnitude < ZeroOnTheUnitCircle ||
            denominator.Magnitude < ZeroOnTheUnitCircle)
        {
            return 0;
        }

        // dP/dω for each polynomial, up to the shared -j both would carry (it cancels in
        // the Re{} above). The stored A1/A2 are negated for the additive-feedback
        // convention, so the denominator is 1 - A1·z^-1 - A2·z^-2 and its slope follows.
        Complex numeratorSlope = coefficients.B1 * z1 + 2.0 * coefficients.B2 * z2;
        Complex denominatorSlope = -coefficients.A1 * z1 - 2.0 * coefficients.A2 * z2;
        return (numeratorSlope / numerator).Real - (denominatorSlope / denominator).Real;
    }

    // An exact zero on the unit circle cancels to ~1e-16 rather than 0 in floating point,
    // so the guard sits well above that noise — and far below the ~1e-5 a real section
    // still carries deep in its stopband.
    private const double ZeroOnTheUnitCircle = 1e-12;
}
