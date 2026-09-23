using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One channel's bypass response at the mic position and the same driver's spatial average.</summary>
internal sealed record SpatialAverageAuditionChannel(
    Complex[] RawImpulseResponse,
    int SampleRate,
    MeasuredBand MeasuredBand,
    CalibrationFile? MicrophoneCalibration,
    LiveCaptureDocument? Capture);

/// <summary>Point minus average, dB per frequency; empty <c>SubtractDb</c> is the identity.</summary>
internal sealed record SpatialAverageAuditionCorrection(
    IReadOnlyList<SignalPoint> SubtractDb,
    double? DatumDb,
    double LowestDb,
    double HighestDb,
    int LimitedPoints)
{
    public static SpatialAverageAuditionCorrection None { get; } = new([], null, 0.0, 0.0, 0);

    public bool Corrects => SubtractDb.Count > 0;
}

internal sealed record SpatialAverageAuditionPlan(
    IReadOnlyList<SpatialAverageAuditionCorrection> Corrections,
    double SetOffsetDb,
    double SpreadDb)
{
    public int PointMeasuredCount => Corrections.Count(correction => !correction.Corrects);

    public bool Corrects => Corrections.Any(correction => correction.Corrects);
}

/// <summary>Per-channel magnitude correction so a render follows the spatial averages: <c>point − (average + setOffset)</c> on the bypass pair.</summary>
/// <remarks>Chain divides out; phase stays the point's. See docs/tech/spatial-average.md#audition-correction.</remarks>
internal static class SpatialAverageAudition
{
    /// <summary>Damage bound, dB either way; only reached where the difference is no longer evidence.</summary>
    public const double LimitDb = 12.0;

    /// <summary>1/6 octave: carries tonal balance, leaves the point response's narrow nulls out of the difference.</summary>
    public const double SmoothingOctaves = 1.0 / 6.0;

    private const int SmoothingCode = 6;

    /// <summary>Corrections levelled by one set offset (median of datums), in input order; channels without a datum stay uncorrected.</summary>
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

            // Each measurement through its own calibration, so aggregate array calibrations do not tilt the correction.
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
        double spread = known.Count < 2 ? 0.0 : known.Max() - known.Min();
        // One set never mixes methods (LiveCaptureDocument.JudgeSet).
        SpatialAverageMethod method = channels
            .Select(channel => channel.Capture)
            .OfType<LiveCaptureDocument>()
            .FirstOrDefault()?.Method ?? SpatialAverageMethod.MovingMic;
        double setOffset = SpatialAverageOffsets.SetOffsetDb(method, known);

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

    /// <summary>Applies the correction as a linear-phase FIR; every channel gets the same length, so the half-kernel delay is common.</summary>
    public static Complex[] Apply(
        Complex[] response,
        SpatialAverageAuditionCorrection correction,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(correction);
        // Design() realizes the inverse of the curve it is given, which subtracts the point excess.
        double[] fir = CalibrationFirFilter.Design(
            frequencyHz => SampleDb(correction.SubtractDb, frequencyHz), sampleRate);
        var samples = new double[response.Length];
        for (int i = 0; i < response.Length; i++)
        {
            // Processed IR is real; the imaginary part is residue.
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

    /// <summary>Bypass response as band power means on the shared grid, the capture's estimator.</summary>
    /// <remarks>An interpolating resampler invented 11 dB differences; order is bands, smoothing, calibration.</remarks>
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

    /// <summary>Limited difference, bridged across gaps and held past the ends so the FIR magnitude has no steps.</summary>
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
