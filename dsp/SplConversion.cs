using System;
using System.Collections.Generic;

namespace Resonalyze.Dsp;

/// <summary>Puts loopback-referenced curves on dB SPL: primary (dBr) + <c>K = loopbackPeakDbFs + calibrationOffsetDb</c>;
/// dBc traces are lifted by the primary's SPL at their frequency. Mic correction is already in the primary, not in K.</summary>
public static class SplConversion
{
    /// <summary>Curves with no absolute reference and no primary to anchor to are returned unchanged.</summary>
    public static IReadOnlyList<AnalysisCurve> ToSoundPressureLevel(
        IReadOnlyList<AnalysisCurve> curves,
        double offsetDb)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (!double.IsFinite(offsetDb))
        {
            throw new ArgumentOutOfRangeException(nameof(offsetDb));
        }

        IReadOnlyList<SignalPoint>? primary = null;
        foreach (AnalysisCurve curve in curves)
        {
            if (curve.Kind == AnalysisCurveKind.Primary)
            {
                primary = curve.Points;
                break;
            }
        }

        var result = new List<AnalysisCurve>(curves.Count);
        foreach (AnalysisCurve curve in curves)
        {
            if (curve.Kind == AnalysisCurveKind.Primary)
            {
                result.Add(curve with { Points = ShiftBy(curve.Points, offsetDb) });
            }
            else if (IsFundamentalRelative(curve.Kind) && primary is { Count: > 0 })
            {
                result.Add(curve with { Points = Lift(curve.Points, primary, offsetDb) });
            }
            else
            {
                result.Add(curve);
            }
        }

        return result;
    }

    private static bool IsFundamentalRelative(AnalysisCurveKind kind) => kind is
        AnalysisCurveKind.SecondHarmonic or
        AnalysisCurveKind.ThirdHarmonic or
        AnalysisCurveKind.FourthHarmonic or
        AnalysisCurveKind.ThdPlusNoise or
        AnalysisCurveKind.NoiseFloor;

    private static SignalPoint[] ShiftBy(IReadOnlyList<SignalPoint> points, double offsetDb)
    {
        var shifted = new SignalPoint[points.Count];
        for (int i = 0; i < shifted.Length; i++)
        {
            SignalPoint point = points[i];
            shifted[i] = point with { Y = point.Y + offsetDb };
        }

        return shifted;
    }

    // The distortion traces live on a different grid, so sample the primary at each point.
    private static SignalPoint[] Lift(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<SignalPoint> primaryDbr,
        double offsetDb)
    {
        var lifted = new SignalPoint[points.Count];
        for (int i = 0; i < lifted.Length; i++)
        {
            SignalPoint point = points[i];
            double primaryAt = InterpolateDb(primaryDbr, point.X);
            lifted[i] = point with { Y = point.Y + primaryAt + offsetDb };
        }

        return lifted;
    }

    private static double InterpolateDb(IReadOnlyList<SignalPoint> curve, double x)
    {
        int count = curve.Count;
        if (x <= curve[0].X)
        {
            return curve[0].Y;
        }
        if (x >= curve[count - 1].X)
        {
            return curve[count - 1].Y;
        }

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) >> 1;
            if (curve[mid].X <= x)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        SignalPoint left = curve[low];
        SignalPoint right = curve[high];
        double span = right.X - left.X;
        if (span <= 0.0)
        {
            return left.Y;
        }

        double t = (x - left.X) / span;
        return left.Y + t * (right.Y - left.Y);
    }
}
