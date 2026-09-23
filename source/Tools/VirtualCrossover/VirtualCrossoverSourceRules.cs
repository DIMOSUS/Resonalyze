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
            TimingReference.NonCausalLoopback =>
                "In this one the microphone heard the sweep before the loopback did, so its " +
                "arrival is not a delay. Usually the loopback was on another device or stream " +
                "(a driver joining two audio devices), or its path adds latency the " +
                "loudspeaker's does not. Re-measure with the microphone and a loopback taken " +
                "straight from the output on one audio device.",
            _ =>
                "This one has no transfer IR. Re-measure with a loopback channel configured."
        };
    }
}
