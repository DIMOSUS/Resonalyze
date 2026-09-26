using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A channel magnitude from a spatial average plus the chain's analytic magnitude; shared by Virtual DSP and the EQ Wizard.</summary>
/// <remarks>Exact because a filter is position-independent. See docs/tech/spatial-average.md#hybrid-channel-curve.</remarks>
internal static class SpatialAverageHybrid
{
    /// <summary>Hybrid curve at the capture's own level (the set offset is the caller's), or null.</summary>
    /// <param name="chainSampleRateHz">The channel's DSP rate, not the capture's.</param>
    /// <param name="calibration">Off undoes the capture's correction; Own keeps it; Specific swaps it unless the correction is an aggregate (then Own).
    /// A fixed calibration ignores all three.</param>
    public static List<SignalPoint>? BuildChannelCurve(
        LiveCaptureDocument document,
        DspChannelChain chain,
        int chainSampleRateHz,
        SpatialAverageCalibration calibration,
        IReadOnlyList<double> frequenciesHz,
        int smoothingCode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        if (chainSampleRateHz <= 0 || frequenciesHz.Count == 0 ||
            document.CurveDb.Length < 2)
        {
            return null;
        }

        if (document.CalibrationFixed)
        {
            calibration = SpatialAverageCalibration.Own;
        }

        bool swap = calibration.Mode == SpatialAverageCalibrationMode.Specific &&
            !document.CalibrationIsAggregate;
        CalibrationFile? curve = swap ? calibration.Curve : null;
        List<SignalPoint> capture =
            calibration.Mode == SpatialAverageCalibrationMode.Off || swap
                ? Uncalibrated(document)
                : document.ToCurvePoints();
        Complex[] responses =
            PreparedDspResponse.Create(chain, chainSampleRateHz).Responses(frequenciesHz);
        var points = new List<SignalPoint>(frequenciesHz.Count);
        for (int i = 0; i < frequenciesHz.Count; i++)
        {
            double hz = frequenciesHz[i];
            double level = Sample(document, capture, hz);
            if (double.IsNaN(level))
            {
                // No capture data here (e.g. below the protective high-pass): a break, which downstream means "do not equalize".
                points.Add(new SignalPoint(hz, double.NaN));
                continue;
            }

            points.Add(new SignalPoint(
                hz,
                level + DataHelper.AmplitudeToDecibels(responses[i].Magnitude)));
        }

        // Smooth the finished curve after the chain, as measured curves are; power mean that passes gaps through.
        List<SignalPoint> smoothed = smoothingCode == 0 || points.Count < 2
            ? points
            : DataHelper.SmoothBandLevels(
                points,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode));

        // Calibration last, after smoothing, matching the app pipeline and the EQ Wizard's direct route.
        if (curve == null)
        {
            return smoothed;
        }

        for (int i = 0; i < smoothed.Count; i++)
        {
            smoothed[i] = new SignalPoint(
                smoothed[i].X,
                smoothed[i].Y - curve.GetDecibelCorrection(smoothed[i].X));
        }

        return smoothed;
    }

    /// <summary>Power-mean level of <paramref name="left"/> over <paramref name="right"/>, dB, over points finite on both (one shared grid); null when none.</summary>
    public static double? BandLevelDeltaDb(
        IReadOnlyList<SignalPoint> left,
        IReadOnlyList<SignalPoint> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        int count = Math.Min(left.Count, right.Count);
        double leftPower = 0;
        double rightPower = 0;
        for (int i = 0; i < count; i++)
        {
            double leftDb = left[i].Y;
            double rightDb = right[i].Y;
            if (!double.IsFinite(leftDb) || !double.IsFinite(rightDb))
            {
                continue;
            }

            leftPower += Math.Pow(10.0, leftDb / 10.0);
            rightPower += Math.Pow(10.0, rightDb / 10.0);
        }

        return leftPower > 0 && rightPower > 0
            ? 10.0 * Math.Log10(leftPower / rightPower)
            : null;
    }

    /// <summary>Group level curve as a power sum. A NaN outside a member's band is absence; inside it breaks the group point.</summary>
    /// <remarks>Incoherent by necessity (no phase). See docs/tech/spatial-average.md#level-read-outs.</remarks>
    public static List<SignalPoint> PowerSum(
        IReadOnlyList<IReadOnlyList<SignalPoint>> curves,
        IReadOnlyList<(double LowHz, double HighHz)> bands)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(bands);
        if (curves.Count != bands.Count)
        {
            throw new ArgumentException(
                "Every curve needs its channel's band.", nameof(bands));
        }

        if (curves.Count == 0)
        {
            return [];
        }

        int count = curves.Min(curve => curve.Count);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double x = curves[0][i].X;
            double power = 0;
            bool any = false;
            bool missing = false;
            for (int c = 0; c < curves.Count; c++)
            {
                double db = curves[c][i].Y;
                if (double.IsFinite(db))
                {
                    power += Math.Pow(10.0, db / 10.0);
                    any = true;
                }
                else if (x >= bands[c].LowHz && x <= bands[c].HighHz)
                {
                    missing = true;
                    break;
                }
            }

            points.Add(new SignalPoint(
                x,
                any && !missing ? 10.0 * Math.Log10(power) : double.NaN));
        }

        return points;
    }

    // Undo the subtracted correction on the capture's own grid.
    private static List<SignalPoint> Uncalibrated(LiveCaptureDocument document)
    {
        List<SignalPoint> points = document.ToCurvePoints();
        double[] correction = document.CalibrationCorrectionDb;
        if (correction.Length != points.Count)
        {
            return points;
        }

        for (int i = 0; i < points.Count; i++)
        {
            points[i] = new SignalPoint(points[i].X, points[i].Y + correction[i]);
        }

        return points;
    }

    // Linear in dB; NaN if either neighbour is NaN (gaps are never bridged).
    private static double Sample(
        LiveCaptureDocument document, IReadOnlyList<SignalPoint> curve, double hz)
    {
        int count = curve.Count;
        double position = document.IndexOf(hz);
        // Grid endpoints differ by ULPs (20.000000000000004 vs 20); exact bounds dropped the lowest band.
        const double SnapTolerance = 1e-9;
        if (double.IsNaN(position) ||
            position < -SnapTolerance || position > count - 1 + SnapTolerance)
        {
            return double.NaN;
        }

        position = Math.Clamp(position, 0.0, count - 1.0);
        int low = (int)Math.Floor(position);
        int high = Math.Min(low + 1, count - 1);
        double fraction = position - low;
        // Snap onto a stored point (tolerance for log-grid round-trip): NaN·0 would otherwise spread a gap backwards.
        if (fraction <= SnapTolerance || high == low)
        {
            return curve[low].Y;
        }

        if (fraction >= 1.0 - SnapTolerance)
        {
            return curve[high].Y;
        }

        return curve[low].Y + (curve[high].Y - curve[low].Y) * fraction;
    }
}
