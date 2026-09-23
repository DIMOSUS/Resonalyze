namespace Resonalyze;

public enum TimingReference
{
    /// <summary>Same-clock loopback: the arrival is the real delay, comparable across measurements.</summary>
    SynchronizedLoopback,

    /// <summary>External recording: shape is real, position is not; delays are meaningful only within one measurement.</summary>
    RecordedSweep,

    /// <summary>The microphone heard the sweep before the loopback did (another device or stream, or a delayed reference path):
    /// shape is real, position is not. See docs/tech/sweep-measurement.md#arrival-ahead-of-the-loopback.</summary>
    NonCausalLoopback
}

internal static class TimingReferences
{
    public static bool HasAbsoluteTime(this TimingReference reference) =>
        reference == TimingReference.SynchronizedLoopback;
}
