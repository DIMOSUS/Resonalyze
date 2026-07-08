namespace Resonalyze;

/// <summary>
/// The pure source-compatibility policy of the Virtual DSP tool: whether a
/// candidate measurement can drive a channel. The rule was previously hand-rolled
/// in two places (interactive assignment and silent project reload) as mutually
/// inverse predicates that could drift; this is the single decision both share.
/// Kept UI-free so it is unit-testable.
/// </summary>
internal static class VirtualCrossoverSourceRules
{
    public enum Decision
    {
        /// <summary>No loopback transfer IR — the source can never drive a channel.</summary>
        Reject,

        /// <summary>
        /// Usable, but one or more already-assigned channels run at a different
        /// sample rate and must be cleared first (all channels must share one).
        /// </summary>
        NeedsConfirmClear,

        /// <summary>Usable as-is.</summary>
        Accept
    }

    /// <summary>
    /// Decides whether a candidate source is compatible with the channels that
    /// already have a resolved transfer IR. <paramref name="otherResolvedSampleRates"/>
    /// is the sample rate of every OTHER channel that currently has a source.
    /// </summary>
    public static Decision Evaluate(
        bool hasTransferIr,
        int candidateSampleRate,
        IEnumerable<int> otherResolvedSampleRates)
    {
        ArgumentNullException.ThrowIfNull(otherResolvedSampleRates);
        if (!hasTransferIr)
        {
            return Decision.Reject;
        }

        bool anyMismatch = otherResolvedSampleRates.Any(rate => rate != candidateSampleRate);
        return anyMismatch ? Decision.NeedsConfirmClear : Decision.Accept;
    }
}
