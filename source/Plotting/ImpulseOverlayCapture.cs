using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Impulse trace stored framing-free (signed sample index, raw linear value), because the view's origin, unit and level scale change;
/// <see cref="ImpulseOverlayFrame"/> re-frames it. Band filter and envelope smoothing stay baked in. Steps are stored as the raw integral.</summary>
internal readonly record struct ImpulseOverlayCapture(
    IReadOnlyList<SignalPoint> Samples,
    AnalysisCurveKind Kind,
    double PeakReference,
    int SampleRateHz);

/// <summary>Current impulse view framing.</summary>
/// <param name="ReferencePeak">Live record's peak (null without one); overlays scale against it so their level difference stays visible.</param>
internal readonly record struct ImpulseOverlayFrame(
    ImpulseResponseOptions Options,
    double OriginSamples,
    double? ReferencePeak,
    int SampleRate);

/// <summary>Whole-record traces at 96-192 kHz run to millions of samples; thinning keeps overlay JSON small. A window around the
/// peak keeps every sample, since that is where the view zooms in (the arrival, early reflections and pre-ringing).</summary>
internal static class ImpulseOverlayThinning
{
    /// <summary>Consecutive samples kept around the peak: about 340 ms at 96 kHz.</summary>
    public const int DetailSamples = 32_768;

    /// <summary>Points for the rest of the record, as each bucket's extremes.</summary>
    public const int OutlinePoints = 16_384;

    public const int MaximumPoints = DetailSamples + OutlinePoints;

    // A quarter of the window before the peak, for pre-ringing.
    private const int DetailSamplesBeforePeak = DetailSamples / 4;

    /// <param name="points">Ascending in X.</param>
    /// <param name="peakX">Where the detail window is centred; the capture's own peak.</param>
    public static IReadOnlyList<SignalPoint> Thin(IReadOnlyList<SignalPoint> points, double peakX)
    {
        if (points.Count <= MaximumPoints)
        {
            return points;
        }

        int peak = 0;
        while (peak < points.Count - 1 && points[peak].X < peakX)
        {
            peak++;
        }

        int detailStart = Math.Clamp(peak - DetailSamplesBeforePeak, 0, points.Count - DetailSamples);
        int detailEnd = detailStart + DetailSamples;
        int outside = points.Count - DetailSamples;
        int bucketsBefore = (int)Math.Round((double)OutlinePoints / 2 * detailStart / outside);
        int bucketsAfter = OutlinePoints / 2 - bucketsBefore;

        var thinned = new List<SignalPoint>(MaximumPoints);
        AddExtremes(points, 0, detailStart, bucketsBefore, thinned);
        for (int i = detailStart; i < detailEnd; i++)
        {
            thinned.Add(points[i]);
        }

        AddExtremes(points, detailEnd, points.Count, bucketsAfter, thinned);
        return thinned;
    }

    // Each bucket's extremes at their own indices; averaging or subsampling would lose peaks.
    private static void AddExtremes(
        IReadOnlyList<SignalPoint> points,
        int from,
        int to,
        int buckets,
        List<SignalPoint> thinned)
    {
        int count = to - from;
        if (count <= 0 || buckets <= 0)
        {
            return;
        }

        for (int bucket = 0; bucket < buckets; bucket++)
        {
            int start = from + (int)((long)bucket * count / buckets);
            int end = from + (int)((long)(bucket + 1) * count / buckets);
            if (end <= start)
            {
                continue;
            }

            int lowest = start;
            int highest = start;
            for (int i = start + 1; i < end; i++)
            {
                if (points[i].Y < points[lowest].Y)
                {
                    lowest = i;
                }
                if (points[i].Y > points[highest].Y)
                {
                    highest = i;
                }
            }

            if (lowest == highest)
            {
                thinned.Add(points[lowest]);
                continue;
            }

            thinned.Add(points[Math.Min(lowest, highest)]);
            thinned.Add(points[Math.Max(lowest, highest)]);
        }
    }
}

internal static class ImpulseOverlayRenderer
{
    public static DataPoint[] Render(
        ImpulseOverlayCapture capture,
        ImpulseOverlayFrame frame)
    {
        ImpulseResponseOptions options = frame.Options;
        double reference = frame.ReferencePeak is { } live && live > 0.0
            ? live
            : capture.PeakReference > 0.0
                ? capture.PeakReference
                : 1.0;
        // Capture rate converts stored samples, live rate the origin; both in ms, so differently clocked records align.
        int captureRate = capture.SampleRateHz > 0 ? capture.SampleRateHz : frame.SampleRate;
        bool invert = options.Invert && capture.Kind != AnalysisCurveKind.ImpulseEnvelope;
        double sign = invert ? -1.0 : 1.0;

        double stepDivisor = 1.0;
        if (capture.Kind == AnalysisCurveKind.ImpulseStep)
        {
            if (options.NormalizeStepToImpulsePeak)
            {
                stepDivisor = reference;
            }
            else
            {
                double own = 0.0;
                foreach (SignalPoint sample in capture.Samples)
                {
                    own = Math.Max(own, Math.Abs(sample.Y));
                }

                stepDivisor = own > 0.0 ? own : 1.0;
            }
        }

        // Restate another rate's sample index in live units before removing the origin.
        double rateRatio = captureRate > 0 && frame.SampleRate > 0
            ? frame.SampleRate / (double)captureRate
            : 1.0;

        var points = new DataPoint[capture.Samples.Count];
        for (int i = 0; i < points.Length; i++)
        {
            SignalPoint sample = capture.Samples[i];
            double x;
            if (options.TimeUnit == ImpulseTimeUnit.Milliseconds &&
                captureRate > 0 &&
                frame.SampleRate > 0)
            {
                x = (sample.X * 1000.0 / captureRate) -
                    (frame.OriginSamples * 1000.0 / frame.SampleRate);
            }
            else
            {
                x = sample.X * rateRatio - frame.OriginSamples;
            }

            double y = capture.Kind == AnalysisCurveKind.ImpulseStep
                ? sample.Y * sign / stepDivisor
                : DataHelper.ScaleImpulseAmplitude(
                    sample.Y * sign, options.AmplitudeScale, reference);
            points[i] = new DataPoint(x, y);
        }

        return points;
    }
}

/// <summary>Overlay files written before the view drew negative time.</summary>
internal static class ImpulseOverlayLegacy
{
    /// <summary>A capture stored as indices 0..N-1 from record start, redrawn as the live view draws it: the second half before zero,
    /// and a step there re-anchored to be zero just before time zero. N is read off the last index, which thinning keeps within a bucket.</summary>
    public static IReadOnlyList<SignalPoint> FromRecordStartIndices(
        IReadOnlyList<SignalPoint> samples,
        AnalysisCurveKind kind)
    {
        SignalPoint last = samples.MaxBy(point => point.X);
        int length = (int)Math.Round(last.X) + 1;
        double stepTotal = kind == AnalysisCurveKind.ImpulseStep ? last.Y : 0.0;
        var before = new List<SignalPoint>();
        var after = new List<SignalPoint>();
        foreach (SignalPoint point in samples.OrderBy(point => point.X))
        {
            double lag = DspMath.ToSignedLag(point.X, length);
            if (lag < 0)
            {
                before.Add(new SignalPoint(lag, point.Y - stepTotal));
            }
            else
            {
                after.Add(point);
            }
        }

        before.AddRange(after);
        return before;
    }
}
