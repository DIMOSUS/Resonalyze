using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Re-renders a no-raw imported curve (dB SPL): uncalibrate, smooth, calibrate on its OWN frequencies.
/// See docs/tech/eq-auto-tuner.md#re-smoothing-imported-curves.
/// </summary>
internal static class EqWizardImportedCurve
{
    /// <summary>Corrections are per point and ignored on length mismatch; empty means none.</summary>
    public static IReadOnlyList<SignalPoint> Render(
        IReadOnlyList<SignalPoint> points,
        IReadOnlyList<double> capturedCorrectionDb,
        IReadOnlyList<double> targetCorrectionDb,
        int smoothingCode)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(capturedCorrectionDb);
        ArgumentNullException.ThrowIfNull(targetCorrectionDb);

        bool hasCaptured = capturedCorrectionDb.Count == points.Count && points.Count > 0;
        bool hasTarget = targetCorrectionDb.Count == points.Count && points.Count > 0;
        bool smooths = smoothingCode != 0 && points.Count >= 2;
        if (!hasCaptured && !hasTarget && !smooths)
        {
            return points;
        }

        // NaN (unmeasured band) stays NaN through every step, so gaps are neither filled nor spread.
        var working = new SignalPoint[points.Count];
        for (int i = 0; i < working.Length; i++)
        {
            double value = points[i].Y;
            if (hasCaptured)
            {
                value += capturedCorrectionDb[i];
            }

            working[i] = new SignalPoint(points[i].X, value);
        }

        IReadOnlyList<SignalPoint> smoothed = smooths
            // The analyzer's own second pass: same window, power-domain mean and psychoacoustic form.
            ? DataHelper.SmoothBandLevels(
                working,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode))
            : working;

        var result = new SignalPoint[points.Count];
        for (int i = 0; i < result.Length; i++)
        {
            double value = smoothed[i].Y;
            if (hasTarget)
            {
                value -= targetCorrectionDb[i];
            }

            result[i] = new SignalPoint(smoothed[i].X, value);
        }

        return result;
    }

    /// <summary>A null profile yields an empty correction (Off).</summary>
    public static IReadOnlyList<double> SampleCorrection(
        CalibrationFile? calibration,
        IReadOnlyList<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (calibration == null)
        {
            return Array.Empty<double>();
        }

        var correction = new double[points.Count];
        for (int i = 0; i < correction.Length; i++)
        {
            correction[i] = calibration.GetDecibelCorrection(points[i].X);
        }

        return correction;
    }
}
