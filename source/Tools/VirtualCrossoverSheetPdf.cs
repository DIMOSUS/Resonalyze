using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Color = MigraDoc.DocumentObjectModel.Color;

namespace Resonalyze;

// Renders the Virtual DSP settings as a phone-friendly "tuning sheet" PDF
// (MigraDoc / PDFsharp, same style as TuningSheetPdf): the product banner, the
// title, a combined graph of every channel's DSP chain, and one section per
// channel with the values to dial into the DSP plus its PEQ band cards.
internal static class VirtualCrossoverSheetPdf
{
    private const int FilterCardColumns = 4;

    private static readonly Color CaptionColor = Color.FromRgb(90, 90, 90);
    private static readonly Color CardBorderColor = Color.FromRgb(210, 210, 210);

    // Print-friendly (white background) variants of the on-screen channel
    // palette, hue for hue, one per possible channel so colours never repeat.
    private static readonly OxyColor[] ChainColors =
    [
        OxyColor.FromRgb(0x1F, 0x77, 0xB4),   // A: blue
        OxyColor.FromRgb(0xE0, 0x7A, 0x28),   // B: orange
        OxyColor.FromRgb(0x2C, 0xA0, 0x50),   // C: green
        OxyColor.FromRgb(0x8A, 0x56, 0xC8),   // D: purple
        OxyColor.FromRgb(0x1F, 0x9A, 0xA8),   // E: cyan
        OxyColor.FromRgb(0xC8, 0x50, 0x6E),   // F: pink
        OxyColor.FromRgb(0x9A, 0x8A, 0x20),   // G: olive
        OxyColor.FromRgb(0x5A, 0x9A, 0x28)    // H: lime
    ];

    public static void Export(
        string filePath,
        VirtualCrossoverProjectFile project,
        string? metricLine,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(project);

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

            Paragraph titleParagraph = section.AddParagraph("Virtual DSP");
            titleParagraph.Format.Alignment = ParagraphAlignment.Center;
            titleParagraph.Format.Font.Size = 24;
            titleParagraph.Format.Font.Bold = true;
            titleParagraph.Format.SpaceBefore = Unit.FromMillimeter(3);

            string subtitleText =
                $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrWhiteSpace(metricLine))
            {
                subtitleText += $"   ·   {metricLine}";
            }

            Paragraph subtitle = section.AddParagraph(subtitleText);
            subtitle.Format.Alignment = ParagraphAlignment.Center;
            subtitle.Format.Font.Size = 9;
            subtitle.Format.Font.Color = Colors.Gray;
            subtitle.Format.SpaceAfter = Unit.FromMillimeter(4);

            var participating = new List<(int Index, VirtualCrossoverChannelSettings Channel)>();
            for (int i = 0; i < project.Channels.Count; i++)
            {
                if (project.Channels[i].HasSource)
                {
                    participating.Add((i, project.Channels[i]));
                }
            }

            if (participating.Count > 0)
            {
                AddImage(
                    section,
                    RenderChainsGraph(participating, sampleRate),
                    Unit.FromCentimeter(17),
                    tempImages);
            }

            foreach ((int index, VirtualCrossoverChannelSettings channel) in participating)
            {
                AddChannelSection(section, index, channel);
            }

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

    private static void AddChannelSection(
        Section section,
        int index,
        VirtualCrossoverChannelSettings channel)
    {
        Paragraph heading = section.AddParagraph(
            $"Channel {VirtualCrossoverSheet.ChannelName(index)} — {channel.DisplayName}");
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = CardBorderColor;
        Column labelColumn = table.AddColumn(Unit.FromCentimeter(3.0));
        labelColumn.LeftPadding = Unit.FromMillimeter(2);
        Column valueColumn = table.AddColumn(Unit.FromCentimeter(11.0));
        valueColumn.LeftPadding = Unit.FromMillimeter(2);

        AddRow(table, "Gain", $"{Signed(channel.GainDb)} dB");
        AddRow(
            table,
            "Delay",
            $"{Number(channel.DelayMs, "0.00")} ms   " +
            $"(= {Number(channel.DelayMs * Acoustics.SpeedOfSoundAt20CMetersPerSecond, "0.#")} mm in air)");
        AddRow(table, "Polarity", channel.InvertPolarity ? "Inverted" : "Normal");
        AddRow(table, "Crossover", VirtualCrossoverSheet.DescribeCrossover(channel));
        if (channel.PeqBands.Count > 0 || channel.PeqPreampDb != 0)
        {
            AddRow(
                table,
                "PEQ",
                $"{channel.PeqSourceName ?? "custom"}, preamp {Signed(channel.PeqPreampDb)} dB");
        }

        AddFilterCards(section, channel.PeqBands);
    }

    private static void AddRow(Table table, string label, string value)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = CaptionColor;

        Paragraph valueParagraph = row.Cells[1].AddParagraph(value);
        valueParagraph.Format.Font.Bold = true;
    }

    private static void AddFilterCards(Section section, IReadOnlyList<PeqBand> bands)
    {
        if (bands.Count == 0)
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
        for (int i = 0; i < bands.Count; i++)
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

            PeqBand band = bands[i];
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

    private static void AddImage(
        Section section,
        byte[] pngBytes,
        Unit width,
        List<string> tempImages)
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

    // A compact white graph of every participating channel's DSP chain magnitude
    // (gain + crossover + PEQ; the delay has no magnitude effect).
    private static byte[] RenderChainsGraph(
        IReadOnlyList<(int Index, VirtualCrossoverChannelSettings Channel)> channels,
        int sampleRate)
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 200);

        var model = new PlotModel
        {
            Background = OxyColors.White,
            PlotAreaBorderColor = OxyColors.Gray,
            TextColor = OxyColors.Black,
            IsLegendVisible = true
        };
        // OxyPlot 2.x renders no legend unless one is explicitly added; the
        // per-series channel titles were invisible in the exported sheet.
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendTextColor = OxyColors.Black,
            LegendBorder = OxyColors.Gray,
            LegendBackground = OxyColors.White
        });

        double minDb = -6;
        double maxDb = 6;
        foreach ((int index, VirtualCrossoverChannelSettings channel) in channels)
        {
            DspChannelChain chain = channel.ToChain() with { DelayMs = 0 };
            var series = new LineSeries
            {
                Color = ChainColors[index % ChainColors.Length],
                StrokeThickness = 2,
                Title = $"Channel {VirtualCrossoverSheet.ChannelName(index)}"
            };
            foreach (double frequency in grid)
            {
                double db = DataHelper.AmplitudeToDecibels(
                    chain.Response(frequency, sampleRate).Magnitude);
                series.Points.Add(new DataPoint(frequency, db));
                if (db > -70)
                {
                    minDb = Math.Min(minDb, db);
                    maxDb = Math.Max(maxDb, db);
                }
            }

            model.Series.Add(series);
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
            Minimum = Math.Max(-60, Math.Floor(minDb) - 2),
            Maximum = Math.Ceiling(maxDb) + 2,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            TextColor = OxyColors.Black,
            TicklineColor = OxyColors.Gray,
            Unit = "dB"
        });

        var exporter = new PngExporter { Width = 900, Height = 280 };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
        return stream.ToArray();
    }

    private static string Signed(double value) =>
        VirtualCrossoverSheet.Signed(value);

    private static string Number(double value, string format) =>
        VirtualCrossoverSheet.Number(value, format);
}
