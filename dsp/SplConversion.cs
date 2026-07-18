using System;
using System.Collections.Generic;

namespace Resonalyze.Dsp;

/// <summary>
/// Places the loopback-referenced frequency-response curves on an absolute
/// dB SPL axis.
/// <para>
/// The primary magnitude is a transfer function (microphone ÷ loopback), so its
/// dB is relative (dBr); one per-measurement offset turns it into SPL:
/// <c>K = loopbackPeakDbFs + calibrationOffsetDb</c>. The distortion and noise
/// traces are ratios to the fundamental (dBc); adding the primary's own SPL at
/// their frequency turns each ratio into an absolute level, so the whole plot
/// reads in dB SPL. Any per-frequency microphone correction cancels: it is already
/// in the displayed primary, so it rides along without entering <c>K</c>.
/// </para>
/// </summary>
public static class SplConversion
{
    /// <summary>
    /// Returns the curves converted to dB SPL using <paramref name="offsetDb"/>
    /// (the measurement's <c>K</c>). The primary shifts by <c>K</c>; every
    /// fundamental-relative curve is lifted by the primary's SPL at its own
    /// frequency. Curves that carry no absolute reference and have no primary to
    /// anchor to are returned unchanged — the caller decides whether SPL is
    /// available at all.
    /// </summary>
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

    // The distortion and noise traces are all expressed relative to the
    // fundamental, so each becomes an absolute level the same way.
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

    // dBc(f) + primaryDbr(f) + K. The primary is sampled at the curve's own
    // frequency (the distortion traces live on a different grid).
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

    // Linear interpolation of a frequency-ascending dB curve, clamped at the ends.
    // The primary is smoothed and densely sampled on a log grid, so linear
    // interpolation between neighbours sits well under the plotting resolution.
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
