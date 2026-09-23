using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Phase or Group Delay settings panel's state, each field as its control shows it: the gate, the window,
/// the smoothing and the curves, and for Phase the unwrap and the detrend. See docs/tech/mode-settings.md#code-map.</summary>
internal sealed class GatedAnalysisSettingsSession
{
    private GatedAnalysisSettingsSession(GatedAnalysisDefaults defaults, PhaseDetrendState? detrend)
    {
        Defaults = defaults;
        Detrend = detrend;
    }

    public GatedAnalysisDefaults Defaults { get; }

    /// <summary>Phase only.</summary>
    public PhaseDetrendState? Detrend { get; }

    public GateFields Gate { get; } = new();

    public WindowModeChoice WindowMode { get; set; }

    public int SmoothingInverseOctaves { get; set; }

    public CurveVisibilityOptions Curves { get; private set; } = new();

    /// <summary>Phase only.</summary>
    public bool Unwrap { get; set; }

    public ModeSettingsMeasurement Measurement { get; private set; } = new(null, 0);

    public CompareAnalysisSource? Compare { get; set; }

    public GatePreview Preview =>
        new(Measurement.Result, (double)Gate.OffsetMs, (double)Gate.LeftMs, (double)Gate.PlateauMs, (double)Gate.RightMs, Compare);

    public static GatedAnalysisSettingsSession ForPhase() => new(GatedAnalysisDefaults.Phase, new PhaseDetrendState());

    public static GatedAnalysisSettingsSession ForGroupDelay() => new(GatedAnalysisDefaults.GroupDelay, null);

    public void Load(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visibility);
        if (Detrend is { } detrend)
        {
            Gate.Load(options.PhaseGateAutoFit, options.PhaseGateOffsetMs, options.PhaseLeftMs, options.PhasePlateauMs, options.PhaseRightMs);
            WindowMode = WindowModeChoice.From(options.PhaseWindowMode, options.PhaseFdwCycles);
            detrend.Load(options.PhaseDetrendMode, options.PhaseDetrendMs);
            Unwrap = options.Unwrap;
        }
        else
        {
            Gate.Load(
                options.GroupDelayGateAutoFit,
                options.GroupDelayGateOffsetMs,
                options.GroupDelayLeftMs,
                options.GroupDelayPlateauMs,
                options.GroupDelayRightMs);
            WindowMode = WindowModeChoice.From(options.GroupDelayWindowMode, options.GroupDelayFdwCycles);
        }

        SmoothingInverseOctaves = SmoothingPresetOptions.Normalize(options.SmoothingInverseOctaves, includePsychoacoustic: false);
        Curves = visibility.Copy();
        Gate.Snap(Measurement);
    }

    public void Follow(ModeSettingsMeasurement measurement)
    {
        Measurement = measurement;
        Gate.Snap(measurement);
    }

    /// <summary>Auto turned on puts the offset on the IR start at once.</summary>
    public void SetAuto(bool auto)
    {
        Gate.Auto = auto;
        Gate.Snap(Measurement);
    }

    /// <summary>What Phase reads τ through: the gate and the window, with the detrend Auto.</summary>
    public PhaseAnalysisSettings DetrendReading() => new(
        WindowMode.Mode,
        WindowMode.Cycles,
        PhaseDetrendMode.Auto,
        Detrend?.ManualMs ?? 0.0,
        (double)Gate.OffsetMs,
        (double)Gate.LeftMs,
        (double)Gate.PlateauMs,
        (double)Gate.RightMs,
        Unwrap,
        SmoothingInverseOctaves);

    public void WriteTo(FrequencyResponseOptions options, CurveVisibilityOptions visibility)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visibility);
        if (Detrend is { } detrend)
        {
            options.PhaseGateAutoFit = Gate.Auto;
            options.PhaseGateOffsetMs = (double)Gate.OffsetMs;
            options.PhasePlateauMs = (double)Gate.PlateauMs;
            options.PhaseLeftMs = (double)Gate.LeftMs;
            options.PhaseRightMs = (double)Gate.RightMs;
            options.PhaseDetrendMs = detrend.ManualMs;
            options.PhaseWindowMode = WindowMode.Mode;
            options.PhaseFdwCycles = WindowMode.Cycles;
            options.PhaseDetrendMode = detrend.Mode;
            options.Unwrap = Unwrap;
            visibility.ShowMeasuredPhase = Curves.ShowMeasuredPhase;
            visibility.ShowMinimumPhase = Curves.ShowMinimumPhase;
            visibility.ShowExcessPhase = Curves.ShowExcessPhase;
        }
        else
        {
            options.GroupDelayGateAutoFit = Gate.Auto;
            options.GroupDelayGateOffsetMs = (double)Gate.OffsetMs;
            options.GroupDelayPlateauMs = (double)Gate.PlateauMs;
            options.GroupDelayLeftMs = (double)Gate.LeftMs;
            options.GroupDelayRightMs = (double)Gate.RightMs;
            options.GroupDelayWindowMode = WindowMode.Mode;
            options.GroupDelayFdwCycles = WindowMode.Cycles;
            visibility.ShowGroupDelay = Curves.ShowGroupDelay;
            visibility.ShowMinimumPhaseGroupDelay = Curves.ShowMinimumPhaseGroupDelay;
            visibility.ShowExcessGroupDelay = Curves.ShowExcessGroupDelay;
        }

        options.SmoothingInverseOctaves = SmoothingInverseOctaves;
        visibility.ShowCoherence = Curves.ShowCoherence;
    }
}

/// <summary>What the Phase and Group Delay fields reset to; a field without one has no reset button.</summary>
internal sealed record GatedAnalysisDefaults(
    WindowModeChoice? WindowMode,
    decimal LeftMs,
    decimal PlateauMs,
    decimal RightMs,
    decimal? DetrendMs,
    int SmoothingInverseOctaves)
{
    public static GatedAnalysisDefaults GroupDelay { get; } = new(
        GroupDelayWindow(),
        (decimal)FrequencyResponseOptions.DefaultGroupDelayLeftMs,
        (decimal)FrequencyResponseOptions.DefaultGroupDelayPlateauMs,
        (decimal)FrequencyResponseOptions.DefaultGroupDelayRightMs,
        null,
        SmoothingPresetOptions.Normalize(FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves));

    public static GatedAnalysisDefaults Phase { get; } = new(
        null,
        (decimal)FrequencyResponseOptions.DefaultPhaseLeftMs,
        (decimal)FrequencyResponseOptions.DefaultPhasePlateauMs,
        (decimal)FrequencyResponseOptions.DefaultPhaseRightMs,
        (decimal)FrequencyResponseOptions.DefaultPhaseDetrendMs,
        SmoothingPresetOptions.Normalize(FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves));

    private static WindowModeChoice GroupDelayWindow()
    {
        var defaults = new FrequencyResponseOptions();
        return WindowModeChoice.From(defaults.GroupDelayWindowMode, defaults.GroupDelayFdwCycles);
    }
}
