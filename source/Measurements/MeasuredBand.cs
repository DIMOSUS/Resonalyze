using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The frequencies a measurement actually carries a measurement at.
/// </summary>
/// <remarks>
/// Two things narrow it, and they narrow it for the same reason. A protective
/// high-pass took the signal past what the compensation could invert, and the
/// compensation zeroed those bins. A band sweep never excited part of the range at
/// all, and the excitation gate zeroed those. Either way the response is exactly
/// zero there — and a WINDOWED spectrum of that refills it with the analysis
/// window's own leakage, which is smooth, plausible, and none of it measured.
/// Measured on the owner's tweeter, a sweep from 565 Hz drew 495 of its 1024 points
/// as a rolloff from -60 dB down to -96, where nothing had played.
/// <para>
/// Only for a response those zeroes are IN — a loopback transfer. A sweep
/// deconvolution is normalized by the digital excitation rather than gated against
/// a loopback, and it still carries the protective high-pass, so its edges are
/// signal the loudspeaker really produced and blanking them would delete a
/// measurement rather than a phantom.
/// </para>
/// </remarks>
public readonly record struct MeasuredBand(double LowestHz, double HighestHz)
{
    /// <summary>A response with nothing to hide: the default for every view.</summary>
    public static MeasuredBand Everything { get; } = new(0.0, double.PositiveInfinity);

    /// <summary>The low edge, or zero when there is none.</summary>
    public double LowEdgeHz =>
        LowestHz > 0.0 && double.IsFinite(LowestHz) ? LowestHz : 0.0;

    /// <summary>
    /// The high edge, or infinity when there is none — INCLUDING the zero a
    /// default-constructed band carries, which means "not narrowed" and must never
    /// be read as "nothing above DC".
    /// </summary>
    public double HighEdgeHz =>
        HighestHz > 0.0 && double.IsFinite(HighestHz)
            ? HighestHz
            : double.PositiveInfinity;

    /// <summary>Whether this band carries a measurement at that frequency.</summary>
    public bool Contains(double frequencyHz) =>
        frequencyHz >= LowEdgeHz && frequencyHz <= HighEdgeHz;

    /// <summary>
    /// Breaks a curve at every frequency NONE of these bands covers — including one
    /// that falls BETWEEN two of them rather than outside both.
    /// </summary>
    /// <remarks>
    /// A curve summed from several responses plays wherever any of them measured, so
    /// the hull of their bands describes its ends and nothing describes a hole in the
    /// middle: two sweeps that do not overlap leave a range where every contributor is
    /// zero at once, and a windowed spectrum of that is the analysis window and
    /// nothing else. Applied to the finished curve, like every other break.
    /// </remarks>
    public static IReadOnlyList<SignalPoint> MaskUnmeasured(
        IReadOnlyList<SignalPoint> curve,
        IReadOnlyList<MeasuredBand> bands)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count == 0)
        {
            return curve;
        }

        var masked = new List<SignalPoint>(curve.Count);
        foreach (SignalPoint point in curve)
        {
            bool measured = false;
            foreach (MeasuredBand band in bands)
            {
                if (band.Contains(point.X))
                {
                    measured = true;
                    break;
                }
            }

            masked.Add(measured ? point : new SignalPoint(point.X, double.NaN));
        }

        return masked;
    }

    /// <summary>
    /// What a measurement carries, given the protective high-pass divided back out
    /// of it and the band its sweep actually swept.
    /// </summary>
    /// <remarks>
    /// A null filter is "nobody recorded what this passed through", which is not
    /// "off": nothing is masked for it, because the alternative is breaking an old
    /// curve at a frequency belonging to a filter it may never have seen. An absent
    /// or nonsensical sweep band is read the same way.
    /// </remarks>
    public static MeasuredBand Resolve(
        ProtectiveHighPassConfiguration? measurementFilter,
        double achievedLowHz,
        double achievedHighHz,
        int sampleRate)
    {
        double lowest = ProtectiveHighPassConfiguration.LowestMeasuredFrequencyHz(
            measurementFilter, sampleRate);
        double highest = double.PositiveInfinity;
        if (achievedLowHz > 0 && achievedHighHz > achievedLowHz &&
            double.IsFinite(achievedLowHz) && double.IsFinite(achievedHighHz))
        {
            // The wider of the two limits below, because both are true at once: a
            // tweeter measured with a band sweep AND a protective high-pass is silent
            // wherever either of them says so.
            lowest = Math.Max(lowest, achievedLowHz);
            highest = achievedHighHz;
        }

        return new MeasuredBand(lowest, highest);
    }
}
