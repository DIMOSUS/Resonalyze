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
    /// Resolved and handed up, and NOT yet shown anywhere: the input meter draws the
    /// microphone and the loopback. Said plainly because the comment here used to
    /// claim the opposite — that a clipped array channel is unrecoverable so its level
    /// "has to be visible before the sweep runs" — beside code no view reads, and the
    /// session that raises these levels lives only for the duration of the sweep, so
    /// "before" was never on offer either.
    /// <para>
    /// What actually guards it is stricter than a meter: a run that compromised ANY
    /// array microphone is rejected, retried, and — if it keeps failing — takes the
    /// measurement down with it, naming the input. Nothing can quietly average one
    /// position fewer than the user set up, so these levels are a convenience waiting
    /// for a meter rather than a safeguard something depends on.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AudioChannelLevel> Array { get; init; } = [];
}
