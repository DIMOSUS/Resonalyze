namespace Resonalyze;

public enum TimingReference
{
    /// <summary>Same-clock loopback: the arrival is the real delay, comparable across measurements.</summary>
    SynchronizedLoopback,

    /// <summary>External recording: shape is real, position is not; delays are meaningful only within one measurement.</summary>
    RecordedSweep,

    /// <summary>Loopback on another clock or stream (an aggregated driver): the microphone heard the sweep before the loopback did,
    /// so shape is real and position is not. See docs/tech/sweep-measurement.md#arrival-ahead-of-the-loopback.</summary>
    UnsynchronizedLoopback
}

internal static class TimingReferences
{
    public static bool HasAbsoluteTime(this TimingReference reference) =>
        reference == TimingReference.SynchronizedLoopback;
}
