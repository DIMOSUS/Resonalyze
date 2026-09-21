using OxyPlot;
using OxyPlot.Series;

namespace Resonalyze;

/// <summary>The plot series of one overlay slot, tagged so the slot finds and replaces its own.</summary>
internal static class OverlaySeries
{
    private const string TagPrefix = "overlay:";
    private const string CoherenceTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00} γ²";

    public static bool IsOverlayTag(string tag) => tag.StartsWith(TagPrefix, StringComparison.Ordinal);

    public static bool Remove(PlotModel model, Mode mode, int slot)
    {
        string prefix = $"{TagPrefix}{mode}:{slot}:";
        List<Series> existing = model.Series
            .Where(series => series.Tag is string tag &&
                tag.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (Series series in existing)
        {
            model.Series.Remove(series);
        }

        return existing.Count > 0;
    }

    public static bool AddCurve(
        PlotModel model,
        Mode mode,
        int slot,
        DataPoint[]? points,
        OverlayAppearance appearance,
        string title,
        string? yAxisKey)
    {
        if (points == null || points.Length < 2)
        {
            return false;
        }

        if (yAxisKey == PlotModelFactory.CoherenceAxisKey)
        {
            PlotModelFactory.AddCoherenceAxis(model);
        }

        var series = new LineSeries
        {
            Color = appearance.LineColor(),
            StrokeThickness = appearance.StrokeThickness,
            LineStyle = OverlayLineStyles.ToOxy(appearance.LineStyle),
            Title = title,
            Tag = Tag(mode, slot, "curve")
        };
        if (!string.IsNullOrEmpty(yAxisKey))
        {
            series.YAxisKey = yAxisKey;
        }

        string? trackerFormat = yAxisKey == PlotModelFactory.CoherenceAxisKey
            ? CoherenceTrackerFormat
            : OverlayModes.TrackerFormat(mode);
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            series.TrackerFormatString = trackerFormat;
        }
        series.Points.AddRange(points);
        model.Series.Add(series);
        return true;
    }

    public static void AddTarget(
        PlotModel model,
        Mode mode,
        int slot,
        TargetOverlayShape shape,
        DataPoint[] deviation,
        TargetDeviationMode deviationMode,
        OverlayAppearance appearance,
        string title)
    {
        Color color = appearance.Color;
        OxyColor lineColor = appearance.LineColor();
        string? trackerFormat = OverlayModes.TrackerFormat(mode);

        if (shape.ToleranceUpper.Length >= 2 &&
            shape.ToleranceLower.Length == shape.ToleranceUpper.Length)
        {
            var band = new AreaSeries
            {
                Color = OxyColors.Transparent,
                Fill = OxyColor.FromArgb(40, color.R, color.G, color.B),
                StrokeThickness = 0,
                Tag = Tag(mode, slot, "tolerance")
            };
            band.Points.AddRange(shape.ToleranceUpper);
            band.Points2.AddRange(shape.ToleranceLower);
            model.Series.Add(band);
        }

        var targetSeries = new LineSeries
        {
            Color = lineColor,
            StrokeThickness = appearance.StrokeThickness,
            LineStyle = OverlayLineStyles.ToOxy(appearance.LineStyle),
            Title = $"{title} (target)",
            Tag = Tag(mode, slot, "target")
        };
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            targetSeries.TrackerFormatString = trackerFormat;
        }
        targetSeries.Points.AddRange(shape.Target);
        model.Series.Add(targetSeries);

        if (deviation.Length < 2)
        {
            return;
        }

        string deviationLabel = deviationMode == TargetDeviationMode.Correction
            ? "EQ correction"
            : "deviation";
        var deviationSeries = new LineSeries
        {
            Color = lineColor,
            StrokeThickness = Math.Max(1.0, appearance.StrokeThickness - 1.0),
            LineStyle = LineStyle.Solid,
            Title = $"{title} ({deviationLabel})",
            Tag = Tag(mode, slot, "deviation")
        };
        if (!string.IsNullOrEmpty(trackerFormat))
        {
            deviationSeries.TrackerFormatString = trackerFormat;
        }
        deviationSeries.Points.AddRange(deviation);
        model.Series.Add(deviationSeries);
    }

    private static string Tag(Mode mode, int slot, string part) => $"{TagPrefix}{mode}:{slot}:{part}";
}
