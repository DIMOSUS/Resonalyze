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

    /// <summary>Why a result <see cref="ResolvedVirtualDspSource.FromResult"/> refused cannot be summed, and what to do about it.</summary>
    public static string DescribeUnsummable(MeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.TimingReference switch
        {
            TimingReference.RecordedSweep =>
                "This one was imported from a recorded sweep and carries no absolute time. " +
                "Re-measure with a loopback channel configured.",
            TimingReference.UnsynchronizedLoopback =>
                "In this one the microphone heard the sweep before the loopback did: the " +
                "loopback ran on another clock or stream, as happens when a driver joins " +
                "two audio devices, so its arrival is not a delay. Re-measure with the " +
                "microphone and the loopback on one audio device.",
            _ =>
                "This one has no transfer IR. Re-measure with a loopback channel configured."
        };
    }
}
