using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Which measurement a plotted analysis curve belongs to.
/// </summary>
public enum CurveSource
{
    Main,
    Compare
}

/// <summary>
/// Identity carried on every analysis <see cref="OxyPlot.Series.LineSeries"/> Tag so
/// overlays can bind to a live curve by a stable key — independent of the display name
/// or the currently selected Compare file — and recompute as the plot is rebuilt. Also
/// carries the phase wrap state that overlay difference math needs (this record replaces
/// the former dedicated phase tag).
/// </summary>
public sealed record CurveTag(
    Mode Mode,
    AnalysisCurveKind Kind,
    CurveSource Source = CurveSource.Main,
    bool? PhaseUnwrapped = null)
{
    // Stable across rebuilds; matches a linked overlay slot to its live curve and is
    // persisted as the binding. Deliberately excludes the Compare file name so swapping
    // the Compare measurement keeps the binding and picks up the new curve.
    public string Key => $"{Mode}:{Kind}:{Source}";

    // Human-readable name shown in the "Link to live curve…" picker.
    public string Label => Source == CurveSource.Compare
        ? $"{BaseLabel(Mode, Kind)} — Compare"
        : BaseLabel(Mode, Kind);

    private static string BaseLabel(Mode mode, AnalysisCurveKind kind) => kind switch
    {
        AnalysisCurveKind.SecondHarmonic => "2nd harmonic",
        AnalysisCurveKind.ThirdHarmonic => "3rd harmonic",
        AnalysisCurveKind.FourthHarmonic => "4th harmonic",
        AnalysisCurveKind.ThdPlusNoise => "THD+N",
        AnalysisCurveKind.MinimumPhase => "Minimum phase",
        AnalysisCurveKind.ExcessPhase => "Excess phase",
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
