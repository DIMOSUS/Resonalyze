using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel as the audition correction reads it: the BYPASS response measured at
/// the microphone position, and the spatial average of the same driver.
/// </summary>
/// <param name="MicrophoneCalibration">
/// What the impulse response itself was recorded through — the measurement
/// microphone's own correction, as its file froze it. Null when the file names none.
/// </param>
internal sealed record SpatialAverageAuditionChannel(
    Complex[] RawImpulseResponse,
    int SampleRate,
    MeasuredBand MeasuredBand,
    CalibrationFile? MicrophoneCalibration,
    LiveCaptureDocument? Capture);

/// <summary>
/// How far one channel's point response sits ABOVE its spatial average, in dB at each
/// frequency — the curve a render subtracts to hear the average instead of the point.
/// </summary>
/// <param name="SubtractDb">
/// The correction itself, on the band grid it was measured on. Empty when the channel
/// has nothing to correct with, which is the identity: that channel keeps the response
/// the microphone measured.
/// </param>
/// <param name="DatumDb">
/// This channel's own median difference, before the set's common offset came off it.
/// Null when the two curves never overlapped enough to compare.
/// </param>
internal sealed record SpatialAverageAuditionCorrection(
    IReadOnlyList<SignalPoint> SubtractDb,
    double? DatumDb,
    double LowestDb,
    double HighestDb,
    int LimitedPoints)
{
    /// <summary>The do-nothing correction: this channel keeps its point response.</summary>
    public static SpatialAverageAuditionCorrection None { get; } = new([], null, 0.0, 0.0, 0);

    /// <summary>Whether this correction changes anything.</summary>
    public bool Corrects => SubtractDb.Count > 0;
}

/// <summary>
/// A whole audition's corrections: one per channel in the order they were handed in,
/// the single offset that levels the captures against the impulse responses, and how
/// far the channels disagreed about that offset.
/// </summary>
internal sealed record SpatialAverageAuditionPlan(
    IReadOnlyList<SpatialAverageAuditionCorrection> Corrections,
    double SetOffsetDb,
    double SpreadDb)
{
    /// <summary>How many channels are left on their point measurement.</summary>
    public int PointMeasuredCount => Corrections.Count(correction => !correction.Corrects);

    /// <summary>Whether any channel is corrected at all.</summary>
    public bool Corrects => Corrections.Any(correction => correction.Corrects);
}

/// <summary>
/// Makes an audition render read as the spatial averages instead of as the one
/// microphone position the impulse responses were measured at.
/// </summary>
/// <remarks>
/// The correction is per channel and purely a MAGNITUDE: what a driver's response has
/// to be filtered by so its level follows the average over the listening volume rather
/// than the point. It is <c>point − (average + setOffset)</c>, read on the BYPASS
/// measurements, and the DSP chain does not appear in it at all — the render is
/// <c>raw·D</c> and the target is <c>average·D</c>, so the chain divides out exactly.
/// That is also why the curve survives tuning: it is a property of the two
/// measurements, not of the tune, and a crossover change does not invalidate it.
/// <para>
/// Read on the RAW pair for a second reason as well. Between a PROCESSED response and
/// an analytic chain the filter does NOT cancel — the response is filtered and then
/// gated while the chain is computed exactly, and a gate does not commute with a
/// filter; on a real car at a 1.6 kHz junction the two readings parted by 23 dB in the
/// stopband (see <see cref="DataHelper.GetGatedSubstitutedMagnitudeSum"/>). A
/// correction built there would be that error, amplified into the render.
/// </para>
/// <para>
/// Both curves are read UNGATED, because the kernel being corrected carries the whole
/// decay and a spatial average carries it too; and both at the same fractional-octave
/// width, because a difference between two curves is only meaningful at one
/// resolution. The width is fixed rather than the plot's display selector: a
/// correction that moved with a smoothing combo would not be a property of the
/// measurements. It is coarse enough that the point response's own interference nulls
/// stay in the render — they are narrower than the width and never enter the
/// difference — which is deliberate. Inverting a null would demand twenty-odd dB of
/// boost at a frequency where this position has nothing, and the render would ring
/// rather than sound like the car.
/// </para>
/// <para>
/// What this does NOT do is make the render a spatially averaged one. Phase stays the
/// point measurement's, so the interference between two drivers at a junction is still
/// that position's interference — the same honest limit the hybrid plot carries.
/// </para>
/// </remarks>
internal static class SpatialAverageAudition
{
    /// <summary>
    /// How far a correction may reach, in dB either way.
    /// </summary>
    /// <remarks>
    /// A bound on the damage rather than on the physics: inside a driver's working band
    /// the two measurements are a few dB apart and the limit is never near. It is
    /// reached where the difference has stopped being evidence — a stopband where both
    /// curves read the noise floor, or a capture the set's offset does not fit — and
    /// there a bounded error is inaudible under the chain that put the channel there,
    /// while an unbounded one is not.
    /// </remarks>
    public const double LimitDb = 12.0;

