namespace Resonalyze;

internal readonly record struct AudioChannelLevel(
    double PeakDbFs,
    double RmsDbFs,
    bool FullScale);
