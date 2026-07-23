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
    ExcessPhase,
    // The measurement noise floor, shown as its own trace next to the distortion
    // curves (REW-style) rather than fused into a single THD+N number.
    NoiseFloor,
    // The reference-free input spectrum (RTA): the microphone's own spectrum, with
    // no loopback reference behind it. Unlike a transfer function it carries an
    // absolute level, which is what makes a moving-microphone RTA average usable as
    // an equalization source.
    InputSpectrum
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
    // The measurement noise floor, selectable on its own (it is not a harmonic and
    // no longer rides on the THD flag).
    NoiseFloor = 1 << 5,
    Harmonics = SecondHarmonic | ThirdHarmonic | FourthHarmonic | ThdPlusNoise,
    // Everything derived from the sweep deconvolution — harmonics AND the noise
    // floor. The ESS decomposition is computed when any of these is requested.
    Distortion = Harmonics | NoiseFloor,
    All = Primary | Distortion
}

/// <summary>
/// Contains one named analysis curve produced by the DSP layer.
/// </summary>
public sealed record AnalysisCurve(
    string Name,
    IReadOnlyList<SignalPoint> Points,
    AnalysisCurveKind Kind = AnalysisCurveKind.Primary);
