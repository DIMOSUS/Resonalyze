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
    /// <param name="chainSampleRateHz">
    /// The rate <paramref name="chain"/> is realized at — the CHANNEL's, not the
    /// capture's. A biquad's response depends on the rate it runs at, and the rate
    /// that matters is the one the DSP will use; the capture's own rate is already
    /// folded into its stored levels and has no say over a filter. Passing the
    /// capture's rate here would draw a prediction of a DSP nobody is building.
    /// </param>
    /// <param name="calibration">
    /// Which correction the result should carry, as a MODE rather than a curve.
    /// <list type="bullet">
    /// <item><b>Off</b> — the capture back at the level it was taken. Its own
    /// correction is undone on its own grid, before anything is interpolated: these
    /// corrections are additive per frequency, so the undo is exact, and doing it
    /// first keeps each value on the frequency it was frozen at.</item>
    /// <item><b>Own</b> — the capture exactly as stored. A moving-microphone pass was
    /// a measurement of its own through its own file; an array is several capsules
    /// each through theirs. Either way the answer is the one the capture already
    /// holds, and nothing beside it has standing to replace it.</item>
    /// <item><b>Specific</b> — a named curve in place of the capture's own. Defined
    /// only when the capture declares ONE correction; a capture whose positions
    /// carried different files has an aggregate that belongs to no single microphone,
    /// and no curve can be swapped for it. That case falls back to Own, which is the
    /// nearest thing that is true, rather than to a swap that is not.</item>
    /// </list>
    /// </param>
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

        // A swap the capture cannot support is not performed; it reads as Own.
        bool swap = calibration.Mode == SpatialAverageCalibrationMode.Specific &&
            !document.CalibrationIsAggregate;
        CalibrationFile? curve = swap ? calibration.Curve : null;
        List<SignalPoint> capture =
            calibration.Mode == SpatialAverageCalibrationMode.Off || swap
                ? Uncalibrated(document)
                : document.ToCurvePoints();
        var prepared = PreparedDspResponse.Create(chain, chainSampleRateHz);
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
        List<SignalPoint> smoothed = smoothingCode == 0 || points.Count < 2
            ? points
            : DataHelper.SmoothBandLevels(
                points,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode));

        // Calibration LAST, after the smoothing — the operation order the rest of the
        // app's frequency-response pipeline uses, and the one the same capture is
        // corrected under when the EQ Wizard opens it directly rather than through a
        // handoff. Correcting first and smoothing afterwards smooths the correction
        // too, so a frequency-dependent calibration file made one capture read
        // slightly differently by which route it arrived.
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

    /// <summary>
    /// How much louder the first curve is than the second over the band they share,
    /// in dB: the mean of the point POWERS on each curve over the indices where BOTH
    /// are finite, converted back to dB once. Null when no point is finite on both.
    /// </summary>
    /// <remarks>
    /// The energy-mean rule is the one the impulse-response band level uses
    /// (<c>VirtualCrossoverAnalysis.MeasureBandLevelDb</c>): averaging power lets the
    /// figure track loudness and shrug off narrow dips, where a dB mean would follow
    /// them down. That method weights its linear-spaced bins by 1/f; on the
    /// log-spaced grid these curves are built on, uniform weights say the same thing.
    /// The two curves must share one grid, and the points are paired on purpose: a
    /// gap on either side (a protective high-pass, the end of a capture's grid)
    /// removes that frequency from BOTH, rather than comparing one side's band
    /// against a different part of the other's.
    /// </remarks>
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

        // Both sums count the same points, so one ratio is the difference of the
        // two means; a zero says no shared point (or a level beyond any real dB
        // scale), and either way there is nothing honest to report.
        return leftPower > 0 && rightPower > 0
            ? 10.0 * Math.Log10(leftPower / rightPower)
            : null;
    }

    /// <summary>
    /// Several channels' hybrid curves on ONE grid combined into a group's level
    /// curve by adding their POWERS point by point. A point where no member has a
    /// value is a gap; a member's own gap simply contributes nothing there — its
    /// crossover has removed it from the group's output anyway.
    /// </summary>
    /// <remarks>
    /// Deliberately not the phasor sum the plot's hybrid Sum uses: a spatial
    /// average carries no phase, so the only sum a set of captures can state by
    /// themselves is the incoherent one. What this figure feeds — a LEVEL over a
    /// band spanning octaves — barely tells the two apart: the coherent
    /// cross-terms live in the junction overlaps, a fraction of the band, and
    /// they appear on both sides of a group to group comparison. Borrowing the
    /// point-measured phase to resolve them would put one microphone position's
    /// interference back into the very numbers the hybrid mode exists to free of
    /// it, at the price of full-length gated FFTs per group per frame.
    /// </remarks>
    public static List<SignalPoint> PowerSum(
        IReadOnlyList<IReadOnlyList<SignalPoint>> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (curves.Count == 0)
        {
            return [];
        }

        int count = curves.Min(curve => curve.Count);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double power = 0;
            bool any = false;
            foreach (IReadOnlyList<SignalPoint> curve in curves)
            {
                double db = curve[i].Y;
                if (double.IsFinite(db))
                {
                    power += Math.Pow(10.0, db / 10.0);
                    any = true;
                }
            }

            points.Add(new SignalPoint(
                curves[0][i].X,
                any ? 10.0 * Math.Log10(power) : double.NaN));
        }

        return points;
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
        // The SAME tolerance the snap below uses, and for the same reason. The drawn
        // grid and a capture's own grid are one logarithmic grid built two ways, and
        // their endpoints differ in the last ULPs: the capture stores 20.000000000000004
        // and the curve is drawn at exactly 20, which puts the first band at an index
        // of -3.3e-14. Rejected as "outside", that dropped the lowest band of every
        // hybrid channel — 20 Hz on a subwoofer, where there is content — for no
        // reason but arithmetic.
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
        // Landing ON a stored point must read that point, never its neighbour. The
        // interpolation below cannot do it: NaN·0 is NaN, so a finite value whose
        // successor is a gap would come back NaN and the gap would spread one point
        // backwards — the opposite of "a break is neither bridged nor spread".
        //
        // Snapped with a tolerance rather than tested for zero, because the display
        // grid and a capture's own grid are the SAME log grid: a frequency that
        // round-trips through the exponential and back lands a few ULPs off the
        // index it came from, and an exact test would miss the case that matters
        // most. A billionth of an index step is nothing — the step itself is about
        // a hundredth of an octave.
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
