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
    ThdPlusNoise
}

/// <summary>
/// Contains one named analysis curve produced by the DSP layer.
/// </summary>
public sealed record AnalysisCurve(
    string Name,
    IReadOnlyList<SignalPoint> Points,
    AnalysisCurveKind Kind = AnalysisCurveKind.Primary);
