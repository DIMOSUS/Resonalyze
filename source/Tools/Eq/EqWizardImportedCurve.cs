using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Re-renders an imported curve that has NO raw form — a dB SPL RTA or frequency response,
/// whose band levels cannot be re-gridded back to a raw spectrum without inventing data.
/// </summary>
/// <remarks>
/// Such a curve is not a dead end: these modes apply the microphone correction ADDITIVELY
/// per frequency, so the correction frozen at capture can be subtracted back out exactly
/// and another applied in its place. The order mirrors the primary frequency-response path
/// — uncalibrate, smooth, then calibrate — so a curve re-smoothed here matches what the
/// measuring mode would have drawn at that width. Everything happens on the curve's OWN
/// frequencies: no resampling, so the display range is never extrapolated beyond the bands
/// the analyzer actually resolved.
/// </remarks>
internal static class EqWizardImportedCurve
{
    /// <summary>
    /// Renders <paramref name="points"/> with <paramref name="capturedCorrectionDb"/>
    /// removed, <paramref name="smoothingCode"/> applied, and
    /// <paramref name="targetCorrectionDb"/> applied in its place. Both corrections are
    /// per-point and are ignored unless they line up with the points — a mismatched length
    /// is not aligned to these frequencies at all. An empty correction means none, so
    /// passing empty for both and a smoothing code of 0 returns the input untouched.
    /// </summary>
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

        // Back to the uncalibrated level the analyzer measured. A NaN (a band below the
        // measurement threshold) stays NaN through every step, so gaps are neither filled
        // nor spread.
        var working = new OverlayPoint[points.Count];
        for (int i = 0; i < working.Length; i++)
        {
            double value = points[i].Y;
            if (hasCaptured)
            {
                value += capturedCorrectionDb[i];
            }

            working[i] = new OverlayPoint(points[i].X, value);
        }

        if (smooths)
        {
            // The overlay smoother works in place on these very frequencies, so the curve
            // keeps its own band grid. Magnitude semantics: this path only ever carries a
            // magnitude response, which is what the psychoacoustic width is defined for.
            working = OverlayMath.SmoothByOctaves(
                working, smoothingCode, psychoacousticMagnitude: true);
        }

        var result = new SignalPoint[points.Count];
        for (int i = 0; i < result.Length; i++)
        {
            double value = working[i].Y;
            if (hasTarget)
            {
                value -= targetCorrectionDb[i];
            }

            result[i] = new SignalPoint(working[i].X, value);
        }

        return result;
    }

    /// <summary>
    /// Samples a calibration profile at each of <paramref name="points"/>' frequencies,
    /// giving the per-point correction <see cref="Render"/> takes. A null profile yields
    /// an empty correction — "none", which is exactly what the Off mode needs.
    /// </summary>
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
