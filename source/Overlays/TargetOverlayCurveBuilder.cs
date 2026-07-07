using OxyPlot;

namespace Resonalyze;

/// <summary>
/// Builds the plot-ready curves of a Target overlay. The parametric target
/// shape and its tolerance band depend only on the spec, offset and tolerance,
/// so they are cached and reused across the ~30 fps live redraws; only the
/// deviation curve, which follows the measurement, is rebuilt per call.
/// </summary>
internal sealed class TargetOverlayCurveBuilder
{
    /// <summary>
    /// Log-spaced 20 Hz … 20 kHz grid used to draw a target shape and its
    /// tolerance band. The shape is parametric over frequency and must never
    /// be clipped to the measurement, even when its coverage is partial.
    /// </summary>
    internal static readonly OverlayPoint[] DefaultTargetGrid = BuildDefaultTargetGrid();

    private TargetShapeKey? cachedKey;
    private TargetOverlayShape? cachedShape;

    public TargetOverlayShape BuildShape(
        TargetCurveSpec spec,
        double offsetDb,
        double toleranceDb)
    {
        var key = new TargetShapeKey(spec, offsetDb, toleranceDb);
        if (cachedShape != null && key == cachedKey)
        {
            return cachedShape;
        }

        TargetCurveResult result = OverlayMath.BuildTarget(
            DefaultTargetGrid,
            spec,
            offsetDb,
            toleranceDb,
            0,
            TargetDeviationMode.None);
        cachedShape = new TargetOverlayShape(
            ToDataPoints(result.Target),
            ToDataPoints(result.ToleranceUpper),
            ToDataPoints(result.ToleranceLower));
        cachedKey = key;
        return cachedShape;
    }

    public static DataPoint[] BuildDeviation(
        IReadOnlyList<OverlayPoint> source,
        TargetCurveSpec spec,
        double offsetDb,
        int smoothingInverseOctaves,
        TargetDeviationMode deviationMode)
    {
        if (deviationMode == TargetDeviationMode.None || source.Count < 2)
        {
            return Array.Empty<DataPoint>();
        }

        return ToDataPoints(OverlayMath.BuildTarget(
            source,
            spec,
            offsetDb,
            0,
            smoothingInverseOctaves,
            deviationMode).Deviation);
    }

    private static DataPoint[] ToDataPoints(OverlayPoint[] points)
    {
        var result = new DataPoint[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            result[i] = new DataPoint(points[i].X, points[i].Y);
        }

        return result;
    }

    private static OverlayPoint[] BuildDefaultTargetGrid()
    {
        const double minHz = 20.0;
        const double maxHz = 20_000.0;
        const int count = 512;
        var grid = new OverlayPoint[count];
        double logMin = Math.Log10(minHz);
        double logStep = (Math.Log10(maxHz) - logMin) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            grid[i] = new OverlayPoint(Math.Pow(10.0, logMin + i * logStep), 0.0);
        }

        return grid;
    }

    private sealed record TargetShapeKey(
        TargetCurveSpec Spec,
        double OffsetDb,
        double ToleranceDb);
}

/// <summary>Plot-ready curves of a target shape; arrays are cached, do not mutate.</summary>
internal sealed record TargetOverlayShape(
    DataPoint[] Target,
    DataPoint[] ToleranceUpper,
    DataPoint[] ToleranceLower);
