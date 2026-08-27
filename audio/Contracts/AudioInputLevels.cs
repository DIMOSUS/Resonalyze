namespace Resonalyze.Audio;

/// <summary>
/// The Peak/RMS/full-scale summary of one input channel, in dBFS. A neutral
/// audio-layer value; the application maps it onto its own meter presentation
/// (microphone = clip warning, loopback = full-scale reference).
/// </summary>
public readonly record struct AudioChannelLevel(
    double PeakDbFs,
    double RmsDbFs,
    bool FullScale);

/// <summary>
/// Live input levels raised by a capture session, already resolved to the
/// microphone and (optional) loopback roles the caller requested — the caller
/// never has to know which hardware channel each came from.
/// </summary>
public sealed record AudioInputLevels(
    AudioChannelLevel Microphone,
    AudioChannelLevel? Loopback)
{
    /// <summary>
    /// The array microphones' levels, in the order they were requested; empty
    /// when the routing has none.
    /// </summary>
    /// <remarks>
    /// Metered for the same reason the microphone is, and more urgently: a
    /// measurement keeps its recording, but an array microphone keeps only the
    /// curve it produced. A clipped array channel is therefore unrecoverable
    /// after the fact, so its level has to be visible BEFORE the sweep runs.
    /// </remarks>
    public IReadOnlyList<AudioChannelLevel> Array { get; init; } = [];
}
