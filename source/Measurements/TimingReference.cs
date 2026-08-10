namespace Resonalyze;

/// <summary>
/// What the arrival time of a measurement's transfer IR is referenced to — the
/// difference between a delay that means something and a number that only looks
/// like one.
/// </summary>
public enum TimingReference
{
    /// <summary>
    /// The loopback was captured alongside the microphone on one device and one
    /// clock, so the transfer IR's arrival is the tract's real delay: comparable
    /// across measurements, which is what Time Alignment is built on.
    /// </summary>
    SynchronizedLoopback,

    /// <summary>
    /// A recording made outside Resonalyze, analyzed against the generated sweep.
    /// Nothing tied the recorder's start to the playback, so the arrival lands
    /// wherever the record button happened to be pressed: the shape of the
    /// response is real, its position on the time axis is not. Delays are
    /// meaningful only WITHIN one such measurement (a reflection 8 ms after the
    /// direct sound is 8 ms), never between two of them.
    /// </summary>
    RecordedSweep
}
