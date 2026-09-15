using System.Numerics;

namespace Resonalyze.Dsp;

public enum AllPassType
{
    /// <summary>Zero value so pre-v8 projects that never dialled an all-pass deserialize to none.</summary>
    Off,

    /// <summary>180° swing, -90° at the corner; no Q.</summary>
    FirstOrder,

    /// <summary>360° swing, -180° at the corner; width set by Q.</summary>
    SecondOrder
}

/// <summary>Unity magnitude, phase rotated locally around <see cref="FrequencyHz"/>. <see cref="Q"/> (second order only) sets corner group delay τ ≈ 4Q/ω₀.</summary>
public sealed record AllPassSpec(
    AllPassType Type,
    double FrequencyHz,
    double Q = 1.0);

public static class AllPassFilter
{
    public static Complex Response(
        AllPassSpec spec,
        double frequencyHz,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Evaluate(BuildSections(spec, sampleRateHz), frequencyHz, sampleRateHz);
    }

    public static IReadOnlyList<BiquadCoefficients> BuildSections(
        AllPassSpec spec,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (spec.Type == AllPassType.Off)
        {
            return [];
        }
        if (!Enum.IsDefined(spec.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "The all-pass type is invalid.");
        }
        if (!double.IsFinite(spec.FrequencyHz) || spec.FrequencyHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec),
                "The all-pass corner frequency must be positive.");
        }
        // No upper Q bound needed; Q <= 0 divides by zero, refused here since imported projects may bypass UI clamps.
        if (spec.Type == AllPassType.SecondOrder &&
            (!double.IsFinite(spec.Q) || spec.Q <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec),
                "A second-order all-pass Q must be positive.");
        }

        return spec.Type == AllPassType.FirstOrder
            ? [FirstOrderSection(spec.FrequencyHz, sampleRateHz)]
            : [SecondOrderSection(spec.FrequencyHz, spec.Q, sampleRateHz)];
    }

    /// <summary>From the exact digital biquad; the analog 4Q/ω₀ drifts with bilinear warping near Nyquist.</summary>
    public static double GroupDelaySeconds(
        AllPassSpec spec,
        double frequencyHz,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        double samples = 0;
        foreach (BiquadCoefficients section in BuildSections(spec, sampleRateHz))
        {
            samples += BiquadResponse.GroupDelaySamples(section, frequencyHz, sampleRateHz);
        }

        return samples / sampleRateHz;
    }

    /// <summary>Group delay at the corner the filter actually runs at (BuildSections clamps below Nyquist), so the peak is never understated.</summary>
    public static double CornerGroupDelaySeconds(AllPassSpec spec, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return GroupDelaySeconds(
            spec,
            BilinearTransform.ClampBelowNyquist(spec.FrequencyHz, sampleRateHz),
            sampleRateHz);
    }

    private static Complex Evaluate(
        IReadOnlyList<BiquadCoefficients> sections,
        double frequencyHz,
        double sampleRateHz)
    {
        Complex response = Complex.One;
        foreach (BiquadCoefficients section in sections)
        {
            response *= BiquadResponse.Evaluate(section, frequencyHz, sampleRateHz);
        }

        return response;
    }

    // RBJ all-pass, a1/a2 negated for the additive convention; numerator = reversed denominator gives |H| = 1 exactly.
    private static BiquadCoefficients SecondOrderSection(
        double frequencyHz,
        double q,
        double sampleRateHz)
    {
        double w0 = Math.Tau *
            BilinearTransform.ClampBelowNyquist(frequencyHz, sampleRateHz) / sampleRateHz;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);
        double a0 = 1.0 + alpha;

        double b0 = (1.0 - alpha) / a0;
        double b1 = (-2.0 * cos) / a0;
        const double b2 = 1.0;
        double a1 = (-2.0 * cos) / a0;
        double a2 = (1.0 - alpha) / a0;
        return new BiquadCoefficients(b0, b1, b2, -a1, -a2);
    }

    // H(z) = (a + z^-1)/(1 + a z^-1) with K = tan(pi f / fs); mirrored coefficients keep |H| = 1.
    private static BiquadCoefficients FirstOrderSection(
        double frequencyHz,
        double sampleRateHz)
    {
        double k = Math.Tan(
            Math.PI * BilinearTransform.ClampBelowNyquist(frequencyHz, sampleRateHz) /
            sampleRateHz);
        double a = (k - 1.0) / (k + 1.0);
        return new BiquadCoefficients(a, 1.0, 0.0, -a, 0.0);
    }
}
