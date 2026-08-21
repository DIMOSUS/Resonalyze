using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// An impulse-view trace whose tracker reads the point the way the mode is meant
/// to be read: the time in BOTH units regardless of which one the axis is drawn
/// in (the sample index is what the rest of the app addresses the record by, the
/// millisecond is what a delay is set in), and — when the axis measures time from
/// an acoustic event rather than from the record start — the path length that
/// time corresponds to in air. A reflection 3 ms after the direct sound is a
/// metre of extra path; making the reader convert that in their head is the
/// difference between a plot and an instrument.
/// </summary>
internal sealed class ImpulseLineSeries : LineSeries
{
    /// <summary>Samples per second, for the unit the axis is not drawn in.</summary>
    public required int SampleRate { get; init; }

    /// <summary>The unit the X coordinate carries.</summary>
    public required ImpulseTimeUnit TimeUnit { get; init; }

    /// <summary>
    /// Whether X is measured from an acoustic event (an arrival or the peak) and
    /// so has a path length. False for the record-start origin, where the elapsed
    /// time is mostly the measurement chain's own latency and "distance" would be
    /// a number with no physical claim behind it.
    /// </summary>
    public required bool TimeIsRelative { get; init; }

    /// <summary>The value's unit, appended to the level line ("dB", "%" or none).</summary>
    public string ValueUnit { get; init; } = string.Empty;

    public override TrackerHitResult? GetNearestPoint(ScreenPoint point, bool interpolate)
    {
        TrackerHitResult? hit = base.GetNearestPoint(point, interpolate);
        if (hit == null)
        {
            return hit;
        }

        double milliseconds = TimeUnit == ImpulseTimeUnit.Milliseconds
            ? hit.DataPoint.X
            : SampleRate > 0 ? hit.DataPoint.X * 1000.0 / SampleRate : 0.0;
        double samples = TimeUnit == ImpulseTimeUnit.Samples
            ? hit.DataPoint.X
            : hit.DataPoint.X * SampleRate / 1000.0;

        string time = $"{milliseconds:0.000} ms · {samples:0} sample";
        if (TimeIsRelative)
        {
            double millimetres =
                milliseconds * Acoustics.SpeedOfSoundAt20CMetersPerSecond;
            time += Math.Abs(millimetres) >= 1000.0
                ? $" · {millimetres / 1000.0:0.00} m"
                : $" · {millimetres:0} mm";
        }

        string value = ValueUnit.Length > 0
            ? $"{hit.DataPoint.Y:0.000} {ValueUnit}"
            : $"{hit.DataPoint.Y:0.00000000}";
        hit.Text = $"{Title}\n{time}\n{value}";
        return hit;
    }
}
