using OxyPlot;
using OxyPlot.Series;

namespace Resonalyze;

internal sealed class PlotLabelsPanelController
{
    private readonly Panel panel;
    private readonly OxyPlot.WindowsForms.PlotView plotView;
    private readonly Func<Mode> getCurrentMode;
    private Size lastPanelSize;
    private string lastSignature = string.Empty;

    public PlotLabelsPanelController(
        Panel panel,
        OxyPlot.WindowsForms.PlotView plotView,
        Func<Mode> getCurrentMode)
    {
        this.panel = panel;
        this.plotView = plotView;
        this.getCurrentMode = getCurrentMode;
        panel.Resize += (_, _) => Refresh(force: true);
    }

    public void Refresh()
    {
        Refresh(force: false);
    }

    public void Refresh(bool force)
    {
        bool visible = OverlayCollection.SupportsMode(getCurrentMode());
        if (!visible)
        {
            panel.Visible = false;
            ClearCache();
            return;
        }

        panel.Visible = true;
        List<LineSeries> series = plotView.Model?.Series
            .OfType<LineSeries>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Take(16)
            .ToList() ?? new List<LineSeries>();

        string signature = BuildSignature(series);
        bool sizeChanged = lastPanelSize != panel.ClientSize;
        if (!force &&
            !sizeChanged &&
            lastSignature == signature)
        {
            return;
        }

        panel.SuspendLayout();
        panel.Controls.Clear();

        const int columnCount = 4;
        const int rowCount = 4;
        int cellWidth = Math.Max(1, panel.ClientSize.Width / columnCount);
        int cellHeight = Math.Max(1, panel.ClientSize.Height / rowCount);

        for (int index = 0; index < series.Count; index++)
        {
            int row = index / columnCount;
            int column = index % columnCount;
            Rectangle cellBounds = new(
                column * cellWidth,
                row * cellHeight,
                column == columnCount - 1
                    ? panel.ClientSize.Width - (column * cellWidth)
                    : cellWidth,
                row == rowCount - 1
                    ? panel.ClientSize.Height - (row * cellHeight)
                    : cellHeight);
            AddEntry(cellBounds, series[index]);
        }

        panel.ResumeLayout();
        lastSignature = signature;
        lastPanelSize = panel.ClientSize;
    }

    private void AddEntry(Rectangle cellBounds, LineSeries series)
    {
        const int lineWidth = 20;
        const int lineHeight = 3;
        int lineLeft = cellBounds.Left + 6;
        int lineTop = cellBounds.Top + Math.Max(0, (cellBounds.Height - lineHeight) / 2);
        var line = new Panel
        {
            BackColor = ToWinFormsColor(series.Color),
            Location = new Point(lineLeft, lineTop),
            Name = $"plotLabelLine{panel.Controls.Count}",
            Size = new Size(lineWidth, lineHeight)
        };

        var title = new Label
        {
            AutoEllipsis = true,
            Font = new Font(panel.Font.FontFamily, 9f, FontStyle.Regular),
            ForeColor = Color.White,
            Location = new Point(lineLeft + lineWidth + 8, cellBounds.Top),
            Name = $"plotLabelText{panel.Controls.Count}",
            Size = new Size(
                Math.Max(1, cellBounds.Width - (lineWidth + 18)),
                cellBounds.Height),
            Text = series.Title ?? string.Empty,
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(line);
        panel.Controls.Add(title);
    }

    private void ClearCache()
    {
        panel.Controls.Clear();
        lastSignature = string.Empty;
        lastPanelSize = Size.Empty;
    }

    private static string BuildSignature(IEnumerable<LineSeries> series)
    {
        return string.Join(
            "|",
            series.Select(item =>
                $"{item.Title}:{item.Color.A},{item.Color.R},{item.Color.G},{item.Color.B}"));
    }

    private static Color ToWinFormsColor(OxyColor color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}
