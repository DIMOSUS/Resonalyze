using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Re-renders an imported curve that has NO raw form — a dB SPL capture, whose band levels
/// cannot be re-gridded back to a raw spectrum without inventing data.
/// </summary>
/// <remarks>
/// Such a curve is not a dead end: these modes apply the microphone correction ADDITIVELY
/// per frequency, so the correction frozen at capture can be subtracted back out exactly
/// and another applied in its place. The order mirrors the primary frequency-response path
/// — uncalibrate, smooth, then calibrate. Everything happens on the curve's OWN
/// frequencies: no resampling, so the display range is never extrapolated beyond the bands
/// the analyzer actually resolved.
/// <para>
/// Smoothing is delegated to <see cref="DataHelper.SmoothBandLevels"/>, which shares its
/// core with the RTA's own resampler. Re-smoothing with a merely similar window would not
/// do: this curve goes into Auto Tune, and a mean taken in the wrong domain or over the
/// wrong window suppresses narrow peaks differently from the analyzer, so the fit would
/// chase a shape the measurement never had at that width.
/// </para>
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
            // The analyzer's own second pass, replayed over its own band levels on their
            // own grid — same window, same power-domain mean, same psychoacoustic form.
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
