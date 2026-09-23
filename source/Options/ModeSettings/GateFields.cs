namespace Resonalyze.Options;

/// <summary>A millisecond gate as the Phase and Group Delay fields show it: its offset, whether Auto keeps the offset on
/// the IR start, and the left fade, plateau and right fade.</summary>
internal sealed class GateFields
{
    public bool Auto { get; set; }

    public decimal OffsetMs { get; set; }

    public decimal LeftMs { get; set; }

    public decimal PlateauMs { get; set; }

    public decimal RightMs { get; set; }

    public bool OffsetEditable => !Auto;

    public string ReliableFrom => GateReadout.ReliableFrom((double)LeftMs, (double)PlateauMs, (double)RightMs);

    public void Load(bool auto, double offsetMs, double leftMs, double plateauMs, double rightMs)
    {
        OffsetMs = ModeSettingsLimits.GateOffsetMs.Clamp(offsetMs);
        Auto = auto;
        PlateauMs = ModeSettingsLimits.GateLengthMs.Clamp(plateauMs);
        LeftMs = ModeSettingsLimits.GateLengthMs.Clamp(leftMs);
        RightMs = ModeSettingsLimits.GateLengthMs.Clamp(rightMs);
    }

    /// <summary>Auto puts the offset on the transfer IR's band-limited start; without one the offset stays. An equal
    /// value keeps the one held, as the field does.</summary>
    public void Snap(ModeSettingsMeasurement measurement)
    {
        if (Auto && measurement.TransferStartMs() is { } startMs &&
            ModeSettingsLimits.GateOffsetMs.Clamp(startMs) is var snapped && snapped != OffsetMs)
        {
            OffsetMs = snapped;
        }
    }
}
