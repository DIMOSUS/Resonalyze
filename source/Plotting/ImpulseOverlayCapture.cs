using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Impulse trace stored framing-free (absolute sample index, raw linear value), because the view's origin, unit and level scale change;
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

/// <summary>Whole-record traces at 192 kHz are ~1M samples; thinning keeps overlay JSON small.</summary>
internal static class ImpulseOverlayThinning
{
    /// <summary>About what a peak + Length trace used to hold.</summary>
    public const int MaximumPoints = 32_768;

    /// <summary>Above the budget keeps each bucket's extremes at their own indices; averaging or subsampling would lose peaks.</summary>
    public static IReadOnlyList<SignalPoint> Thin(IReadOnlyList<SignalPoint> points)
    {
        if (points.Count <= MaximumPoints)
        {
            return points;
        }

        int buckets = MaximumPoints / 2;
        var thinned = new List<SignalPoint>(MaximumPoints);
        for (int bucket = 0; bucket < buckets; bucket++)
        {
            int start = (int)((long)bucket * points.Count / buckets);
            int end = (int)((long)(bucket + 1) * points.Count / buckets);
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

        return thinned;
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
