using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel's magnitude built from a stored spatial average instead of from an
/// impulse response measured at one point: the capture, rebased onto the caller's
/// microphone calibration, with that channel's DSP chain added as its ANALYTIC
/// magnitude, on the caller's frequency grid and at the caller's display smoothing.
/// </summary>
/// <remarks>
/// Shared by the Virtual DSP plot and the EQ Wizard so the curve a tune is fitted to
/// is the curve the panel drew. It is exact rather than a convenience: a spatial
/// average is √⟨|H(f, r)|²⟩ over the listening volume, and a filter D(f) does not
/// depend on position, so ⟨|D·H|²⟩ = |D|²·⟨|H|²⟩ — the filter comes straight out of
/// the average. Delay and polarity are absent for the same reason: they are pure
/// phase, so this is the tonal balance alone.
/// <para>
/// The chain is added analytically and NOT as the difference between two gated
/// spectra. A spatial average is a steady-state curve with no window, and a gate does
/// not commute with a filter — the two readings part by several dB wherever the bank
/// rings longer than the window.
/// </para>
/// </remarks>
internal static class SpatialAverageHybrid
{
    /// <summary>
    /// The hybrid curve on <paramref name="frequenciesHz"/>, or null when the capture
    /// or the rate cannot support one. The level is the capture's own — the offset
    /// that puts a whole SET on the impulse responses' axis belongs to the set and is
    /// applied by the caller that knows it.
    /// </summary>
    /// <param name="calibration">
    /// The correction the result should carry. The capture's own is undone first, on
    /// the capture's own grid, before anything is interpolated: these corrections are
    /// additive per frequency, so the swap is exact, and doing it before the
    /// interpolation keeps each value on the frequency it was frozen at. Null means
    /// none — which is the right answer when the panel draws uncalibrated, since the
    /// curves beside this one are uncalibrated too.
    /// </param>
    public static List<SignalPoint>? BuildChannelCurve(
        LiveCaptureDocument document,
        DspChannelChain chain,
        int sampleRateHz,
        CalibrationFile? calibration,
        IReadOnlyList<double> frequenciesHz,
        int smoothingCode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        if (sampleRateHz <= 0 || frequenciesHz.Count == 0 || document.CurveDb.Length < 2)
        {
            return null;
        }

        List<SignalPoint> capture = Uncalibrated(document);
        var prepared = PreparedDspResponse.Create(chain, sampleRateHz);
        var points = new List<SignalPoint>(frequenciesHz.Count);
        foreach (double hz in frequenciesHz)
        {
            double level = Sample(document, capture, hz);
            if (double.IsNaN(level))
            {
                // The capture says it has nothing here — below a protective high-pass,
                // typically, or past the end of its grid. A break is the honest answer;
                // inventing a level would put a curve where no measurement exists, and
                // downstream that gap is what says "do not equalize here".
                points.Add(new SignalPoint(hz, double.NaN));
                continue;
            }

            if (calibration != null)
            {
                level -= calibration.GetDecibelCorrection(hz);
            }

            points.Add(new SignalPoint(
                hz,
                level + DataHelper.AmplitudeToDecibels(prepared.Response(hz).Magnitude)));
        }

        // Smoothing goes on the FINISHED curve, after the chain — the way a measured
        // curve through the same chain is smoothed. Smoothing the capture alone and
        // then adding an unsmoothed analytic filter would leave a steep crossover
        // corner razor sharp here while the measured curve beside it rounds off, and a
        // corner is exactly where the two get compared. A level-preserving mean of band
        // POWER, which passes a gap through and excludes it from its neighbours' means.
        return smoothingCode == 0 || points.Count < 2
            ? points
            : DataHelper.SmoothBandLevels(
                points,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode));
    }

    // The capture back at the level the analyzer measured, before any microphone
    // correction: the pipeline SUBTRACTS the correction, so undoing it adds it back.
    // On the capture's own grid, where each stored value belongs.
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

    // The stored curve at one frequency, interpolated on its own logarithmic grid.
    // Linear in dB between neighbours, and NaN as soon as either neighbour is NaN: a
    // gap must not be bridged by the points around it.
    private static double Sample(
        LiveCaptureDocument document, IReadOnlyList<SignalPoint> curve, double hz)
    {
        int count = curve.Count;
        double position = document.IndexOf(hz);
        if (double.IsNaN(position) || position < 0 || position > count - 1)
        {
            return double.NaN;
        }

        int low = (int)Math.Floor(position);
        int high = Math.Min(low + 1, count - 1);
        double fraction = position - low;
        return curve[low].Y + (curve[high].Y - curve[low].Y) * fraction;
    }
}
