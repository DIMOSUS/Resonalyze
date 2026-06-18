namespace Resonalyze;

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
}
