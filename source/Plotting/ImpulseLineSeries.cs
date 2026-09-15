using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Tracker shows time in samples and ms, plus path length in air when time is measured from an acoustic event.</summary>
internal sealed class ImpulseLineSeries : LineSeries
{
    public ImpulseLineSeries()
    {
        // Whole-record traces (1M samples at 192 kHz) took GDI+ 40 s; per-pixel-column extremes: 76 ms. Tracker still reads real points.
        Decimator = OxyPlot.Decimator.Decimate;
    }

    public required int SampleRate { get; init; }

    public required ImpulseTimeUnit TimeUnit { get; init; }

    /// <summary>False for the record-start origin, where elapsed time is mostly chain latency, not distance.</summary>
    public required bool TimeIsRelative { get; init; }

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
