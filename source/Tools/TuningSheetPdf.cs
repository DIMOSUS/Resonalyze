using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Color = MigraDoc.DocumentObjectModel.Color;

namespace Resonalyze;

// Renders an EqualizationCurve as a phone-friendly "tuning sheet" PDF (MigraDoc /
// PDFsharp): the product banner, a big title, the date and fit range, a small EQ
// preview graph, the tuning statistics, the preamp and one card per PEQ band.
// The shared layout (scaffold, images, filter cards) lives in PdfSheet.
internal static class TuningSheetPdf
{
    private static readonly Color GoodColor = Color.FromRgb(60, 150, 70);
    private static readonly Color WarnColor = Color.FromRgb(200, 150, 0);
    private static readonly Color BadColor = Color.FromRgb(200, 50, 40);
    private static readonly Color InfoColor = Color.FromRgb(20, 110, 180);

    public static void Export(
        string filePath,
        string title,
        EqualizationCurve curve,
        double fitMinHz,
        double fitMaxHz,
        EqTuneStats? stats)
    {
        ArgumentNullException.ThrowIfNull(curve);

        using var sheet = new PdfSheet(
            string.IsNullOrWhiteSpace(title) ? "Tuning sheet" : title,
            $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}" +
            $"   ·   Fit range {Number(fitMinHz, "0")}–{Number(fitMaxHz, "0")} Hz");
        Section section = sheet.Section;

        sheet.AddImage(RenderEqGraph(curve, fitMinHz, fitMaxHz), Unit.FromCentimeter(17));

        if (stats != null)
        {
            AddStatsTable(section, stats);
        }

        Paragraph preamp = section.AddParagraph();
        preamp.Format.SpaceBefore = Unit.FromMillimeter(5);
        preamp.Format.Font.Size = 15;
        preamp.AddFormattedText("Preamp   ", TextFormat.NotBold);
        preamp.AddFormattedText($"{Signed(curve.PreampDb)} dB", TextFormat.Bold);

        sheet.AddFilterCards(curve.Bands);

        sheet.Save(filePath);
    }

    private static void AddStatsTable(Section section, EqTuneStats stats)
    {
        Paragraph heading = section.AddParagraph("Tuning results");
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 13;
        heading.Format.SpaceBefore = Unit.FromMillimeter(2);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = PdfSheet.CardBorderColor;
        Column labelColumn = table.AddColumn(Unit.FromCentimeter(4.5));
        labelColumn.LeftPadding = Unit.FromMillimeter(2);
        Column valueColumn = table.AddColumn(Unit.FromCentimeter(3.5));
        valueColumn.RightPadding = Unit.FromMillimeter(2);

        AddStatRow(table, "RMS error", $"{Number(stats.RmsErrorDb, "0.0")} dB", QualityColor(stats.RmsErrorDb, 3, 6));
        AddStatRow(table, "Max error", $"{Number(stats.MaxErrorDb, "0.0")} dB", QualityColor(stats.MaxErrorDb, 6, 12));
        AddStatRow(table, "Filters used", stats.FiltersUsed.ToString(CultureInfo.InvariantCulture), Colors.Black);
        AddStatRow(table, "Peak boost", $"{Signed(stats.PeakBoostDb)} dB", stats.PeakBoostDb > 0.05 ? BadColor : GoodColor);
        AddStatRow(table, "Peak cut", $"{Signed(stats.PeakCutDb)} dB", InfoColor);
        AddStatRow(table, "Headroom", $"{Signed(stats.HeadroomDb)} dB", stats.HeadroomDb < -0.05 ? BadColor : GoodColor);
    }

    private static void AddStatRow(Table table, string label, string value, Color valueColor)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;

        Paragraph valueParagraph = row.Cells[1].AddParagraph(value);
        valueParagraph.Format.Alignment = ParagraphAlignment.Right;
        valueParagraph.Format.Font.Bold = true;
        valueParagraph.Format.Font.Color = valueColor;
    }

    // A compact white EQ graph (combined bands + preamp) with the fit range shaded.
    private static byte[] RenderEqGraph(EqualizationCurve curve, double fitMinHz, double fitMaxHz)
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 200);

        double minDb = 0;
        double maxDb = 0;
        var series = new LineSeries { Color = OxyColor.FromRgb(0x1F, 0x77, 0xB4), StrokeThickness = 2 };
        foreach (double frequency in grid)
        {
            double gain = curve.MagnitudeDbAt(frequency);
            series.Points.Add(new DataPoint(frequency, gain));
            minDb = Math.Min(minDb, gain);
            maxDb = Math.Max(maxDb, gain);
        }

        var model = new PlotModel
        {
            Background = OxyColors.White,
            PlotAreaBorderColor = OxyColors.Gray,
            TextColor = OxyColors.Black
        };

        if (fitMaxHz > fitMinHz)
        {
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = fitMinHz,
                MaximumX = fitMaxHz,
                Fill = OxyColor.FromArgb(28, 90, 210, 120),
                StrokeThickness = 0,
                Layer = AnnotationLayer.BelowSeries
            });
        }

        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = 20_000,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            TextColor = OxyColors.Black,
            TicklineColor = OxyColors.Gray,
            Unit = "Hz"
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = Math.Min(-6, Math.Floor(minDb) - 2),
            Maximum = Math.Max(6, Math.Ceiling(maxDb) + 2),
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            TextColor = OxyColors.Black,
            TicklineColor = OxyColors.Gray,
            Unit = "dB"
        });
        model.Series.Add(series);

        var exporter = new PngExporter { Width = 900, Height = 280 };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
        return stream.ToArray();
    }

    private static Color QualityColor(double errorDb, double goodBelow, double badAbove)
    {
        if (errorDb <= goodBelow)
        {
            return GoodColor;
        }

        return errorDb >= badAbove ? BadColor : WarnColor;
    }

    private static string Signed(double value) =>
        SheetFormat.Signed(value);

    private static string Number(double value, string format) =>
        SheetFormat.Number(value, format);
}
