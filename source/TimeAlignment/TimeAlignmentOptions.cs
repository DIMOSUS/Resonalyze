namespace Resonalyze;

/// <summary>
/// How the Time Alignment analysis limits its frequency band: the raw
/// record as-is, an automatic window around the driver's own dominant band
/// (detected from the record's smoothed response), or a hand-set window.
/// </summary>
public enum TimeAlignmentBandMode
{
    FullBand,
    AutoBand,
    ManualBand
}

public sealed class TimeAlignmentOptions
{
    public string? AsioDriverName { get; set; }
    public int MicrophoneInputChannelOffset { get; set; }
    public int LoopbackInputChannelOffset { get; set; }
    public int AsioOutputChannelOffset { get; set; }
    public TimeAlignmentBandMode BandMode { get; set; } = TimeAlignmentBandMode.AutoBand;
    public double BandpassCenterHz { get; set; } = 1000;
    public double BandpassPassOctaves { get; set; } = 1;
    public double BandpassFadeOctaves { get; set; } = 0.5;
    public double FirstPeakThresholdBelowMaxDb { get; set; } = 25;
    public double FirstPeakMinimumSnrDb { get; set; } = 12;
    public double PeakSearchWindowMilliseconds { get; set; } = 80;
}
