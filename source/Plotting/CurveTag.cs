using Resonalyze.Dsp;

namespace Resonalyze;

public enum CurveSource
{
    Main,
    Compare
}

/// <summary>Series Tag identity so overlays bind to a live curve by a stable key; also carries phase wrap state for difference math.</summary>
public sealed record CurveTag(
    Mode Mode,
    AnalysisCurveKind Kind,
    CurveSource Source = CurveSource.Main,
    bool? PhaseUnwrapped = null)
{
    // Persisted binding; excludes the Compare file name so swapping Compare keeps the link.
    public string Key => $"{Mode}:{Kind}:{Source}";

    public string Label => Source == CurveSource.Compare
        ? $"{BaseLabel(Mode, Kind)} — Compare"
        : BaseLabel(Mode, Kind);

    private static string BaseLabel(Mode mode, AnalysisCurveKind kind) => kind switch
    {
        AnalysisCurveKind.SecondHarmonic => "2nd harmonic",
        AnalysisCurveKind.ThirdHarmonic => "3rd harmonic",
        AnalysisCurveKind.FourthHarmonic => "4th harmonic",
        AnalysisCurveKind.ThdPlusNoise => "THD",
        AnalysisCurveKind.NoiseFloor => "Noise floor",
        AnalysisCurveKind.MinimumPhase => "Minimum phase",
        AnalysisCurveKind.ExcessPhase => "Excess phase",
        AnalysisCurveKind.MinimumPhaseGroupDelay => "Minimum-phase group delay",
        AnalysisCurveKind.ExcessGroupDelay => "Excess group delay",
        AnalysisCurveKind.InputSpectrum => "Input Spectrum (RTA)",
        AnalysisCurveKind.ImpulseEnvelope => "Envelope (ETC)",
        AnalysisCurveKind.ImpulseStep => "Step response",
        AnalysisCurveKind.ArrayAverage => "Array average",
        AnalysisCurveKind.ArrayMicrophone => "Array microphone",
        AnalysisCurveKind.ArraySpread => "Array spread",
        _ => mode switch
        {
            Mode.FrequencyResponse => "Magnitude",
            Mode.PhaseResponse => "Measured phase",
            Mode.GroupDelay => "Group delay",
            Mode.ImpulseResponse => "Impulse",
            Mode.Autocorrelation => "Autocorrelation",
            Mode.LiveSpectrum => "Live transfer function",
            _ => "Curve"
        }
    };
}
