using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>The all-pass stage of a channel.</summary>
public enum AllPassType
{
    /// <summary>
    /// No all-pass. Deliberately the zero value, so a project written before the
    /// stage existed deserializes to "no all-pass" instead of a live filter.
    /// </summary>
    Off,

    /// <summary>One real pole: 180° of phase swing, -90° at the corner. Takes no Q.</summary>
    FirstOrder,

    /// <summary>A pole pair: 360° of phase swing, -180° at the corner, width set by Q.</summary>
    SecondOrder
}

/// <summary>
/// One all-pass filter: unity magnitude at every frequency, phase rotated around
/// <see cref="FrequencyHz"/>. It is the only stage that moves phase without touching
/// the tonal balance — unlike a delay (a constant group delay everywhere) or a
/// polarity flip (180° everywhere), it rotates phase *locally*, which is what makes
/// it the tool for lining up drivers through a crossover region.
/// <see cref="Q"/> is read only by <see cref="AllPassType.SecondOrder"/>: it sets how
/// abruptly the phase turns, and therefore how much group delay piles up at the corner
/// (τ ≈ 4Q/ω₀). A first-order section has a single real pole and no Q at all.
/// </summary>
public sealed record AllPassSpec(
    AllPassType Type,
    double FrequencyHz,
    double Q = 1.0);

public static class AllPassFilter
{
    /// <summary>
    /// Complex response of the all-pass at the given frequency. An Off stage is unity;
    /// otherwise the magnitude is 1 at every frequency and only the phase moves.
    /// </summary>
    public static Complex Response(
        AllPassSpec spec,
        double frequencyHz,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Evaluate(BuildSections(spec, sampleRateHz), frequencyHz, sampleRateHz);
    }

    /// <summary>
    /// The digital biquad realizing this all-pass, in the same coefficient convention
    /// a miniDSP-style device runs. An Off stage builds nothing.
    /// </summary>
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
        // Q has no upper bound to enforce — an extreme Q is merely a very sharp phase
        // turn, still perfectly stable. Zero or negative, though, divides by zero in
        // alpha and poisons every coefficient, so the DSP refuses it rather than trust
        // the UI to have clamped: an imported or hand-edited project might not have.
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

    /// <summary>
    /// Group delay (seconds) the all-pass adds at <paramref name="frequencyHz"/>, read
    /// from the exact digital biquad rather than the analog ideal (τ = 4Q/ω₀), which the
    /// bilinear transform's frequency warping pulls away from as the corner climbs
    /// toward Nyquist. Zero for an Off stage.
    /// </summary>
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

    /// <summary>
    /// Group delay (seconds) at the stage's own corner — where a second-order section's
    /// delay peaks, and the figure worth showing a user: it is why an all-pass works,
    /// and on a low corner, where it runs to many milliseconds, its main risk.
    /// <para>
    /// Evaluated at the corner the filter ACTUALLY runs at. <see cref="BuildSections"/>
    /// clamps a corner at or above Nyquist below it, so reading the delay at the
    /// requested frequency instead would miss the peak — and near Nyquist that peak is
    /// enormous (a Q of 20 just under Nyquist holds a quarter of a second), which is
    /// precisely where a readout must not understate.
    /// </para>
    /// </summary>
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

    // RBJ cookbook all-pass biquad (bilinear transform, prewarped at the corner),
    // normalized to a0 = 1 with a1/a2 negated for the additive-feedback convention of
    // BiquadCoefficients. The numerator is the denominator reversed (b0 = a2, b1 = a1,
    // b2 = 1) — that mirror symmetry is what makes |H| exactly 1 at every frequency,
    // analytically rather than approximately.
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
        const double b2 = 1.0; // (1 + alpha) / a0
        double a1 = (-2.0 * cos) / a0;
        double a2 = (1.0 - alpha) / a0;
        return new BiquadCoefficients(b0, b1, b2, -a1, -a2);
    }

    // First-order all-pass via the bilinear transform of H(s) = (w0 - s)/(w0 + s):
    // with K = tan(pi f / fs) it collapses to H(z) = (a + z^-1)/(1 + a z^-1), stored as
    // a biquad with zero second-order terms. Here too the numerator is the denominator
    // reversed, so the magnitude is exactly 1.
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
