namespace Resonalyze;

/// <summary>Whether a queued live sequence continues the one read before it, so the overlap reframer may join them.</summary>
internal static class LiveSequenceContinuity
{
    /// <summary>A gap in the queue numbers is a dropped block, a changed discontinuity count a device gap; either
    /// way a frame built across it reads the step as a broadband burst. Judged on what the sequences carry, not on
    /// when the reader wakes: a drop landing between a dequeue and the next check must not move the reset.</summary>
    /// <param name="previousIndex">Null for the run's first sequence, which has nothing to join.</param>
    public static bool Breaks(
        long? previousIndex,
        long index,
        long previousDiscontinuityGeneration,
        long discontinuityGeneration) =>
        (previousIndex is long previous && index != previous + 1) ||
        discontinuityGeneration != previousDiscontinuityGeneration;
}
