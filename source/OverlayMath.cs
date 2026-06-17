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

    public static OverlayPoint[] SmoothByOctaves(
        IReadOnlyList<OverlayPoint> points,
        int inverseOctaves)
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

        double halfWidth = 0.5 / inverseOctaves;
        var result = new OverlayPoint[points.Count];
        int left = 0;
        int right = 0;

        for (int center = 0; center < points.Count; center++)
        {
            OverlayPoint centerPoint = points[center];
            if (centerPoint.X <= 0)
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
                double distance =
                    Math.Abs(Math.Log2(points[sample].X) - centerOctaves);
                double weight = 0.5 *
                    (1 + Math.Cos(Math.PI * distance / halfWidth));
                weightedSum += points[sample].Y * weight;
                weightSum += weight;
            }

            result[center] = new OverlayPoint(
                centerPoint.X,
                weightSum > 1e-12
                    ? weightedSum / weightSum
                    : centerPoint.Y);
        }

        return result;
    }

    public static OverlayPoint[] CalculateOperation(
        IReadOnlyList<OverlayPoint> a,
        IReadOnlyList<OverlayPoint> b,
        OverlayOperation operation,
        double blendFrequencyHz = 1_000,
        double blendWidthOctaves = 1,
        bool useAmplitudeSpace = false)
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

            double position = (aPoint.X - left.X) / (right.X - left.X);
            double bValue = left.Y + (right.Y - left.Y) * position;
            double aValue = aPoint.Y;
            if (useAmplitudeSpace)
            {
                aValue = DataHelper.DecibelsToAmplitude(aValue);
                bValue = DataHelper.DecibelsToAmplitude(bValue);
            }
            double value = useBlend
                ? ApplyBlend(aPoint.X, aValue, bValue, lowerBlend, upperBlend)
                : ApplyOperation(aValue, bValue, operation);
            if (useAmplitudeSpace)
            {
                value = DataHelper.AmplitudeToDecibels(value);
            }
            if (double.IsFinite(value))
            {
                result.Add(new OverlayPoint(aPoint.X, value));
            }
        }

        return result.ToArray();
    }

    private static double ApplyOperation(
        double a,
        double b,
        OverlayOperation operation)
    {
        return operation switch
        {
            OverlayOperation.AMinusB => a - b,
            OverlayOperation.BMinusA => b - a,
            OverlayOperation.Sum => a + b,
            OverlayOperation.Average => (a + b) / 2,
            OverlayOperation.AbsoluteDifference => Math.Abs(a - b),
            _ => double.NaN
        };
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
