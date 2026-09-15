namespace Resonalyze;

public enum TimingReference
{
    /// <summary>Same-clock loopback: the arrival is the real delay, comparable across measurements.</summary>
    SynchronizedLoopback,

    /// <summary>External recording: shape is real, position is not; delays are meaningful only within one measurement.</summary>
    RecordedSweep
}
