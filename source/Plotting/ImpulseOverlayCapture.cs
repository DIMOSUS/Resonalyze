using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// An impulse trace stored the way it can be re-drawn later: the X coordinate is the
/// record's own ABSOLUTE sample index and the Y is the trace's raw linear value, both
/// free of the framing that was on screen when it was captured.
///
/// A frequency-domain overlay can be stored as drawn, because its axis means the same
/// thing forever. A time-domain one cannot: the impulse view's zero moves between the
/// record start, the arrival and the peak, its unit switches between milliseconds and
/// samples, and its levels are raw, percent or decibels — so a snapshot frozen in
/// drawn coordinates silently lands somewhere the live curve would never be. Storing
/// the sample index and the raw value keeps the two statements the record actually
/// makes, and <see cref="ImpulseOverlayFrame"/> puts them back on the current axes.
///
/// What CANNOT be undone this way is baked in: the band filter and the envelope
/// smoothing are part of the values themselves, so an overlay stays the band and the
/// smoothing it was captured with.
/// </summary>
/// <param name="Kind">
/// Which trace this is. The impulse and the step carry a polarity the view can flip;
/// the envelope is a magnitude and has none.
/// </param>
/// <param name="PeakReference">
/// The record's own peak at capture time, in the same raw units. It is the fallback
/// when the current view has no measurement to normalize against.
/// </param>
internal readonly record struct ImpulseOverlayCapture(
    IReadOnlyList<SignalPoint> Samples,
    AnalysisCurveKind Kind,
    double PeakReference,
    int SampleRateHz);

/// <summary>
/// The impulse view's current framing: everything needed to put a stored trace back on
/// the axes as they are now.
/// </summary>
/// <param name="ReferencePeak">
/// The peak the live view normalizes against, or null when there is no measurement on
/// screen. A stored overlay is re-scaled against the LIVE record's peak on purpose:
/// how far the snapshot sits below what is being measured now is the comparison, and
/// re-normalizing it to its own peak would erase exactly that (the lesson the Time
/// Alignment envelopes and the Compare curve already carry).
/// </param>
internal readonly record struct ImpulseOverlayFrame(
    ImpulseResponseOptions Options,
    double OriginSamples,
    double? ReferencePeak,
    int SampleRate);

/// <summary>
/// Keeps a stored impulse trace to a size a settings file can hold. The traces cover
/// the whole record now, which at 192 kHz is a million samples — a slot storing those
/// verbatim would write tens of megabytes of JSON per overlay.
/// </summary>
internal static class ImpulseOverlayThinning
{
    /// <summary>
    /// Points kept at most. Chosen to land where the drawn curve used to sit before the
    /// traces covered the whole record (peak + Length, tens of thousands of samples), so
    /// overlay files do not grow past what they already held.
    /// </summary>
    public const int MaximumPoints = 32_768;

    /// <summary>
    /// Every point below the budget; above it, the EXTREMES of each bucket at their own
    /// sample indices. Averaging would round the peaks off a trace whose whole subject
    /// is where the peaks are, and plain subsampling would step over them entirely.
    /// </summary>
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

            // In time order, so the stored curve still reads left to right.
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
    /// <summary>
    /// Draws a stored trace on the axes the view has now.
    /// </summary>
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
        // The record's own rate converts its samples to time; the origin belongs to the
        // live view and is converted with the live rate. Both land in milliseconds, so a
        // snapshot from a differently clocked record still sits at the right instant.
        int captureRate = capture.SampleRateHz > 0 ? capture.SampleRateHz : frame.SampleRate;
        bool invert = options.Invert && capture.Kind != AnalysisCurveKind.ImpulseEnvelope;
        double sign = invert ? -1.0 : 1.0;

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
                x = sample.X - frame.OriginSamples;
            }

            // The step is already a ratio of the reference it was normalized against, so
            // it carries no level to re-scale — only its polarity can be flipped.
            double y = capture.Kind == AnalysisCurveKind.ImpulseStep
                ? sample.Y * sign
                : DataHelper.ScaleImpulseAmplitude(
                    sample.Y * sign, options.AmplitudeScale, reference);
            points[i] = new DataPoint(x, y);
        }

        return points;
    }
}
