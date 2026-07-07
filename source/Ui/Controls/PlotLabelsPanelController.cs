using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using HorizontalAlignment = OxyPlot.HorizontalAlignment;

namespace Resonalyze;

internal sealed class PlotLabelsPanelController
{
    private readonly OxyPlot.WindowsForms.PlotView plotView;
    private readonly Func<Mode> getCurrentMode;

    public PlotLabelsPanelController(
        OxyPlot.WindowsForms.PlotView plotView,
        Func<Mode> getCurrentMode)
    {
        this.plotView = plotView;
        this.getCurrentMode = getCurrentMode;
    }

    public void Refresh()
    {
        bool visible = OverlayCollection.SupportsMode(getCurrentMode());
        if (!visible)
        {
            RemovePlotLabelAnnotations();
            return;
        }

        if (plotView.Model == null)
        {
            return;
        }

        List<LineSeries> series = plotView.Model.Series
            .OfType<LineSeries>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Take(16)
            .ToList();

        RemovePlotLabelAnnotations();

        const int columnCount = 4;
        int rowCount = (series.Count + columnCount - 1) / columnCount;

        for (int index = 0; index < series.Count; index++)
        {
            int column = index % columnCount;
            int row = index / columnCount;

            AddEntry(rowCount - row - 1, 0.01 + column / (double)columnCount, series[index]);
        }
        plotView.InvalidatePlot(false);
    }

    private void AddEntry(int stringNumber, double offset, LineSeries series)
    {
        if (plotView.Model == null)
        {
            return;
        }

        plotView.Model.Annotations.Add(new OverlayTextAnnotation()
        {
            IsPlotLabelOverlay = true,
            TextFlowDirection = TextFlowDirection.BottomUp,
            Text = "\u2501\u2501 " + series.Title,
            TextPosition = new DataPoint(offset, stringNumber),
            FontSize = 12,
            FontWeight = 700,
            TextColor = series.Color,
            TextHorizontalAlignment = HorizontalAlignment.Left
        });
    }

    private void RemovePlotLabelAnnotations()
    {
        if (plotView.Model == null)
        {
            return;
        }

        for (int index = plotView.Model.Annotations.Count - 1; index >= 0; index--)
        {
            if (plotView.Model.Annotations[index] is OverlayTextAnnotation
                {
                    IsPlotLabelOverlay: true
                })
            {
                plotView.Model.Annotations.RemoveAt(index);
            }
        }
    }

}
