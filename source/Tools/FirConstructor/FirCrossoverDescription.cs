using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Single wording source so the Virtual DSP block, constructor and sheet describe a kernel the same way.</summary>
internal static class FirCrossoverDescription
{
    /// <summary>E.g. "LP 2 kHz", "BP 80 Hz–3 kHz".</summary>
    public static string Short(FirCrossoverDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.Kind switch
        {
            CrossoverKind.LowPass => $"LP {Hz(design.LowPassEdge.FrequencyHz)}",
            CrossoverKind.HighPass => $"HP {Hz(design.HighPassEdge.FrequencyHz)}",
            CrossoverKind.BandPass =>
                $"BP {Hz(design.HighPassEdge.FrequencyHz)}–{Hz(design.LowPassEdge.FrequencyHz)}",
            _ => "FIR"
        };
    }

    public static string Long(FirCrossoverDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        string corners = design.Kind switch
        {
            CrossoverKind.LowPass => $"low-pass {Edge(design, design.LowPassEdge)}",
            CrossoverKind.HighPass => $"high-pass {Edge(design, design.HighPassEdge)}",
            CrossoverKind.BandPass =>
                $"band-pass: high-pass {Edge(design, design.HighPassEdge)}, " +
                $"low-pass {Edge(design, design.LowPassEdge)}",
            _ => "no crossover"
        };
        string method = design.Method == FirCrossoverMethod.WindowedSinc ? "windowed sinc" : "IIR magnitude";
        return $"Linear-phase {corners} ({method}, {Window(design)}), " +
            $"{design.TapCount} taps at {Rate(design.SampleRateHz)}";
    }

    public static string Window(FirCrossoverDesign design) =>
        design.Window switch
        {
            FirWindow.Kaiser => $"Kaiser β {design.KaiserBeta.ToString("0.#", CultureInfo.InvariantCulture)}",
            FirWindow.Rectangular => "rectangular window",
            _ => $"{design.Window} window"
        };

    public static string FamilyName(CrossoverFilterFamily family) => family switch
    {
        CrossoverFilterFamily.LinkwitzRiley => "Linkwitz-Riley",
        CrossoverFilterFamily.Bessel => "Bessel",
        CrossoverFilterFamily.Chebyshev => "Chebyshev",
        _ => "Butterworth"
    };

    public static string Rate(int sampleRateHz) =>
        (sampleRateHz / 1_000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";

    private static string Edge(FirCrossoverDesign design, CrossoverEdge edge) =>
        design.Method == FirCrossoverMethod.WindowedSinc
            ? $"{Hz(edge.FrequencyHz)}"
            : $"{Hz(edge.FrequencyHz)} {FamilyName(edge.Family)} {edge.SlopeDbPerOctave} dB/oct";

    private static string Hz(double frequencyHz) =>
        frequencyHz >= 1_000
            ? (frequencyHz / 1_000).ToString("0.##", CultureInfo.InvariantCulture) + " kHz"
            : frequencyHz.ToString("0.#", CultureInfo.InvariantCulture) + " Hz";
}
