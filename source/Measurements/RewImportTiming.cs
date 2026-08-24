namespace Resonalyze;

/// <summary>
/// Where an imported REW export's t = 0 lands, and what its arrival may be called.
/// </summary>
/// <param name="Reference">
/// What the arrival is worth once the offset question is answered.
/// </param>
/// <param name="ReferenceIndex">
/// The index in REW's buffer that becomes sample 0 of the transfer response.
/// </param>
/// <param name="ArrivalSamples">The arrival this plan implies, in samples.</param>
/// <param name="OffsetSeconds">The offset taken back out; zero when none was stated.</param>
internal sealed record RewImportTimingPlan(
    TimingReference Reference,
    double ReferenceIndex,
    double ArrivalSamples,
    double OffsetSeconds);

/// <summary>
/// Decides what a REW text import may claim about time, from the one thing the format
/// cannot state and only the person who measured it knows: the timing offset REW was
/// running with.
/// </summary>
/// <remarks>
/// The design this replaces asked the wrong question. It tried to prove the offset was
/// zero from the file, and the file cannot say: the header of a measurement taken with
/// an offset is word for word the header of one taken without, and the offset is folded
/// into the start time. The only visible consequence — an arrival that precedes the
/// reference — appears exclusively when the offset is LARGER than the arrival, so it
/// catches the loud cases and is blind to every quiet one.
///
/// So the offset is asked for instead, and the answer decides the reference: a value
/// (zero included) is a user assertion, and the import is compensated and stamped
/// <see cref="TimingReference.SynchronizedLoopback"/> on the strength of it; "I do not
/// know" is not a failure but a different measurement — the shape is real, its position
/// is not, which is exactly <see cref="TimingReference.RecordedSweep"/>.
///
/// Lives beside the enum rather than in the import method for the reason
/// <c>SampleRateOptions.Resolve</c> does: it is decided without a window and can
/// therefore be tested without one.
/// </remarks>
internal static class RewImportTiming
{
    /// <summary>
    /// Builds the plan, or explains in <paramref name="problem"/> why the stated offset
    /// cannot be true of this file.
    /// </summary>
    /// <param name="statedOffsetSeconds">
    /// The offset REW was measuring with, as the user states it, or null for "unknown".
    /// </param>
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
            // Nothing is claimed, so nothing needs checking: the export is taken on its
            // own terms and the arrival is not offered to anything that compares
            // measurements. An arrival that precedes the reference is not refused here
            // either — under RecordedSweep it is not a lie, only a number nobody may use.
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
            // The claim and the file disagree, and the file is not the one that can be
            // wrong about this: sound does not reach the microphone before it reaches
            // the loopback. Reporting the offset that WOULD make the arrival physical
            // turns a refusal into the next thing to try.
            double neededMs = (peakIndex - timeZeroIndex) / (double)sampleRate * -1000.0;
            problem = FormattableString.Invariant(
                $"with a {offsetSeconds * 1000.0:0.####} ms offset taken out the arrival would be {arrivalSamples / (double)sampleRate * 1000.0:0.####} ms, which a loopback-referenced sweep cannot produce — the microphone cannot hear the sweep before the reference does. This header needs an offset above {neededMs:0.####} ms to place the arrival after t = 0");
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
