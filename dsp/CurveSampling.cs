namespace Resonalyze.Dsp;

/// <summary>
/// Shared sampling of (Hz, dB) curves with ascending X: linear interpolation in dB
/// over log frequency, via binary search.
/// </summary>
internal static class CurveSampling
{
    /// <summary>
    /// Interpolates the curve at <paramref name="frequencyHz"/>. With
    /// <paramref name="clampEnds"/> a frequency outside the curve's range holds the
    /// endpoint value (a measured driver outside its range has simply rolled off);
    /// without it the point reads NaN so callers can ignore it. A NaN gap in the
    /// curve always reads NaN.
    /// </summary>
    public static double InterpolateDbLog(
        IReadOnlyList<SignalPoint> points,
        double frequencyHz,
        bool clampEnds)
    {
        int count = points.Count;
        if (count == 0)
        {
            return double.NaN;
        }
        if (frequencyHz <= points[0].X)
        {
            return clampEnds || frequencyHz == points[0].X ? points[0].Y : double.NaN;
        }
        if (frequencyHz >= points[count - 1].X)
        {
            return clampEnds || frequencyHz == points[count - 1].X
                ? points[count - 1].Y
                : double.NaN;
        }

        int lo = 0;
        int hi = count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].X <= frequencyHz)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        SignalPoint a = points[lo];
        SignalPoint b = points[hi];
        if (!double.IsFinite(a.Y) || !double.IsFinite(b.Y))
        {
            return double.NaN;
        }
        if (b.X <= a.X)
        {
            return a.Y;
        }

        double t = (Math.Log(frequencyHz) - Math.Log(a.X)) /
                   (Math.Log(b.X) - Math.Log(a.X));
        return a.Y + t * (b.Y - a.Y);
    }
}