    /// <summary>
    /// The width both curves are read at. 1/6 octave: fine enough to carry the tonal
    /// balance a spatial average exists to state, coarse enough to leave a point
    /// response's own nulls out of the difference.
    /// </summary>
    public const double SmoothingOctaves = 1.0 / 6.0;

    /// <summary>The same width as the capture side takes it, as a stored code.</summary>
    private const int SmoothingCode = 6;

    /// <summary>
    /// Every channel's correction, levelled as one set. The order of the result is the
    /// order of <paramref name="channels"/>, so a caller can pair them back up.
    /// </summary>
    /// <remarks>
    /// ONE offset for the whole set, and the median of the channels' own datums, for
    /// the reason the plot uses one: the captures are a set taken at one gain, so
    /// whatever separates them from the impulse responses separates them all by the
    /// same amount. Being a common gain it cannot change what the render sounds like,
    /// only how loud it is, and the render is normalized afterwards. What it does buy
    /// is corrections that sit around zero rather than around the gap between two
    /// families of measurement, which is what keeps them inside <see cref="LimitDb"/>.
    /// <para>
    /// A channel whose datum cannot be read is left uncorrected rather than corrected
    /// by the set's offset alone: with nothing to level it against its curve would
    /// arrive at whatever level the analyzer happened to be set to, and the limit would
    /// be the only thing between that and the render.
    /// </para>
    /// </remarks>
    public static SpatialAverageAuditionPlan Build(
        IReadOnlyList<SpatialAverageAuditionChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var points = new IReadOnlyList<SignalPoint>?[channels.Count];
        var averages = new IReadOnlyList<SignalPoint>?[channels.Count];
        var datums = new double?[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            SpatialAverageAuditionChannel channel = channels[i];
            if (channel.Capture == null)
            {
                continue;
            }

            IReadOnlyList<SignalPoint>? point = PointCurve(channel);
            if (point == null)
            {
                continue;
            }

            // No chain, so the rate handed over cannot matter — an identity response is
            // one at every frequency whatever it is realized at. The channel's own is
            // passed for the same reason the panel's datum passes it: there is nothing
            // else here that could be more nearly right.
            //
            // Each measurement through ITS OWN correction, which is what makes the
            // difference an acoustic quantity rather than partly a comparison of
            // capsules. Where one microphone took both — a moving-microphone pass, an
            // array of matched capsules — the two corrections are the same curve and
            // cancel, so this only shows up where they are NOT: a microphone array
            // whose positions carry individual calibrations reports an aggregate of
            // them, and reading both raw would have left the gap between that aggregate
            // and the measurement microphone's own file inside the correction, tilting
            // the whole render by it.
            IReadOnlyList<SignalPoint>? average = SpatialAverageHybrid.BuildChannelCurve(
                channel.Capture,
                DspChannelChain.Identity,
                channel.SampleRate,
                SpatialAverageCalibration.Own,
                [.. point.Select(band => band.X)],
                SmoothingCode);
            if (average == null)
            {
                continue;
            }

            points[i] = point;
            averages[i] = average;
            datums[i] = SpatialAverageOffsets.ChannelDatumDb(average, point);
        }

        List<double> known = datums
            .Where(datum => datum.HasValue)
            .Select(datum => datum!.Value)
            .ToList();
        double setOffset = known.Count == 0 ? 0.0 : SpatialAverageOffsets.Median(known);
        double spread = known.Count < 2 ? 0.0 : known.Max() - known.Min();

        var corrections = new SpatialAverageAuditionCorrection[channels.Count];
        for (int i = 0; i < channels.Count; i++)
        {
            corrections[i] = points[i] is { } point && averages[i] is { } average &&
                datums[i] is { } datum
                ? Correction(point, average, datum, setOffset)
                : SpatialAverageAuditionCorrection.None;
        }

        return new SpatialAverageAuditionPlan(corrections, setOffset, spread);
    }

