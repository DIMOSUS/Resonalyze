using System.Numerics;

namespace Resonalyze.Dsp;

public static class BiquadResponse
{
    /// <summary>
    /// Digital biquad response, miniDSP sign convention: H(z) = (b0 + b1 z^-1 + b2 z^-2) / (1 - a1 z^-1 - a2 z^-2).
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
    /// Group delay in SAMPLES, closed form: -dArg(P)/dω = Re{(Σ k·c_k·e^{-jkω}) / P}, numerator minus denominator.
    /// Exact where a phase finite difference wraps near Nyquist; zero where a polynomial vanishes on the unit circle.
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

        // Common -j factor omitted (cancels in Re{}); A1/A2 negated for the additive-feedback convention.
        Complex numeratorSlope = coefficients.B1 * z1 + 2.0 * coefficients.B2 * z2;
        Complex denominatorSlope = -coefficients.A1 * z1 - 2.0 * coefficients.A2 * z2;
        return (numeratorSlope / numerator).Real - (denominatorSlope / denominator).Real;
    }

    // Above the ~1e-16 residue of an exact zero, far below the ~1e-5 a real stopband carries.
    private const double ZeroOnTheUnitCircle = 1e-12;
}
