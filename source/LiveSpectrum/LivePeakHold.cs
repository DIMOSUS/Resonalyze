using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The display transform a peak-hold envelope was built under; see <see cref="LiveSpectrumDisplay.PeakHoldKey"/>.</summary>
/// <param name="TiltModel">Null (off) and a flat model are different transforms.</param>
internal readonly record struct LivePeakHoldKey(
    MagnitudeScale Scale,
    bool RtaOnly,
    int SmoothingCode,
    double? SplOffsetDb,
    NoiseSpectralModel? TiltModel);

/// <summary>
/// The peak-hold envelope: held over the displayed band curve, not raw bins, and dropped whenever the transform that
/// drew it changes.
/// </summary>
/// <remarks>See docs/tech/live-spectrum.md#peak-hold.</remarks>
internal sealed class LivePeakHold
{
    private const long SuspensionMilliseconds = 1000;
    private readonly Func<long> clockMilliseconds;
    private List<SignalPoint>? points;
    private long resumeTick;

    /// <param name="clockMilliseconds">Milliseconds since some fixed moment; <see cref="Environment.TickCount64"/> by default.</param>
    public LivePeakHold(Func<long>? clockMilliseconds = null)
    {
        this.clockMilliseconds = clockMilliseconds ?? (() => Environment.TickCount64);
    }

    /// <summary>The envelope, or null when there is none to draw.</summary>
    public List<SignalPoint>? Points => points;

    /// <summary>The transform of the last curve drawn with peak hold available.</summary>
    public LivePeakHoldKey DrawnKey { get; private set; }

    public void Clear() => points = null;

    /// <summary>Drops the envelope and ignores the next second of frames, so ramp-up frames are not latched.</summary>
    public void Suspend()
    {
        points = null;
        resumeTick = clockMilliseconds() + SuspensionMilliseconds;
    }

    public void Drawn(LivePeakHoldKey key) => DrawnKey = key;

    /// <summary>An envelope of finished display values cannot be max-ed against another transform's: drop it.</summary>
    public void Follow(LivePeakHoldKey key)
    {
        if (key != DrawnKey)
        {
            Suspend();
        }
    }

    // Per-index max of displayed dB equals the band-level peak (grid stable, level monotone in power).
    public void Hold(List<SignalPoint> current)
    {
        if (clockMilliseconds() < resumeTick)
        {
            return;
        }

        if (points == null || points.Count != current.Count)
        {
            points = new List<SignalPoint>(current);
            return;
        }

        for (int i = 0; i < current.Count; i++)
        {
            double held = Math.Max(points[i].Y, current[i].Y);
            points[i] = new SignalPoint(current[i].X, held);
        }
    }
}
