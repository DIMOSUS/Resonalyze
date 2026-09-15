namespace Resonalyze;

/// <summary>Where a REW export's t = 0 lands. <c>ReferenceIndex</c> is REW's buffer index that becomes sample 0.</summary>
internal sealed record RewImportTimingPlan(
    TimingReference Reference,
    double ReferenceIndex,
    double ArrivalSamples,
    double OffsetSeconds);

/// <summary>The user-stated REW timing offset decides the timing reference. See docs/tech/sweep-measurement.md#rew-import-timing.</summary>
internal static class RewImportTiming
{
    /// <summary>Null <paramref name="statedOffsetSeconds"/> means unknown; false explains why the stated offset cannot fit the file.</summary>
    public static bool TryResolve(
        double? statedOffsetSeconds,
        double timeZeroIndex,
        int peakIndex,
        int sampleCount,
        int sampleRate,
        out RewImportTimingPlan? plan,
        out string? problem)
    {
        plan = null;
        problem = null;

        if (statedOffsetSeconds is not { } offsetSeconds)
        {
            // RecordedSweep claims nothing, so nothing is checked.
            plan = new RewImportTimingPlan(
                TimingReference.RecordedSweep,
                timeZeroIndex,
                peakIndex - timeZeroIndex,
                0);
            return true;
        }

        if (!double.IsFinite(offsetSeconds))
        {
            problem = "the timing offset must be a number";
            return false;
        }

        double referenceIndex = timeZeroIndex - (offsetSeconds * sampleRate);
        if (!(referenceIndex >= 0) || referenceIndex >= sampleCount)
        {
            problem = FormattableString.Invariant(
                $"with a {offsetSeconds * 1000.0:0.####} ms offset taken out, t = 0 falls at sample {referenceIndex:0.###} of {sampleCount} — outside the buffer, so these samples do not contain the reference arrival");
            return false;
        }

        double arrivalSamples = peakIndex - referenceIndex;
        if (arrivalSamples <= 0)
        {
            // Sound cannot reach the mic before the loopback: report the offset that would make the arrival physical.
            double neededMs = (peakIndex - timeZeroIndex) / (double)sampleRate * -1000.0;
            problem = FormattableString.Invariant(
                $"with a {offsetSeconds * 1000.0:0.####} ms offset taken out the arrival would be {arrivalSamples / (double)sampleRate * 1000.0:0.####} ms, which a loopback-referenced sweep cannot produce — the microphone cannot hear the sweep before the reference does. This measurement needs an offset above {neededMs:0.####} ms to place the arrival after t = 0");
            return false;
        }

        plan = new RewImportTimingPlan(
            TimingReference.SynchronizedLoopback,
            referenceIndex,
            arrivalSamples,
            offsetSeconds);
        return true;
    }
}
