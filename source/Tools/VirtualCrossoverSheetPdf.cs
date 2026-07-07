using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

// Renders the Virtual DSP settings as a phone-friendly "tuning sheet" PDF
// (MigraDoc / PDFsharp, same style as TuningSheetPdf): the product banner, the
// title, a combined graph of every channel's DSP chain, and one section per
// channel with the values to dial into the DSP plus its PEQ band cards.
// The shared layout (scaffold, images, filter cards) lives in PdfSheet.
internal static class VirtualCrossoverSheetPdf
{
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

        string subtitleText =
            $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(metricLine))
        {
            subtitleText += $"   ·   {metricLine}";
        }

        using var sheet = new PdfSheet("Virtual DSP", subtitleText);

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
            sheet.AddImage(
                RenderChainsGraph(participating, sampleRate),
                Unit.FromCentimeter(17));
        }

        foreach ((int index, VirtualCrossoverChannelSettings channel) in participating)
        {
            AddChannelSection(sheet, index, channel);
        }

        sheet.Save(filePath);
    }

    private static void AddChannelSection(
        PdfSheet sheet,
        int index,
        VirtualCrossoverChannelSettings channel)
    {
        Section section = sheet.Section;
        Paragraph heading = section.AddParagraph(
            $"Channel {VirtualCrossoverSheet.ChannelName(index)} — {channel.DisplayName}");
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);

        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = PdfSheet.CardBorderColor;
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

        sheet.AddFilterCards(channel.PeqBands);
    }

    private static void AddRow(Table table, string label, string value)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;

        Paragraph valueParagraph = row.Cells[1].AddParagraph(value);
        valueParagraph.Format.Font.Bold = true;
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
