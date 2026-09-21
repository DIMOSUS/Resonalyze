using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Captured curve for the plot: uncalibrated oversampled spectrum plus calibration frozen on the display grid,
/// kept separate to preserve smooth-then-calibrate order. <paramref name="Spectrum"/> may be empty (no raw form).
/// </summary>
/// <param name="PointsCalibration">No-raw captures only: calibration baked into the drawn points, per point, so a consumer can undo it.</param>
public readonly record struct RawCurveCapture(
    IReadOnlyList<SignalPoint> Spectrum,
    IReadOnlyList<double> CalibrationCorrectionDb,
    int SmoothingCode,
    int? SampleRateHz = null,
    CalibrationFile? PointsCalibration = null,
    // Spectrum is stored unmasked (masking would let smoothing straddle the break), so the band travels with it.
    MeasuredBand Band = default);

internal static class RawCurveRenderer
{
    public const double StartFrequency = 20.0;
    public const double StopFrequency = 20_000.0;
    public const int PointCount = 1024;

    /// <summary>Calibration sampled per drawn point (no other grid for no-raw captures); null calibration yields zeros.</summary>
    public static double[] CaptureCalibrationCorrectionAt(
        CalibrationFile? calibration,
        IReadOnlyList<DataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var correction = new double[points.Count];
        if (calibration == null)
        {
            return correction;
        }

        for (int i = 0; i < correction.Length; i++)
        {
            correction[i] = calibration.GetDecibelCorrection(points[i].X);
        }

        return correction;
    }

    public static double[] CaptureCalibrationCorrection(CalibrationFile? calibration)
    {
        if (calibration == null)
        {
            return Array.Empty<double>();
        }

        var correction = new double[PointCount];
        for (int i = 0; i < correction.Length; i++)
        {
            double frequency = DataHelper.LogPositionToFrequency(
                i / (PointCount - 1.0),
                StartFrequency,
                StopFrequency);
            correction[i] = calibration.GetDecibelCorrection(frequency);
        }

        return correction;
    }

    /// <param name="band">Applied to the finished curve, since the spectrum is stored unmasked. Default = whole range.</param>
    public static List<SignalPoint> Render(
        IReadOnlyList<SignalPoint> spectrum,
        IReadOnlyList<double> calibrationCorrectionDb,
        int smoothing,
        MeasuredBand band = default)
    {
        List<SignalPoint> input = spectrum as List<SignalPoint> ?? spectrum.ToList();
        List<SignalPoint> result = DataHelper.LogarithmicResample(
            input,
            StartFrequency,
            StopFrequency,
            PointCount,
            calibration: null,
            SpectrumSmoothing.SmoothingOctaves(smoothing),
            dBUnpack: true,
            psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(smoothing));

        if (calibrationCorrectionDb.Count == 0)
        {
            return Mask(result, band);
        }
        if (calibrationCorrectionDb.Count != result.Count)
        {
            throw new ArgumentException(
                "Calibration correction must match the rendered curve grid.",
                nameof(calibrationCorrectionDb));
        }

        for (int i = 0; i < result.Count; i++)
        {
            SignalPoint point = result[i];
            result[i] = new SignalPoint(
                point.X,
                point.Y - calibrationCorrectionDb[i]);
        }

        return Mask(result, band);
    }

    // Last: a break is not a level and must not be corrected, smoothed, or leak into a neighbour's mean.
    private static List<SignalPoint> Mask(List<SignalPoint> curve, MeasuredBand band)
    {
        double low = band.LowEdgeHz;
        double high = band.HighEdgeHz;
        if (!(low > 0.0) && double.IsPositiveInfinity(high))
        {
            return curve;
        }

        for (int i = 0; i < curve.Count; i++)
        {
            if (curve[i].X < low || curve[i].X > high)
            {
                curve[i] = new SignalPoint(curve[i].X, double.NaN);
            }
        }

        return curve;
    }
}
