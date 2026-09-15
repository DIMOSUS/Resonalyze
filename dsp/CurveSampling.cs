namespace Resonalyze.Dsp;

/// <summary>Linear in dB over log frequency for ascending (Hz, dB) curves.</summary>
internal static class CurveSampling
{
    /// <summary>Out of range: endpoint value with <paramref name="clampEnds"/>, else NaN. NaN gaps read NaN.</summary>
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
