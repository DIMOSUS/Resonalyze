using System.Numerics;

namespace Resonalyze.Dsp;

public enum CrossoverFilterFamily
{
    Butterworth,
    LinkwitzRiley,
    Bessel,
    Chebyshev
}

public enum CrossoverKind
{
    Off,
    LowPass,
    HighPass,
    BandPass
}

/// <summary>
/// One crossover slope: the filter family, the corner frequency and the rolloff
/// steepness. An edge is either the low-pass or the high-pass side of a crossover;
/// a band-pass carries one of each with independent settings.
/// <see cref="RippleDb"/> is the passband ripple used only by the
/// <see cref="CrossoverFilterFamily.Chebyshev"/> family; every other family ignores it.
/// </summary>
public readonly record struct CrossoverEdge(
    CrossoverFilterFamily Family,
    double FrequencyHz,
    int SlopeDbPerOctave,
    double RippleDb = 1.0);

/// <summary>
/// A virtual crossover for one channel: off, a single low-pass or high-pass edge,
/// or a band-pass combining a high-pass (lower corner) and a low-pass (upper
/// corner). Only the edges the kind requires are read.
/// </summary>
public sealed record CrossoverSpec(
    CrossoverKind Kind,
    CrossoverEdge? LowPassEdge = null,
    CrossoverEdge? HighPassEdge = null)
{
    public static CrossoverSpec Off { get; } = new(CrossoverKind.Off);
}

public static class CrossoverFilter
{
    /// <summary>
    /// The slopes each family offers, matching common DSP hardware. Linkwitz-Riley
    /// filters only exist in even orders built from a squared Butterworth, and DSPs
    /// ship the 12/24/48 variants. Bessel is realized from a fixed -3 dB prototype
    /// table that has no 5th/7th-order entry, so it omits the odd 30/42 dB/oct orders.
    /// Butterworth and Chebyshev are computed for any order and offer the full set.
    /// </summary>
    public static IReadOnlyList<int> SupportedSlopes(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => [12, 24, 48],
        CrossoverFilterFamily.Bessel => [6, 12, 18, 24, 36, 48],
        _ => [6, 12, 18, 24, 30, 36, 42, 48]
    };

    /// <summary>
    /// Complex response of the crossover at the given frequency — the product of
    /// its edges (band-pass multiplies both). An Off crossover is unity.
    /// </summary>
    public static Complex Response(
        CrossoverSpec spec,
        double frequencyHz,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Complex response = Complex.One;
        if (spec.Kind is CrossoverKind.LowPass or CrossoverKind.BandPass)
        {
            CrossoverEdge edge = spec.LowPassEdge
                ?? throw new InvalidOperationException(
                    "The crossover kind requires a low-pass edge.");
            response *= EdgeResponse(edge, highPass: false, frequencyHz, sampleRateHz);
        }
        if (spec.Kind is CrossoverKind.HighPass or CrossoverKind.BandPass)
        {
            CrossoverEdge edge = spec.HighPassEdge
                ?? throw new InvalidOperationException(
                    "The crossover kind requires a high-pass edge.");
            response *= EdgeResponse(edge, highPass: true, frequencyHz, sampleRateHz);
        }

        return response;
    }

    /// <summary>
    /// The peak group delay (seconds) this crossover edge adds, read from the
    /// exact digital biquad cascade. Group delay τ(f) = −dφ/dω is sampled around
    /// the corner — where a crossover's delay peaks — and the maximum returned:
    /// the figure that bounds how much a steep low-frequency slope smears the
    /// arrival. It scales as ≈ 1/f_c for a fixed order, and is the same for the
    /// low-pass and high-pass sides, so callers may pass either.
    /// </summary>
    public static double MaxGroupDelaySeconds(
        CrossoverEdge edge,
        bool highPass,
        double sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (!(edge.FrequencyHz > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                "The crossover corner frequency must be positive.");
        }

        CrossoverSpec spec = highPass
            ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
            : new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge);

        // An octave-and-a-half either side of the corner captures the peak. The
        // finite-difference step is small relative to the frequency, keeping the
        // phase change per step well under π so no unwrapping is needed.
        double nyquist = sampleRateHz / 2.0;
        double lo = edge.FrequencyHz / 4.0;
        double hi = Math.Min(edge.FrequencyHz * 4.0, nyquist * 0.99);
        const int steps = 400;
        double max = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double f = lo * Math.Pow(hi / lo, (double)i / steps);
            double delta = f * 1e-3;
            if (f - delta <= 0 || f + delta >= nyquist)
            {
                continue;
            }

