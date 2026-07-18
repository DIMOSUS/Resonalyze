using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Contains plotting-independent calculations used by overlay comparisons.
/// </summary>
public static class OverlayMath
{
    public static bool SupportsAmplitudeSpace(Mode mode)
    {
        return mode is Mode.FrequencyResponse or Mode.LiveSpectrum;
    }

    /// <summary>
    /// Fractional-octave smoothing of an overlay curve.
    /// <paramref name="psychoacousticFloor"/> gates the psychoacoustic mode's
    /// asymmetric median floor — a MAGNITUDE concept: the caller must pass
    /// false for phase, group-delay and coherence curves, where the code then
    /// decodes to its plain 1/6-octave base width so those traces stay
    /// unbiased instead of being pulled upward at every narrow dip.
    /// </summary>
    public static OverlayPoint[] SmoothByOctaves(
        IReadOnlyList<OverlayPoint> points,
        int inverseOctaves,
        bool psychoacousticFloor = true)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2 || inverseOctaves == 0)
        {
            return points.ToArray();
        }
        if (!OverlaySmoothing.IsValid(inverseOctaves))
        {
            throw new ArgumentOutOfRangeException(nameof(inverseOctaves));
        }

        // The psychoacoustic mode smooths at its base width and additionally
        // floors each point at the window median (below), so narrow
        // interference dips drop out while peaks and broad structure survive.
        bool psychoacoustic = psychoacousticFloor &&
            SpectrumSmoothing.IsPsychoacoustic(inverseOctaves);
        double halfWidth = 0.5 * SpectrumSmoothing.SmoothingOctaves(inverseOctaves);
        var result = new OverlayPoint[points.Count];
        int left = 0;
        int right = 0;

        for (int center = 0; center < points.Count; center++)
        {
            OverlayPoint centerPoint = points[center];
            if (centerPoint.X <= 0 || double.IsNaN(centerPoint.Y))
            {
                result[center] = centerPoint;
                continue;
            }

            double centerOctaves = Math.Log2(centerPoint.X);
            while (left < points.Count &&
                   (points[left].X <= 0 ||
                    Math.Log2(points[left].X) < centerOctaves - halfWidth))
            {
                left++;
            }
            right = Math.Max(right, center);
            while (right + 1 < points.Count &&
                   points[right + 1].X > 0 &&
                   Math.Log2(points[right + 1].X) <= centerOctaves + halfWidth)
            {
                right++;
            }

            double weightedSum = 0;
            double weightSum = 0;
            for (int sample = left; sample <= right; sample++)
            {
                if (double.IsNaN(points[sample].Y))
                {
                    continue;
                }

                double distance =
                    Math.Abs(Math.Log2(points[sample].X) - centerOctaves);
                double weight = 0.5 *
                    (1 + Math.Cos(Math.PI * distance / halfWidth));
                weightedSum += points[sample].Y * weight;
                weightSum += weight;
            }

            double value = weightSum > 1e-12
                ? weightedSum / weightSum
                : centerPoint.Y;
            if (psychoacoustic)
            {
                double median = WindowMedian(points, left, right);
                if (double.IsFinite(median))
                {
                    value = Math.Max(value, median);
                }
            }

            result[center] = new OverlayPoint(centerPoint.X, value);
        }

        return result;
    }

    // The median of the finite Y values over [first, last] — the robust center
    // the psychoacoustic floor compares the windowed mean against: a dip
    // narrower than half the window cannot move it. NaN when the window holds
    // no finite value.
    private static double WindowMedian(
        IReadOnlyList<OverlayPoint> points, int first, int last)
    {
        var values = new List<double>();
        for (int i = Math.Max(first, 0);
             i <= Math.Min(last, points.Count - 1);
             i++)
        {
            if (!double.IsNaN(points[i].Y))
            {
                values.Add(points[i].Y);
            }
        }

        if (values.Count == 0)
        {
            return double.NaN;
        }

        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }

    /// <summary>
    /// Builds the target curve, the deviation of a measurement from it, and the
    /// optional tolerance band. The shared slot offset shifts the target, and the
    /// deviation is computed against that shifted target. The measurement is
    /// smoothed before the deviation so it does not jitter.
    /// </summary>
    public static TargetCurveResult BuildTarget(
        IReadOnlyList<OverlayPoint> source,
        TargetCurveSpec spec,
        double offsetDb,
        double toleranceDb,
        int smoothingInverseOctaves,
        TargetDeviationMode deviationMode = TargetDeviationMode.Deviation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spec);

        OverlayPoint[] smoothed = SmoothByOctaves(source, smoothingInverseOctaves);
        var target = new List<OverlayPoint>(smoothed.Length);
        bool hasDeviation = deviationMode != TargetDeviationMode.None;
        var deviation = hasDeviation
            ? new List<OverlayPoint>(smoothed.Length)
            : null;
        bool hasTolerance = toleranceDb > 0;
        var upper = hasTolerance
            ? new List<OverlayPoint>(smoothed.Length)
            : null;
        var lower = hasTolerance
            ? new List<OverlayPoint>(smoothed.Length)
            : null;

        foreach (OverlayPoint point in smoothed)
        {
            if (!(point.X > 0))
            {
                continue;
            }

            double targetValue = spec.Evaluate(point.X) + offsetDb;
            target.Add(new OverlayPoint(point.X, targetValue));
            if (hasDeviation)
            {
                // Correction is the EQ gain to reach the target (target − source);
                // Deviation is how far the response sits from it (source − target).
                double value = deviationMode == TargetDeviationMode.Correction
                    ? targetValue - point.Y
                    : point.Y - targetValue;
                deviation!.Add(new OverlayPoint(point.X, value));
            }
            upper?.Add(new OverlayPoint(point.X, targetValue + toleranceDb));
            lower?.Add(new OverlayPoint(point.X, targetValue - toleranceDb));
        }

        return new TargetCurveResult(
            target.ToArray(),
            deviation?.ToArray() ?? Array.Empty<OverlayPoint>(),
            upper?.ToArray() ?? Array.Empty<OverlayPoint>(),
            lower?.ToArray() ?? Array.Empty<OverlayPoint>());
    }

    public static OverlayPoint[] CalculateOperation(
        IReadOnlyList<OverlayPoint> a,
        IReadOnlyList<OverlayPoint> b,
        OverlayOperation operation,
        double blendFrequencyHz = 1_000,
        double blendWidthOctaves = 1,
        bool useAmplitudeSpace = false,
        bool wrapPhaseDifference = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Count == 0 ||
            b.Count < 2)
        {
            return Array.Empty<OverlayPoint>();
        }

        var result = new List<OverlayPoint>(a.Count);
        int bIndex = 0;
        bool useBlend = operation == OverlayOperation.Blend;
        double lowerBlend = 0;
        double upperBlend = 0;
        if (useBlend)
        {
            if (!double.IsFinite(blendFrequencyHz) || blendFrequencyHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendFrequencyHz));
            }
            if (!double.IsFinite(blendWidthOctaves) || blendWidthOctaves <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendWidthOctaves));
            }

            double halfWidth = blendWidthOctaves / 2.0;
            lowerBlend = blendFrequencyHz / Math.Pow(2.0, halfWidth);
            upperBlend = blendFrequencyHz * Math.Pow(2.0, halfWidth);
        }

        foreach (OverlayPoint aPoint in a)
        {
            while (bIndex + 1 < b.Count &&
                   b[bIndex + 1].X < aPoint.X)
            {
                bIndex++;
            }

            if (bIndex + 1 >= b.Count)
            {
                break;
            }

            OverlayPoint left = b[bIndex];
            OverlayPoint right = b[bIndex + 1];
            if (aPoint.X < left.X ||
                aPoint.X > right.X ||
                right.X <= left.X)
            {
                continue;
            }

            // Acoustic curves live on a logarithmic frequency axis, so the
            // interpolation position is logarithmic too — on sparse imported
            // curves a linear-Hz blend lands visibly off between octave-spaced
            // points. (Linear fallback only for degenerate non-positive X.)
            double position = left.X > 0 && aPoint.X > 0
                ? Math.Log(aPoint.X / left.X) / Math.Log(right.X / left.X)
                : (aPoint.X - left.X) / (right.X - left.X);
            // Wrapped phase must interpolate through the branch cut: a curve
            // stepping from +170° to −170° passes through ±180°, not through 0°
            // the way a linear blend of the raw numbers would — and the wrap of
            // the difference afterwards cannot recover the lost branch.
            double bValue = wrapPhaseDifference
                ? InterpolateWrappedDegrees(left.Y, right.Y, position)
                : left.Y + (right.Y - left.Y) * position;
            double aValue = aPoint.Y;
            if (useAmplitudeSpace)
            {
                aValue = DataHelper.DecibelsToAmplitude(aValue);
                bValue = DataHelper.DecibelsToAmplitude(bValue);
            }
            double value = useBlend
                ? ApplyBlend(aPoint.X, aValue, bValue, lowerBlend, upperBlend)
                : ApplyOperation(aValue, bValue, operation, wrapPhaseDifference);
            if (useAmplitudeSpace)
            {
                // A non-positive amplitude difference has no dB representation;
                // emit NaN so the plot draws an honest gap instead of the
                // -160 dB floor the conversion would clamp to.
                value = value > 0
                    ? DataHelper.AmplitudeToDecibels(value)
                    : double.NaN;
            }
            if (!double.IsInfinity(value))
            {
                result.Add(new OverlayPoint(aPoint.X, value));
            }
        }

        return result.ToArray();
    }

    private static double ApplyOperation(
        double a,
        double b,
        OverlayOperation operation,
        bool wrapPhaseDifference = false)
    {
        return operation switch
        {
            OverlayOperation.AMinusB => WrapDegrees(a - b, wrapPhaseDifference),
            OverlayOperation.BMinusA => WrapDegrees(b - a, wrapPhaseDifference),
            OverlayOperation.Sum => a + b,
            OverlayOperation.Average => (a + b) / 2,
            OverlayOperation.AbsoluteDifference =>
                Math.Abs(WrapDegrees(a - b, wrapPhaseDifference)),
            _ => double.NaN
        };
    }

    // Interpolates between two wrapped phase readings along the SHORT way
    // around the circle, by blending the unit phasors and taking the angle of
    // the result.
    private static double InterpolateWrappedDegrees(
        double fromDegrees,
        double toDegrees,
        double position)
    {
        double from = fromDegrees * Math.PI / 180.0;
        double to = toDegrees * Math.PI / 180.0;
        double x = (1.0 - position) * Math.Cos(from) + position * Math.Cos(to);
        double y = (1.0 - position) * Math.Sin(from) + position * Math.Sin(to);
        return Math.Atan2(y, x) * 180.0 / Math.PI;
    }

    // Maps a phase difference in degrees to the shortest angular distance in (-180, 180]
    // via atan2(sin, cos). Used only when comparing wrapped phase curves; left untouched
    // otherwise so unwrapped curves keep their accumulated slope.
    private static double WrapDegrees(double degrees, bool wrap)
    {
        if (!wrap || !double.IsFinite(degrees))
        {
            return degrees;
        }

        double radians = degrees * Math.PI / 180.0;
        return Math.Atan2(Math.Sin(radians), Math.Cos(radians)) * 180.0 / Math.PI;
    }

    private static double ApplyBlend(
        double frequency,
        double a,
        double b,
        double lowerBlend,
        double upperBlend)
    {
        if (frequency <= lowerBlend)
        {
            return a;
        }
        if (frequency >= upperBlend)
        {
            return b;
        }

        double t = (Math.Log2(frequency) - Math.Log2(lowerBlend)) /
            (Math.Log2(upperBlend) - Math.Log2(lowerBlend));
        t = Math.Clamp(t, 0, 1);
        double crossfade = 0.5 - 0.5 * Math.Cos(Math.PI * t);
        double blended = a + (b - a) * crossfade;
        return Math.Clamp(blended, Math.Min(a, b), Math.Max(a, b));
    }
}
