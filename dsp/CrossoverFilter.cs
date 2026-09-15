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

/// <summary><see cref="RippleDb"/> is read only by <see cref="CrossoverFilterFamily.Chebyshev"/>.</summary>
public readonly record struct CrossoverEdge(
    CrossoverFilterFamily Family,
    double FrequencyHz,
    int SlopeDbPerOctave,
    double RippleDb = 1.0);

/// <summary>Band-pass = high-pass lower corner + low-pass upper corner; only the edges the kind requires are read.</summary>
public sealed record CrossoverSpec(
    CrossoverKind Kind,
    CrossoverEdge? LowPassEdge = null,
    CrossoverEdge? HighPassEdge = null)
{
    public static CrossoverSpec Off { get; } = new(CrossoverKind.Off);
}

public static class CrossoverFilter
{
    /// <summary>Above 10·log10(2) dB the prototype's acosh(1/ε) is undefined (NaN).</summary>
    public const double MaximumChebyshevRippleDb = 3.0;

    /// <summary>LR only in even orders (squared BW, 12..48); Bessel's prototype table has no 5th/7th order.</summary>
    public static IReadOnlyList<int> SupportedSlopes(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => [12, 24, 36, 48],
        CrossoverFilterFamily.Bessel => [6, 12, 18, 24, 36, 48],
        _ => [6, 12, 18, 24, 30, 36, 42, 48]
    };

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

    /// <summary>Peak group delay (seconds) around the corner from the exact cascade; same for LP and HP.</summary>
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

        // ±2 octaves captures the peak; closed form avoids aliasing a narrow peak near Nyquist.
        IReadOnlyList<BiquadCoefficients> sections =
            BuildSections(edge, highPass, sampleRateHz);
        double lo = edge.FrequencyHz / 4.0;
        double hi = Math.Min(edge.FrequencyHz * 4.0, sampleRateHz / 2.0 * 0.99);
        const int steps = 400;
        double max = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            double frequency = lo * Math.Pow(hi / lo, (double)i / steps);
            double samples = 0;
            foreach (BiquadCoefficients section in sections)
            {
                samples += BiquadResponse.GroupDelaySamples(
                    section, frequency, sampleRateHz);
            }

            max = Math.Max(max, samples / sampleRateHz);
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

    /// <summary>Biquad cascade for one edge. LR(n) = BW(n/2) twice; when n/2 is odd (LR12, LR36) LP and HP are 180° apart,
    /// so flat summation needs the channel's polarity inverted (not touched here).</summary>
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
        // Refuse here: imported or hand-edited projects may bypass UI clamping.
        if (edge.Family == CrossoverFilterFamily.Chebyshev &&
            !(edge.RippleDb > 0 && edge.RippleDb <= MaximumChebyshevRippleDb))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                $"A Chebyshev crossover ripple must be in (0, {MaximumChebyshevRippleDb}] dB.");
        }

        var sections = new List<BiquadCoefficients>();
        int order = edge.SlopeDbPerOctave / 6;
        switch (edge.Family)
        {
            case CrossoverFilterFamily.LinkwitzRiley:
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
        // Q = 1 / (2 sin((2k - 1) pi / (2n))); odd order adds one real pole.
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
        // Each Bessel section has its own FSF, applied prewarped; divided for high-pass (s -> 1/s).
        ((double Fsf, double Q)[] pairs, double? realFsf) = BesselPrototype(order);
        foreach ((double fsf, double q) in pairs)
        {
            double sectionHz = ScaledSectionHz(frequencyHz, fsf, highPass, sampleRateHz);
            sections.Add(SecondOrderSection(sectionHz, q, highPass, sampleRateHz));
        }
        if (realFsf is { } real)
        {
            double sectionHz = ScaledSectionHz(frequencyHz, real, highPass, sampleRateHz);
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
        // Chebyshev I: radii scaled by the prototype's -3 dB frequency so the entered corner lands at -3 dB like other families.
        double epsilon = Math.Sqrt(Math.Pow(10.0, rippleDb / 10.0) - 1.0);
        double a = Math.Asinh(1.0 / epsilon) / order;
        double sinhA = Math.Sinh(a);
        double coshA = Math.Cosh(a);
        double omega3 = Math.Cosh(Math.Acosh(1.0 / epsilon) / order);

        int firstIndex = sections.Count;
        for (int k = 1; k <= order / 2; k++)
        {
            double theta = (2 * k - 1) * Math.PI / (2.0 * order);
            double sigma = -sinhA * Math.Sin(theta);
            double omega = coshA * Math.Cos(theta);
            double radius = Math.Sqrt(sigma * sigma + omega * omega);
            double q = radius / (-2.0 * sigma);
            double sectionHz = ScaledSectionHz(
                frequencyHz, radius / omega3, highPass, sampleRateHz);
            sections.Add(SecondOrderSection(sectionHz, q, highPass, sampleRateHz));
        }
        if (order % 2 == 1)
        {
            double sectionHz = ScaledSectionHz(
                frequencyHz, sinhA / omega3, highPass, sampleRateHz);
            sections.Add(FirstOrderSection(sectionHz, highPass, sampleRateHz));
        }
        else
        {
            // Even orders sit at -ripple at the RBJ-normalized passband edge: shift down so ripple spans [-ripple, 0] dB.
            double gain = Math.Pow(10.0, -rippleDb / 20.0);
            sections[firstIndex] = ScaleGain(sections[firstIndex], gain);
        }
    }

    private static BiquadCoefficients ScaleGain(BiquadCoefficients section, double gain) =>
        section with
        {
            B0 = section.B0 * gain,
            B1 = section.B1 * gain,
            B2 = section.B2 * gain
        };

    // FSF applied in the prewarped domain: a direct digital multiply lands many dB off near the top of the band.
    private static double ScaledSectionHz(
        double cornerHz, double fsf, bool highPass, double sampleRateHz)
    {
        double corner = BilinearTransform.ClampBelowNyquist(cornerHz, sampleRateHz);
        double warpedCorner = Math.Tan(Math.PI * corner / sampleRateHz);
        double warpedSection = highPass ? warpedCorner / fsf : warpedCorner * fsf;
        return sampleRateHz / Math.PI * Math.Atan(warpedSection);
    }

    // Bessel prototype normalized to -3 dB (TI SLOA049): (FSF, Q) pairs plus the odd real pole's FSF.
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

    // RBJ LP/HP, a1/a2 negated for BiquadCoefficients' additive-feedback convention.
    private static BiquadCoefficients SecondOrderSection(
        double frequencyHz,
        double q,
        bool highPass,
        double sampleRateHz)
    {
        double w0 = Math.Tau * BilinearTransform.ClampBelowNyquist(frequencyHz, sampleRateHz) / sampleRateHz;
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

    private static BiquadCoefficients FirstOrderSection(
        double frequencyHz,
        bool highPass,
        double sampleRateHz)
    {
        double k = Math.Tan(
            Math.PI * BilinearTransform.ClampBelowNyquist(frequencyHz, sampleRateHz) / sampleRateHz);
        double a0 = k + 1.0;

        double b0 = highPass ? 1.0 / a0 : k / a0;
        double b1 = highPass ? -1.0 / a0 : k / a0;
        double a1 = (k - 1.0) / a0;
        return new BiquadCoefficients(b0, b1, 0.0, -a1, 0.0);
    }
}