    /// <summary>
    /// One channel's processed response with its correction applied, as a linear-phase
    /// FIR.
    /// </summary>
    /// <remarks>
    /// Linear phase, so the correction is a magnitude and nothing else: a minimum-phase
    /// design of the same curve would add its own group delay, different on every
    /// channel, to the very alignment an audition exists to judge. The price is a
    /// constant delay of half the kernel — and because EVERY channel goes through a
    /// filter of the same length, including the ones with nothing to correct (whose
    /// design is flat, which is exactly a delay), that constant is common and the
    /// arrivals stay where the tune put them.
    /// </remarks>
    public static Complex[] Apply(
        Complex[] response,
        SpatialAverageAuditionCorrection correction,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(correction);
        // Design() realizes the INVERSE of the correction it is handed, which is what
        // this curve wants: it states how far the point response sits ABOVE the
        // average, so subtracting it is the filter.
        double[] fir = CalibrationFirFilter.Design(
            frequencyHz => SampleDb(correction.SubtractDb, frequencyHz), sampleRate);
        var samples = new double[response.Length];
        for (int i = 0; i < response.Length; i++)
        {
            // The real part alone, the reading the audition's own trim takes: a
            // processed impulse response is real and its imaginary part is arithmetic
            // residue.
            samples[i] = response[i].Real;
        }

        double[] filtered = FastConvolution.Convolve(samples, fir);
        var result = new Complex[filtered.Length];
        for (int i = 0; i < filtered.Length; i++)
        {
            result[i] = filtered[i];
        }

        return result;
    }

    /// <summary>
    /// The channel's bypass response as band levels on the shared grid, read the way a
    /// capture is read, broken outside what it actually measured.
    /// </summary>
    /// <remarks>
    /// The ESTIMATOR is the whole point. A capture's bands are the mean of POWER over
    /// the bins each band spans, and a difference is only a difference when both sides
    /// are the same quantity: reading this side with the interpolating resampler
    /// instead put the two curves 11 dB apart at 500 Hz on a response with a single
    /// 5 ms reflection — a disagreement invented entirely by the arithmetic, which the
    /// correction would then have spent, most of the way to its limit. Smoothing after
    /// the bands and the calibration after the smoothing, the order the capture beside
    /// this one is built in.
    /// </remarks>
    private static IReadOnlyList<SignalPoint>? PointCurve(
        SpatialAverageAuditionChannel channel)
    {
        Complex[] response = channel.RawImpulseResponse;
        if (response.Length < 4 || channel.SampleRate <= 0)
        {
            return null;
        }

        double[] levels = DataHelper.GetUngatedBandLevels(
            new ImpulseMeasurementView(response, 0, channel.SampleRate));
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        if (levels.Length < 2 || levels.Length != grid.Count)
        {
            return null;
        }

        var raw = new List<SignalPoint>(levels.Length);
        for (int i = 0; i < levels.Length; i++)
        {
            raw.Add(new SignalPoint(grid[i], levels[i]));
        }

        List<SignalPoint> bands =
            DataHelper.SmoothBandLevels(raw, SmoothingOctaves, psychoacoustic: false);
        for (int i = 0; i < bands.Count; i++)
        {
            bands[i] = new SignalPoint(
                bands[i].X,
                channel.MeasuredBand.Contains(bands[i].X)
                    ? bands[i].Y -
                        (channel.MicrophoneCalibration?.GetDecibelCorrection(bands[i].X)
                            ?? 0.0)
                    : double.NaN);
        }

        return bands;
    }

