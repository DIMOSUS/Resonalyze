using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Phase panel's τ: Off shows zero, Auto the value read off the gate, Manual the user's own value, which is
/// what the settings keep and what survives the other two modes.</summary>
internal sealed class PhaseDetrendState
{
    /// <summary>The detrend list holds Off, Auto, Manual in the enum's order.</summary>
    public int ModeIndex { get; set; }

    public double ManualMs { get; private set; }

    /// <summary>The Auto reading of the gate the panel last presented; null when there is none.</summary>
    public double? AutoMs { get; set; }

    public bool IsManual => ModeIndex == (int)PhaseDetrendMode.Manual;

    public bool IsAuto => ModeIndex == (int)PhaseDetrendMode.Auto;

    public PhaseDetrendMode Mode =>
        Enum.IsDefined((PhaseDetrendMode)ModeIndex) ? (PhaseDetrendMode)ModeIndex : PhaseDetrendMode.Auto;

    public decimal ShownMs => ModeSettingsLimits.DetrendMs.Clamp(ModeIndex switch
    {
        (int)PhaseDetrendMode.Auto when AutoMs is { } auto => auto,
        (int)PhaseDetrendMode.Manual => ManualMs,
        _ => 0.0
    });

    public void Load(PhaseDetrendMode mode, double manualMs)
    {
        ModeIndex = (int)mode;
        ManualMs = manualMs;
    }

    /// <summary>A value in the τ field is the user's only while Manual shows it.</summary>
    public void Type(decimal shownMs)
    {
        if (IsManual)
        {
            ManualMs = (double)shownMs;
        }
    }

    /// <summary>An estimate taken as the user's value, as the field shows it.</summary>
    public void TakeEstimate(double estimateMs) => ManualMs = (double)ModeSettingsLimits.DetrendMs.Clamp(estimateMs);
}
