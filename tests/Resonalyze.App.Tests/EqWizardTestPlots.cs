using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Draws a session the way the panel does, without the panel.</summary>
internal static class EqWizardTestPlots
{
    public static PlotModel Draw(
        EqWizardSession session,
        int? selectedBand = null,
        EqWizardPhaseCurves? phase = null)
    {
        var plot = new EqWizardPlot();
        EqualizationCurve eq = EqWizardRender.DisplayedEq(session);
        plot.Draw(session, eq, EqWizardRender.RenderSet(session, eq), selectedBand, phase);
        return plot.Model;
    }

    public static IReadOnlyList<string> CurveTitles(PlotModel model) =>
        model.Series
            .OfType<XYAxisSeries>()
            .Select(series => series.Title ?? string.Empty)
            .ToList();
}
