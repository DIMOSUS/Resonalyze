using Resonalyze.Dsp;

namespace Resonalyze;

internal readonly record struct EqWizardAxisRange(
    double Minimum,
    double Maximum,
    double AbsoluteMinimum,
    double AbsoluteMaximum);

/// <summary>An imported dB SPL curve (~80 dB) would sit outside the IR axis's ABSOLUTE limits, beyond panning.</summary>
internal static class EqWizardPlotFit
{
    /// <summary>Pan ceiling shared with the FR and Live Spectrum plots (same loopback-referenced quantity).</summary>
    public static readonly EqWizardAxisRange ImpulseResponseRange =
        new(-80, 10, -90, PlotModelStyle.RelativeDecibelAbsoluteMaximum);

    private const double Step = 10;
    private const double ViewMarginDb = 10;
    private const double PanMarginDb = 40;

    /// <summary>Falls back to the IR bounds when no level is finite.</summary>
    public static EqWizardAxisRange ForCurve(IEnumerable<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        double dataMin = double.PositiveInfinity;
        double dataMax = double.NegativeInfinity;
        foreach (SignalPoint point in points)
        {
            if (!double.IsFinite(point.Y))
            {
                continue;
            }

            dataMin = Math.Min(dataMin, point.Y);
            dataMax = Math.Max(dataMax, point.Y);
        }

        if (!double.IsFinite(dataMin) || !double.IsFinite(dataMax))
        {
            return ImpulseResponseRange;
        }

        double minimum = Math.Floor((dataMin - ViewMarginDb) / Step) * Step;
        double maximum = Math.Ceiling((dataMax + ViewMarginDb) / Step) * Step;
        if (maximum - minimum < Step)
        {
            maximum = minimum + Step;
        }

        return new EqWizardAxisRange(
            minimum,
            maximum,
            minimum - PanMarginDb,
            maximum + PanMarginDb);
    }

    private const double EqGainAxisStepDb = 6;

    /// <summary>Budget range extended by the summed curve's extent, snapped to 6 dB with one step margin; 0/0 = no curve.</summary>
    public static (double Minimum, double Maximum) EqGainAxisRange(
        double budgetMinDb,
        double budgetMaxDb,
        double curveMinDb,
        double curveMaxDb)
    {
        double low = Math.Min(budgetMinDb, curveMinDb);
        double high = Math.Max(budgetMaxDb, curveMaxDb);
        return (
            Math.Floor(low / EqGainAxisStepDb) * EqGainAxisStepDb - EqGainAxisStepDb,
            Math.Ceiling(high / EqGainAxisStepDb) * EqGainAxisStepDb + EqGainAxisStepDb);
    }
}
