using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Frequencies a loopback transfer actually measured (HP compensation cap, sweep band). See docs/tech/sweep-measurement.md#measured-band.</summary>
public readonly record struct MeasuredBand(double LowestHz, double HighestHz)
{
    public static MeasuredBand Everything { get; } = new(0.0, double.PositiveInfinity);

    public double LowEdgeHz =>
        LowestHz > 0.0 && double.IsFinite(LowestHz) ? LowestHz : 0.0;

    /// <summary>Infinity when absent, INCLUDING the default zero, which means "not narrowed".</summary>
    public double HighEdgeHz =>
        HighestHz > 0.0 && double.IsFinite(HighestHz)
            ? HighestHz
            : double.PositiveInfinity;

    public bool Contains(double frequencyHz) =>
        frequencyHz >= LowEdgeHz && frequencyHz <= HighEdgeHz;

    /// <summary>Breaks a curve wherever no band covers it, including holes between non-overlapping bands.</summary>
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

    /// <summary>Null filter or absent sweep band means unknown, so nothing is masked. Low edge is the FULL-amplitude band.</summary>
    public static MeasuredBand Resolve(
        ProtectiveHighPassConfiguration? measurementFilter,
        double measuredLowHz,
        double measuredHighHz,
        int sampleRate)
    {
        double lowest = ProtectiveHighPassConfiguration.LowestMeasuredFrequencyHz(
            measurementFilter, sampleRate);
        double highest = double.PositiveInfinity;
        if (measuredLowHz > 0 && measuredHighHz > measuredLowHz &&
            double.IsFinite(measuredLowHz) && double.IsFinite(measuredHighHz))
        {
            lowest = Math.Max(lowest, measuredLowHz);
            highest = measuredHighHz;
        }

        return new MeasuredBand(lowest, highest);
    }
}
