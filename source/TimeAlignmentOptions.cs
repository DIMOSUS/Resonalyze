namespace Resonalyze;

public enum TimeAlignmentPeakSearchMode
{
    FirstArrival,
    StrongestPeak
}

public sealed class TimeAlignmentOptions
{
    public string? AsioDriverName { get; set; }
    public int MicrophoneInputChannelOffset { get; set; }
    public int LoopbackInputChannelOffset { get; set; }
    public int AsioOutputChannelOffset { get; set; }
    public bool UseBandpassWindow { get; set; }
    public double BandpassCenterHz { get; set; } = 1000;
    public double BandpassPassOctaves { get; set; } = 1;
    public double BandpassFadeOctaves { get; set; } = 0.5;
    public TimeAlignmentPeakSearchMode PeakSearchMode { get; set; } =
        TimeAlignmentPeakSearchMode.FirstArrival;
    public double FirstPeakThresholdBelowMaxDb { get; set; } = 25;
    public double FirstPeakMinimumSnrDb { get; set; } = 12;
    public double PeakSearchWindowMilliseconds { get; set; } = 80;
}
