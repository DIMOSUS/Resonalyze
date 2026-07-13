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
    AudioChannelLevel? Loopback);
