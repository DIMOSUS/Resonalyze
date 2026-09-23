using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The constructor's pick lists as values with their labels, and the rules that keep a field on them: the slope
/// nearest the one wanted, the family a design falls back to, and odd tap counts.</summary>
internal static class FirConstructorChoices
{
    public const int DefaultRateHz = 48_000;

    public const int DefaultSlope = 24;

    public static IReadOnlyList<(CrossoverKind Value, string Label)> Kinds { get; } =
    [
        (CrossoverKind.LowPass, "Low pass"),
        (CrossoverKind.HighPass, "High pass"),
        (CrossoverKind.BandPass, "Band pass")
    ];

    public static IReadOnlyList<(FirCrossoverMethod Value, string Label)> Methods { get; } =
    [
        (FirCrossoverMethod.IirMagnitude, "IIR magnitude"),
        (FirCrossoverMethod.WindowedSinc, "Windowed sinc")
    ];

    public static IReadOnlyList<(CrossoverFilterFamily Value, string Label)> Families { get; } =
        FirCrossoverDesign.IirFamilies.Select(family => (family, FirCrossoverDescription.FamilyName(family))).ToArray();

    public static IReadOnlyList<(FirWindow Value, string Label)> Windows { get; } =
        Enum.GetValues<FirWindow>().Select(window => (window, window.ToString())).ToArray();

    /// <summary>The rates offered standalone; a handoff adds its processor's when it is not among them.</summary>
    public static IReadOnlyList<int> SampleRates { get; } = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    public static string RateLabel(int rateHz) => FirCrossoverDescription.Rate(rateHz);

    public static IReadOnlyList<(int Value, string Label)> Slopes(CrossoverFilterFamily family) =>
        FirCrossoverDesign.SupportedSlopes(family).Select(value => (value, $"{value} dB/oct")).ToArray();

    public static int NearestSlope(CrossoverFilterFamily family, int preferred) =>
        FirCrossoverDesign.SupportedSlopes(family).OrderBy(value => Math.Abs(value - preferred)).First();

    /// <summary>A design's family when the constructor offers it, Linkwitz-Riley otherwise.</summary>
    public static CrossoverFilterFamily OfferedFamily(CrossoverFilterFamily family) =>
        FirCrossoverDesign.IirFamilies.Contains(family) ? family : CrossoverFilterFamily.LinkwitzRiley;

    /// <summary>Only a typed count can be even (the arrows step by two); it moves up to the odd one above, or down at the
    /// limit.</summary>
    public static int OddTapCount(int taps) =>
        taps % 2 != 0 ? taps : taps + 1 <= FirCrossoverDesign.MaximumTapCount ? taps + 1 : taps - 1;
}