            Complex below = Response(spec, f - delta, sampleRateHz);
            Complex above = Response(spec, f + delta, sampleRateHz);
            // Arg(H(f−δ)·conj(H(f+δ))) = φ(f−δ) − φ(f+δ), the principal-value
            // phase increase over 2·δ Hz; τ = that / (2π·2δ).
            double phaseIncrease = (below * Complex.Conjugate(above)).Phase;
            double groupDelay = phaseIncrease / (2.0 * Math.PI * 2.0 * delta);
            if (groupDelay > max)
            {
                max = groupDelay;
            }
        }

        return max;
    }

    private static Complex EdgeResponse(
        CrossoverEdge edge,
        bool highPass,
        double frequencyHz,
        double sampleRateHz)
    {
        Complex response = Complex.One;
        foreach (BiquadCoefficients section in BuildSections(edge, highPass, sampleRateHz))
        {
            response *= BiquadResponse.Evaluate(section, frequencyHz, sampleRateHz);
        }

        return response;
    }

    /// <summary>
    /// The digital biquad cascade realizing one crossover edge, in the same
    /// coefficient convention a miniDSP-style device runs. A Butterworth of order n
    /// is its canonical second-order sections (Q from the pole angles) plus one
    /// first-order section when n is odd; a Linkwitz-Riley of order n is the
    /// Butterworth of order n/2 cascaded twice.
    /// </summary>
    public static IReadOnlyList<BiquadCoefficients> BuildSections(
        CrossoverEdge edge,
        bool highPass,
        double sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (!(edge.FrequencyHz > 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                "The crossover corner frequency must be positive.");
        }
        if (!SupportedSlopes(edge.Family).Contains(edge.SlopeDbPerOctave))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                $"A {edge.Family} crossover does not support " +
                $"{edge.SlopeDbPerOctave} dB/octave.");
        }

        var sections = new List<BiquadCoefficients>();
        int order = edge.SlopeDbPerOctave / 6;
        switch (edge.Family)
        {
            case CrossoverFilterFamily.LinkwitzRiley:
                // LR(n) = BW(n/2) squared: the same Butterworth cascade twice.
                AppendButterworth(sections, order / 2, edge.FrequencyHz, highPass, sampleRateHz);
                AppendButterworth(sections, order / 2, edge.FrequencyHz, highPass, sampleRateHz);
                break;
            case CrossoverFilterFamily.Bessel:
                AppendBessel(sections, order, edge.FrequencyHz, highPass, sampleRateHz);
                break;
            case CrossoverFilterFamily.Chebyshev:
                AppendChebyshev(
                    sections, order, edge.FrequencyHz, edge.RippleDb, highPass, sampleRateHz);
                break;
            default:
                AppendButterworth(sections, order, edge.FrequencyHz, highPass, sampleRateHz);
                break;
        }

        return sections;
    }

    private static void AppendButterworth(
        List<BiquadCoefficients> sections,
        int order,
        double frequencyHz,
        bool highPass,
        double sampleRateHz)
    {
        // Butterworth pole pairs: for k = 1..n/2 the section quality is
        // Q = 1 / (2 sin((2k - 1) pi / (2n))); an odd order adds one real pole.
        for (int k = 1; k <= order / 2; k++)
        {
            double q = 1.0 / (2.0 * Math.Sin((2 * k - 1) * Math.PI / (2.0 * order)));
            sections.Add(SecondOrderSection(frequencyHz, q, highPass, sampleRateHz));
        }
        if (order % 2 == 1)
        {
            sections.Add(FirstOrderSection(frequencyHz, highPass, sampleRateHz));
        }
    }

    private static void AppendBessel(
        List<BiquadCoefficients> sections,
        int order,
        double frequencyHz,
        bool highPass,
        double sampleRateHz)
    {
        // Unlike Butterworth, every Bessel section sits at its own scaled corner:
        // frequency scale factor (FSF) times the cutoff for the low-pass, cutoff
        // divided by FSF for the high-pass (the s -> 1/s transform inverts it).
        ((double Fsf, double Q)[] pairs, double? realFsf) = BesselPrototype(order);
        foreach ((double fsf, double q) in pairs)
        {
            double sectionHz = highPass ? frequencyHz / fsf : frequencyHz * fsf;
            sections.Add(SecondOrderSection(sectionHz, q, highPass, sampleRateHz));
        }
        if (realFsf is { } real)
        {
            double sectionHz = highPass ? frequencyHz / real : frequencyHz * real;
            sections.Add(FirstOrderSection(sectionHz, highPass, sampleRateHz));
        }
    }

    private static void AppendChebyshev(
        List<BiquadCoefficients> sections,
        int order,
        double frequencyHz,
        double rippleDb,
        bool highPass,
        double sampleRateHz)
    {
        // Chebyshev Type I: the poles lie on an ellipse set by the passband ripple.
        // ε from the ripple; a scales the ellipse. The natural prototype is normalized
        // to the ripple (passband) edge, so every section is scaled by the prototype's
        // own -3 dB frequency (ω3) — that makes the corner the user enters land at
        // -3 dB, matching Butterworth/Bessel here. A section sits at radius/ω3 times the
        // cutoff (divided, for the high-pass, by the s -> 1/s inversion), same as Bessel.
        double ripple = Math.Max(rippleDb, 1e-3);
        double epsilon = Math.Sqrt(Math.Pow(10.0, ripple / 10.0) - 1.0);
        double a = Math.Asinh(1.0 / epsilon) / order;
        double sinhA = Math.Sinh(a);
        double coshA = Math.Cosh(a);
        double omega3 = Math.Cosh(Math.Acosh(1.0 / epsilon) / order);

        int firstIndex = sections.Count;
        for (int k = 1; k <= order / 2; k++)
        {
            double theta = (2 * k - 1) * Math.PI / (2.0 * order);
            double sigma = -sinhA * Math.Sin(theta); // pole real part (< 0)
            double omega = coshA * Math.Cos(theta);  // pole imaginary part
            double radius = Math.Sqrt(sigma * sigma + omega * omega);
            double q = radius / (-2.0 * sigma);
            double fsf = radius / omega3;
            double sectionHz = highPass ? frequencyHz / fsf : frequencyHz * fsf;
            sections.Add(SecondOrderSection(sectionHz, q, highPass, sampleRateHz));
        }
        if (order % 2 == 1)
        {
            // The odd-order real pole sits at s = -sinh(a); scale it the same way.
            double fsf = sinhA / omega3;
            double sectionHz = highPass ? frequencyHz / fsf : frequencyHz * fsf;
            sections.Add(FirstOrderSection(sectionHz, highPass, sampleRateHz));
        }
        else
        {
            // The RBJ sections each normalize their passband edge (DC for a low-pass,
            // Nyquist for a high-pass) to unity, but an even-order Chebyshev's gain
            // there is -ripple dB — odd orders already pass through 0 dB. Pull the whole
            // cascade down by that ripple so the passband equiripples in [-ripple, 0] dB
            // instead of [0, +ripple].
            double gain = Math.Pow(10.0, -ripple / 20.0);
            sections[firstIndex] = ScaleGain(sections[firstIndex], gain);
        }
    }

    // Scales a biquad's feed-forward path, changing its gain without moving its poles.
    private static BiquadCoefficients ScaleGain(BiquadCoefficients section, double gain) =>
        section with
        {
            B0 = section.B0 * gain,
            B1 = section.B1 * gain,
            B2 = section.B2 * gain
        };

    // The Bessel analog prototype normalized to the -3 dB frequency (TI SLOA049):
    // per order the second-order sections as (frequency scale factor, Q) plus the
    // real pole's scale factor for odd orders.
    private static ((double Fsf, double Q)[] Pairs, double? RealFsf) BesselPrototype(
        int order) => order switch
    {
        1 => ([], 1.0),
        2 => ([(1.2736, 0.5773)], null),
        3 => ([(1.4524, 0.6910)], 1.3270),
        4 => ([(1.4192, 0.5219), (1.5912, 0.8055)], null),
        6 => ([(1.6060, 0.5103), (1.6913, 0.6112), (1.9071, 1.0234)], null),
        8 => ([(1.7837, 0.5060), (1.8376, 0.5596), (1.9591, 0.7109), (2.1953, 1.2258)], null),
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };

    // A section corner at or above Nyquist cannot be realized by the bilinear
    // transform (the prewarp tangent blows up), so it is clamped just below —
    // the same way DSP hardware limits its frequency entry. Applied per section
    // because Bessel scale factors can push a section past Nyquist even when the
    // nominal cutoff itself is fine.
    private static double ClampBelowNyquist(double frequencyHz, double sampleRateHz) =>
        Math.Min(frequencyHz, sampleRateHz * 0.499);

    // RBJ cookbook LP/HP biquad (bilinear transform, prewarped at the corner),
    // normalized to a0 = 1 with a1/a2 negated for the additive-feedback convention
    // of BiquadCoefficients.
    private static BiquadCoefficients SecondOrderSection(
        double frequencyHz,
        double q,
        bool highPass,
        double sampleRateHz)
    {
        double w0 = Math.Tau * ClampBelowNyquist(frequencyHz, sampleRateHz) / sampleRateHz;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);
        double a0 = 1.0 + alpha;

        double b0;
        double b1;
        if (highPass)
        {
            b0 = (1.0 + cos) / 2.0 / a0;
            b1 = -(1.0 + cos) / a0;
        }
        else
        {
            b0 = (1.0 - cos) / 2.0 / a0;
            b1 = (1.0 - cos) / a0;
        }

        double a1 = (-2.0 * cos) / a0;
        double a2 = (1.0 - alpha) / a0;
        return new BiquadCoefficients(b0, b1, b0, -a1, -a2);
    }

    // First-order LP/HP via the bilinear transform with K = tan(pi f / fs), stored
    // as a biquad with zero second-order terms.
    private static BiquadCoefficients FirstOrderSection(
        double frequencyHz,
        bool highPass,
        double sampleRateHz)
    {
        double k = Math.Tan(
            Math.PI * ClampBelowNyquist(frequencyHz, sampleRateHz) / sampleRateHz);
        double a0 = k + 1.0;

        double b0 = highPass ? 1.0 / a0 : k / a0;
        double b1 = highPass ? -1.0 / a0 : k / a0;
        double a1 = (k - 1.0) / a0;
        return new BiquadCoefficients(b0, b1, 0.0, -a1, 0.0);
    }
}
