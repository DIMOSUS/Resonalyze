namespace Resonalyze.Audio;

public readonly record struct AudioChannelLevel(
    double PeakDbFs,
    double RmsDbFs,
    bool FullScale);

public sealed record AudioInputLevels(
    AudioChannelLevel Microphone,
    AudioChannelLevel? Loopback)
{
    /// <summary>Not shown by any meter yet; a compromised array channel is rejected by run validation, not by this.</summary>
    public IReadOnlyList<AudioChannelLevel> Array { get; init; } = [];
}
