using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze;

internal static class PlotAxisFit
{
    /// <summary>Vertical axes only; horizontal (frequency/time) axes get no margin: 20 Hz-20 kHz is the data.</summary>
    private const double ValueAxisMarginFraction = 0.05;

    /// <summary>Axes that refuse zoom (pinned by a mode) are left alone.</summary>
    public static bool FitToData(PlotModel? model, bool verticalOnly)
    {
        if (model == null)
        {
            return false;
        }

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
