namespace Resonalyze;

/// <summary>Source-compatibility policy shared by interactive assignment and silent reload, so the two cannot drift.</summary>
internal static class VirtualCrossoverSourceRules
{
    public enum Decision
    {
        Reject,

        /// <summary>A project is locked to one sample rate; clear existing sources to switch.</summary>
        RejectSampleRateMismatch,

        Accept
    }

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
        return anyMismatch ? Decision.RejectSampleRateMismatch : Decision.Accept;
    }
}
