using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
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
internal static class TuningSheetPdf
{
    private const int FilterCardColumns = 4;

    private static readonly Color CaptionColor = Color.FromRgb(90, 90, 90);
    private static readonly Color GoodColor = Color.FromRgb(60, 150, 70);
    private static readonly Color WarnColor = Color.FromRgb(200, 150, 0);
    private static readonly Color BadColor = Color.FromRgb(200, 50, 40);
    private static readonly Color InfoColor = Color.FromRgb(20, 110, 180);
    private static readonly Color CardBorderColor = Color.FromRgb(210, 210, 210);

    public static void Export(
        string filePath,
        string title,
        EqualizationCurve curve,
        double fitMinHz,
        double fitMaxHz,
        EqTuneStats? stats)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var tempImages = new List<string>();
        try
        {
            var document = new Document();
            Style normalStyle = document.Styles["Normal"]!;
            normalStyle.Font.Name = "Segoe UI";
            normalStyle.Font.Size = 11;

            Section section = document.AddSection();
            PageSetup pageSetup = section.PageSetup!;
            pageSetup.PageFormat = PageFormat.A4;
            pageSetup.TopMargin = Unit.FromCentimeter(1.2);
            pageSetup.BottomMargin = Unit.FromCentimeter(1.2);
            pageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            pageSetup.RightMargin = Unit.FromCentimeter(1.5);

            byte[]? banner = LoadBanner();
            if (banner != null)
            {
                AddImage(section, banner, Unit.FromCentimeter(11), tempImages);
            }

            Paragraph titleParagraph = section.AddParagraph(
                string.IsNullOrWhiteSpace(title) ? "Tuning sheet" : title);
            titleParagraph.Format.Alignment = ParagraphAlignment.Center;
            titleParagraph.Format.Font.Size = 24;
            titleParagraph.Format.Font.Bold = true;
            titleParagraph.Format.SpaceBefore = Unit.FromMillimeter(3);

            Paragraph subtitle = section.AddParagraph(
                $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}" +
                $"   ·   Fit range {Number(fitMinHz, "0")}–{Number(fitMaxHz, "0")} Hz");
            subtitle.Format.Alignment = ParagraphAlignment.Center;
            subtitle.Format.Font.Size = 9;
            subtitle.Format.Font.Color = Colors.Gray;
            subtitle.Format.SpaceAfter = Unit.FromMillimeter(4);

            AddImage(section, RenderEqGraph(curve, fitMinHz, fitMaxHz), Unit.FromCentimeter(17), tempImages);

            if (stats != null)
            {
                AddStatsTable(section, stats);
            }

            Paragraph preamp = section.AddParagraph();
            preamp.Format.SpaceBefore = Unit.FromMillimeter(5);
            preamp.Format.Font.Size = 15;
            preamp.AddFormattedText("Preamp   ", TextFormat.NotBold);
            preamp.AddFormattedText($"{Signed(curve.PreampDb)} dB", TextFormat.Bold);

            AddFilterCards(section, curve);

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            renderer.PdfDocument.Save(filePath);
        }
        finally
        {
            foreach (string temp in tempImages)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception)
                {
                    // Best-effort cleanup; a leftover temp image must not fail
                    // (or mask the failure of) an export.
                }
            }
        }
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
        table.Borders.Color = CardBorderColor;
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
        caption.Format.Font.Color = CaptionColor;

        Paragraph valueParagraph = row.Cells[1].AddParagraph(value);
        valueParagraph.Format.Alignment = ParagraphAlignment.Right;
        valueParagraph.Format.Font.Bold = true;
        valueParagraph.Format.Font.Color = valueColor;
    }

    private static void AddFilterCards(Section section, EqualizationCurve curve)
    {
        if (curve.Bands.Count == 0)
        {
            return;
        }

        var table = section.AddTable();
        for (int c = 0; c < FilterCardColumns; c++)
        {
            Column cardColumn = table.AddColumn(Unit.FromCentimeter(4.3));
            cardColumn.LeftPadding = Unit.FromMillimeter(2.5);
            cardColumn.RightPadding = Unit.FromMillimeter(2.5);
        }

        Row? row = null;
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            int column = i % FilterCardColumns;
            if (column == 0)
            {
                row = table.AddRow();
                row.TopPadding = Unit.FromMillimeter(2);
                row.BottomPadding = Unit.FromMillimeter(2);
            }

            Cell cell = row!.Cells[column];
            cell.Borders.Width = 0.5;
            cell.Borders.Color = CardBorderColor;

            PeqBand band = curve.Bands[i];
            Paragraph name = cell.AddParagraph($"Filter {i + 1}");
            name.Format.Font.Bold = true;
            name.Format.Font.Size = 13;

            Paragraph type = cell.AddParagraph("PK");
            type.Format.Font.Color = CaptionColor;
            type.Format.Font.Size = 9;

            cell.AddParagraph($"{Number(band.FrequencyHz, "0")} Hz").Format.Font.Size = 12;
            Paragraph gain = cell.AddParagraph($"{Signed(band.GainDb)} dB");
            gain.Format.Font.Bold = true;
            gain.Format.Font.Size = 12;
            cell.AddParagraph($"Q {Number(band.Q, "0.0")}").Format.Font.Size = 12;
        }
    }

    private static void AddImage(Section section, byte[] pngBytes, Unit width, List<string> tempImages)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"resonalyze_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, pngBytes);
        tempImages.Add(tempPath);

        Paragraph paragraph = section.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        var image = paragraph.AddImage(tempPath);
        image.Width = width;
        image.LockAspectRatio = true;
    }

    private static byte[]? LoadBanner()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Resonalyze.banner.jpg");
        if (stream == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
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
        value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private static string Number(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
