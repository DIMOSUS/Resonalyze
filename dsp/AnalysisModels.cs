namespace Resonalyze.Dsp;

/// <summary>
/// Represents a numeric sample without coupling DSP code to a plotting framework.
/// </summary>
public readonly record struct SignalPoint(double X, double Y);

/// <summary>
/// Describes the semantic role of an analysis curve.
/// Presentation layers may use this value to select colors and line styles.
/// </summary>
public enum AnalysisCurveKind
{
    Primary,
    SecondHarmonic,
    ThirdHarmonic,
    FourthHarmonic,
    ThdPlusNoise,
    MinimumPhase,
    ExcessPhase
}

/// <summary>
/// Selects which frequency-response curves <see cref="DataHelper.GetSpectrum"/>
/// computes. The DSP layer takes this instead of reading presentation-layer
/// visibility flags: callers translate their own "show" state (and any
/// computational scoping) into this set.
/// </summary>
[System.Flags]
public enum SpectrumCurves
{
    None = 0,
    Primary = 1 << 0,
    SecondHarmonic = 1 << 1,
    ThirdHarmonic = 1 << 2,
    FourthHarmonic = 1 << 3,
    ThdPlusNoise = 1 << 4,
    Harmonics = SecondHarmonic | ThirdHarmonic | FourthHarmonic | ThdPlusNoise,
    All = Primary | Harmonics
}

/// <summary>
/// Contains one named analysis curve produced by the DSP layer.
/// </summary>
public sealed record AnalysisCurve(
    string Name,
    IReadOnlyList<SignalPoint> Points,
    AnalysisCurveKind Kind = AnalysisCurveKind.Primary);
