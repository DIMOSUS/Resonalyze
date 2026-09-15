using Resonalyze.Dsp;

namespace Resonalyze;

public static class OverlayMath
{
    private const double GaussianRadiusSigma = 3.0;
    private const double GaussianTaperStartSigma = 2.5;

    public static bool SupportsAmplitudeSpace(Mode mode)
    {
        return mode is Mode.FrequencyResponse or Mode.LiveSpectrum;
    }

    /// <summary>Pass <paramref name="psychoacousticMagnitude"/> false for phase, GD and coherence (decodes to plain 1/6 octave).</summary>
    public static OverlayPoint[] SmoothByOctaves(
        IReadOnlyList<OverlayPoint> points,
        int inverseOctaves,
        bool psychoacousticMagnitude = true)
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

        bool psychoacoustic = psychoacousticMagnitude &&
            SpectrumSmoothing.IsPsychoacoustic(inverseOctaves);
        var result = new OverlayPoint[points.Count];

        for (int center = 0; center < points.Count; center++)
        {
            OverlayPoint centerPoint = points[center];
            if (centerPoint.X <= 0 || double.IsNaN(centerPoint.Y))
            {
                result[center] = centerPoint;
                continue;
            }

            double centerOctaves = Math.Log2(centerPoint.X);
            double smoothingOctaves = psychoacoustic
                ? SpectrumSmoothing.PsychoacousticOctaves(centerPoint.X)
                : SpectrumSmoothing.SmoothingOctaves(inverseOctaves);
            double halfWidth = 0.5 * smoothingOctaves;
            double radius = psychoacoustic
                ? GaussianRadiusSigma * smoothingOctaves / 2.354820045
                : halfWidth;
            int left = center;
            while (left > 0 &&
                   points[left - 1].X > 0 &&
                   Math.Log2(points[left - 1].X) >= centerOctaves - radius)
            {
                left--;
            }
            int right = center;
            while (right + 1 < points.Count &&
                   points[right + 1].X > 0 &&
                   Math.Log2(points[right + 1].X) <= centerOctaves + radius)
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
                double weight;
                double sampleValue;
                if (psychoacoustic)
                {
                    double sigma = smoothingOctaves / 2.354820045;
                    double normalized = distance / sigma;
                    if (normalized >= GaussianRadiusSigma)
                    {
                        continue;
                    }

                    double taper = normalized <= GaussianTaperStartSigma
                        ? 1.0
                        : 0.5 * (1.0 + Math.Cos(
                            Math.PI *
                            (normalized - GaussianTaperStartSigma) /
                            (GaussianRadiusSigma - GaussianTaperStartSigma)));
                    weight = Math.Exp(-0.5 * normalized * normalized) * taper;
                    double amplitude = Math.Pow(10.0, points[sample].Y / 20.0);
                    sampleValue = amplitude * amplitude * amplitude;
                }
                else
                {
                    weight = 0.5 *
                        (1 + Math.Cos(Math.PI * distance / halfWidth));
                    sampleValue = points[sample].Y;
                }

                weightedSum += sampleValue * weight;
                weightSum += weight;
            }

            double value = weightSum > 1e-12
                ? weightedSum / weightSum
                : centerPoint.Y;
            if (psychoacoustic)
            {
                value = 20.0 * Math.Log10(Math.Cbrt(value));
            }

            result[center] = new OverlayPoint(centerPoint.X, value);
        }

        return result;
    }

    /// <summary>The slot offset shifts the target before the deviation; the measurement is smoothed first to avoid jitter.</summary>
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

    /// <summary>dB added by a slope hinged at <paramref name="pivotHz"/> (0 dB there); either sign.</summary>
    public static double TiltDb(
        double frequencyHz,
        double tiltDbPerOctave,
        double pivotHz)
    {
        if (tiltDbPerOctave == 0 ||
            !double.IsFinite(tiltDbPerOctave) ||
            !(frequencyHz > 0) ||
            !(pivotHz > 0))
        {
            return 0;
        }

        return tiltDbPerOctave * Math.Log2(frequencyHz / pivotHz);
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

        // B is not read; the caller may not have resolved one.
        if (operation == OverlayOperation.CurveA)
        {
            return a.ToArray();
        }

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

            // Log-frequency interpolation: linear Hz lands visibly off on sparse octave-spaced curves.
            double position = left.X > 0 && aPoint.X > 0
                ? Math.Log(aPoint.X / left.X) / Math.Log(right.X / left.X)
                : (aPoint.X - left.X) / (right.X - left.X);
            // Wrapped phase interpolates through ±180°, not 0°; wrapping the difference later cannot recover the branch.
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
                // NaN for a non-positive amplitude difference: an honest gap instead of the -160 dB floor.
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

    // Short way around the circle via blended unit phasors.
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

    // Only for wrapped curves; unwrapped curves keep their accumulated slope.
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
