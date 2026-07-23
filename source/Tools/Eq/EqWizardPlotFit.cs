using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The dB axis bounds a source curve needs: the view, and how far it may pan.</summary>
internal readonly record struct EqWizardAxisRange(
    double Minimum,
    double Maximum,
    double AbsoluteMinimum,
    double AbsoluteMaximum);

/// <summary>
/// Fits the wizard plot to whatever the source curve actually is. An impulse response is
/// loopback-referenced dB around zero and gets fixed bounds; an imported curve can sit
/// anywhere — a dB SPL room average lives near 80 dB — and would otherwise be outside the
/// axis's ABSOLUTE limits, where no amount of panning brings it back.
/// </summary>
internal static class EqWizardPlotFit
{
    /// <summary>Bounds used for an impulse-response source (relative dB around zero).</summary>
    public static readonly EqWizardAxisRange ImpulseResponseRange = new(-80, 10, -90, 20);

    // Rounding the view to whole tens keeps the gridlines on the familiar 10 dB step,
    // and the margin leaves room for the target and the corrected curve around the data.
    private const double Step = 10;
    private const double ViewMarginDb = 10;
    private const double PanMarginDb = 40;

    /// <summary>
    /// The axis for a curve source. Falls back to the impulse-response bounds when the
    /// curve has no finite level at all (every band unmeasured).
    /// </summary>
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
            // A dead-flat curve would collapse the axis onto a single gridline.
            maximum = minimum + Step;
        }

        return new EqWizardAxisRange(
            minimum,
            maximum,
            minimum - PanMarginDb,
            maximum + PanMarginDb);
    }

    /// <summary>
    /// The target offset that lands the target curve on the source: the gap between their
    /// mean levels inside the tuning window, in whole dB. Without it an absolute (SPL)
    /// source and a relative target start tens of dB apart and the first Auto Tune tries
    /// to close a gap that is only a datum difference.
    /// </summary>
    public static double SuggestTargetOffsetDb(
        IEnumerable<SignalPoint> sourcePoints,
        Func<double, double> evaluateTarget,
        double minHz,
        double maxHz)
    {
        ArgumentNullException.ThrowIfNull(sourcePoints);
        ArgumentNullException.ThrowIfNull(evaluateTarget);

        double sourceSum = 0;
        double targetSum = 0;
        int count = 0;
        foreach (SignalPoint point in sourcePoints)
        {
            if (point.X < minHz || point.X > maxHz || !double.IsFinite(point.Y))
            {
                continue;
            }

            double target = evaluateTarget(point.X);
            if (!double.IsFinite(target))
            {
                continue;
            }

            sourceSum += point.Y;
            targetSum += target;
            count++;
        }

        return count == 0 ? 0 : Math.Round((sourceSum - targetSum) / count);
    }
}
