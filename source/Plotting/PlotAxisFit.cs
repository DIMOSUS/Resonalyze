using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

/// <summary>
/// "Fit to data" for a plot's axes — REW's Ctrl+Alt+F and Ctrl+Alt+Y, and the
/// buttons of the same name in the graph limits dialog.
/// </summary>
internal static class PlotAxisFit
{
    /// <summary>
    /// Headroom left around the data on a value axis, as a fraction of its span. The
    /// frequency axis gets none: 20 Hz to 20 kHz IS the data, and padding it would
    /// open every fit on empty decades.
    /// </summary>
    private const double ValueAxisMarginFraction = 0.05;

    /// <summary>
    /// Fits the axes to the data drawn against them. Axes that refuse zoom are left
    /// alone — those are the ones a mode deliberately pins (the waterfall's hidden
    /// axes, the EQ wizard's fixed gain axis).
    /// </summary>
    /// <param name="verticalOnly">
    /// True for REW's "Fit Y to data": the frequency (or time) span stays where the
    /// user put it and only the value axis is refitted.
    /// </param>
    public static bool FitToData(PlotModel? model, bool verticalOnly)
    {
        if (model == null)
        {
            return false;
        }

        // The data ranges are maintained by the model, not by the axes' view state,
        // so they survive zooming; refresh them anyway in case a series changed
        // since the last full update.
        ((IPlotModel)model).Update(true);

        bool fitted = false;
        foreach (Axis axis in model.Axes)
        {
            if (!axis.IsZoomEnabled ||
                (verticalOnly && axis.IsHorizontal()) ||
                !TryGetDataRange(axis, out double minimum, out double maximum))
            {
                continue;
            }

            axis.Zoom(minimum, maximum);
            fitted = true;
        }

        return fitted;
    }

    private static bool TryGetDataRange(Axis axis, out double minimum, out double maximum)
    {
        minimum = axis.DataMinimum;
        maximum = axis.DataMaximum;
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
        {
            return false;
        }

        if (axis.IsHorizontal())
        {
            return true;
        }

        // Margin in the axis's own scale: a decade is padded by a ratio, a linear
        // axis by a fraction of its span.
        if (axis is LogarithmicAxis && minimum > 0)
        {
            double factor = Math.Pow(maximum / minimum, ValueAxisMarginFraction);
            minimum /= factor;
            maximum *= factor;
        }
        else
        {
            double margin = (maximum - minimum) * ValueAxisMarginFraction;
            minimum -= margin;
            maximum += margin;
        }

        return double.IsFinite(minimum) && double.IsFinite(maximum) && maximum > minimum;
    }
}
