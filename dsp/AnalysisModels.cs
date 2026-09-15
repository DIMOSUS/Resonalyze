using System.Numerics;

namespace Resonalyze.Dsp;

public readonly record struct SignalPoint(double X, double Y);

public enum AnalysisCurveKind
{
    Primary,
    SecondHarmonic,
    ThirdHarmonic,
    FourthHarmonic,
    ThdPlusNoise,
    MinimumPhase,
    ExcessPhase,
    NoiseFloor,
    // Reference-free RTA: carries absolute level, usable as an EQ source.
    InputSpectrum,
    // Kinds are persisted in overlay files: append only, never reorder.
    MinimumPhaseGroupDelay,
    ExcessGroupDelay,
    ImpulseEnvelope,
    ImpulseStep,
    ArrayAverage,
    ArrayMicrophone,
    // A dB range: own axis, smoothed as a ratio.
    ArraySpread
}

[System.Flags]
public enum SpectrumCurves
{
    None = 0,
    Primary = 1 << 0,
    SecondHarmonic = 1 << 1,
    ThirdHarmonic = 1 << 2,
    FourthHarmonic = 1 << 3,
    ThdPlusNoise = 1 << 4,
    NoiseFloor = 1 << 5,
    Harmonics = SecondHarmonic | ThirdHarmonic | FourthHarmonic | ThdPlusNoise,
    Distortion = Harmonics | NoiseFloor,
    All = Primary | Distortion
}

public sealed record AnalysisCurve(
    string Name,
    IReadOnlyList<SignalPoint> Points,
    AnalysisCurveKind Kind = AnalysisCurveKind.Primary);

/// <summary>All present curves share one grid and one validity gate (finite in all or NaN in all); optional curves null when not requested.</summary>
public sealed record GroupDelayCurveSet(
    AnalysisCurve Measured,
    AnalysisCurve? Minimum,
    AnalysisCurve? Excess);

/// <summary>FFT(w·h) and FFT(t·w·h) in one time reference. Sum channels via <c>DataHelper.SumGatedSpectraPairs</c>, not bin by bin.</summary>
public sealed record GroupDelaySpectra(
    Complex[] Spectrum,
    Complex[] TimeWeighted);