    /// <summary>
    /// The correction curve itself: the difference, limited, and continuous everywhere
    /// the filter design will ask about it.
    /// </summary>
    /// <remarks>
    /// A frequency neither measurement covers gets no correction of its own but is not
    /// snapped to zero either — it is bridged from the neighbours that do, and held flat
    /// past the ends. A step at a band edge is a step in the filter's magnitude and the
    /// kernel would ring at it; bridging costs nothing, because a response is zeroed
    /// where it was never measured and any gain over zero is still zero.
    /// </remarks>
    private static SpatialAverageAuditionCorrection Correction(
        IReadOnlyList<SignalPoint> point,
        IReadOnlyList<SignalPoint> average,
        double datumDb,
        double setOffsetDb)
    {
        int count = Math.Min(point.Count, average.Count);
        var subtract = new double[count];
        double lowest = double.PositiveInfinity;
        double highest = double.NegativeInfinity;
        int limited = 0;
        for (int i = 0; i < count; i++)
        {
            double difference = point[i].Y - (average[i].Y + setOffsetDb);
            if (!double.IsFinite(difference))
            {
                subtract[i] = double.NaN;
                continue;
            }

            double bounded = Math.Clamp(difference, -LimitDb, LimitDb);
            if (bounded != difference)
            {
                limited++;
            }

            subtract[i] = bounded;
            lowest = Math.Min(lowest, bounded);
            highest = Math.Max(highest, bounded);
        }

        if (!Bridge(subtract))
        {
            return SpatialAverageAuditionCorrection.None;
        }

        var curve = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            curve.Add(new SignalPoint(point[i].X, subtract[i]));
        }

        return new SpatialAverageAuditionCorrection(curve, datumDb, lowest, highest, limited);
    }

    /// <summary>
    /// Fills the gaps in place — linearly across an interior one, flat past the ends.
    /// False when there was nothing to fill from.
    /// </summary>
    private static bool Bridge(double[] values)
    {
        int first = Array.FindIndex(values, double.IsFinite);
        if (first < 0)
        {
            return false;
        }

        int last = Array.FindLastIndex(values, double.IsFinite);
        for (int i = 0; i < first; i++)
        {
            values[i] = values[first];
        }

        for (int i = last + 1; i < values.Length; i++)
        {
            values[i] = values[last];
        }

        for (int i = first + 1; i < last; i++)
        {
            if (double.IsFinite(values[i]))
            {
                continue;
            }

            int next = i + 1;
            while (!double.IsFinite(values[next]))
            {
                next++;
            }

            double step = (values[next] - values[i - 1]) / (next - i + 1);
            for (int gap = i; gap < next; gap++)
            {
                values[gap] = values[i - 1] + step * (gap - i + 1);
            }

            i = next;
        }

        return true;
    }

    /// <summary>
    /// The correction at one frequency: linear in dB between the two bands around it,
    /// and the nearest band's own value outside the grid.
    /// </summary>
    /// <remarks>
    /// Held rather than faded to zero past the ends, for the reason the gaps are
    /// bridged: the filter is designed from DC to Nyquist and a step anywhere in that
    /// magnitude is a step the kernel rings at. The correction at the ends of the grid
    /// is the correction of a band the driver barely reaches, so holding it is holding
    /// a small number.
    /// </remarks>
    private static double SampleDb(IReadOnlyList<SignalPoint> curve, double frequencyHz)
    {
        if (curve.Count == 0)
        {
            return 0.0;
        }

        if (frequencyHz <= curve[0].X)
        {
            return curve[0].Y;
        }

        if (frequencyHz >= curve[^1].X)
        {
            return curve[^1].Y;
        }

        int low = 0;
        int high = curve.Count - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (curve[middle].X <= frequencyHz)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        double span = curve[high].X - curve[low].X;
        if (span <= 0.0)
        {
            return curve[low].Y;
        }

        double fraction = (frequencyHz - curve[low].X) / span;
        return curve[low].Y + (curve[high].Y - curve[low].Y) * fraction;
    }
}
