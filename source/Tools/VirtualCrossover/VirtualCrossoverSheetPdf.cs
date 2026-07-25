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
// channel PAIR with the values to dial into the DSP plus the PEQ band cards —
// a stereo pair prints its L and R values side by side in one table, a mono
// pair (or a pair with one loaded side) prints the single-channel layout.
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
        using PdfSheet sheet = Build(project, metricLine, sampleRate);
        sheet.Save(filePath);
    }

    // Builds the sheet without rendering it, so a test can walk the MigraDoc
    // document model (section tables, rows, cells) and assert the layout — a
    // stereo pair as one L/R table, a mono/one-sided pair as a single column —
    // without parsing a rendered PDF. The caller owns disposal.
    internal static PdfSheet Build(
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

        var sheet = new PdfSheet("Virtual DSP", subtitleText);

        // Both sides of every pair print in one sheet; a mono pair prints
        // once. On the graph the right side reuses the pair's hue dashed.
        var participating =
            new List<(int Index, string SideSuffix, bool Dashed,
                VirtualCrossoverChannelSettings Channel)>();
        for (int i = 0; i < project.Pairs.Count; i++)
        {
            foreach ((VirtualCrossoverChannelSettings channel, string sideSuffix)
                in VirtualCrossoverSheet.SideSections(project.Pairs[i]))
            {
                if (channel.HasSource)
                {
                    participating.Add(
                        (i, sideSuffix,
                         sideSuffix == VirtualCrossoverSheet.RightSuffix, channel));
                }
            }
        }

        if (participating.Count > 0)
        {
            sheet.AddImage(
                RenderChainsGraph(participating, sampleRate),
                Unit.FromCentimeter(17));
        }

        // A stereo pair with both sides loaded prints as ONE section with an
        // L/R value table — the two sides of a pair are dialed in together,
        // so their numbers belong side by side. A mono pair (or a pair with
        // one loaded side) keeps the single-channel layout.
        for (int i = 0; i < project.Pairs.Count; i++)
        {
            VirtualCrossoverChannelPairSettings pair = project.Pairs[i];
            if (!pair.Mono && pair.Left.HasSource && pair.Right.HasSource)
            {
                AddPairSection(sheet, i, pair.Left, pair.Right);
                continue;
            }

            foreach ((VirtualCrossoverChannelSettings channel, string sideSuffix)
                in VirtualCrossoverSheet.SideSections(pair))
            {
                if (channel.HasSource)
                {
                    AddChannelSection(sheet, i, sideSuffix, channel);
                }
            }
        }

        return sheet;
    }

    private static void AddPairSection(
        PdfSheet sheet,
        int index,
        VirtualCrossoverChannelSettings left,
        VirtualCrossoverChannelSettings right)
    {
        Section section = sheet.Section;
        AddSectionHeading(
            section, $"Channel {VirtualCrossoverSheet.ChannelName(index)}");

        Table table = AddValueTable(section);
        for (int side = 0; side < 2; side++)
        {
            Column sideColumn = table.AddColumn(Unit.FromCentimeter(5.5));
            sideColumn.LeftPadding = Unit.FromMillimeter(2);
        }

        Row header = table.AddRow();
        header.Cells[1].AddParagraph("Left").Format.Font.Bold = true;
        header.Cells[2].AddParagraph("Right").Format.Font.Bold = true;

        AddPairRow(table, "Source", left.DisplayName, right.DisplayName);
        AddPairRow(table, "Gain",
            $"{Signed(left.GainDb)} dB",
            $"{Signed(right.GainDb)} dB");
        AddPairRow(table, "Delay", DelayText(left), DelayText(right));
        AddPairRow(table, "Polarity", PolarityText(left), PolarityText(right));
        AddPairRow(table, "Crossover",
            VirtualCrossoverSheet.DescribeCrossover(left),
            VirtualCrossoverSheet.DescribeCrossover(right));
        if (HasAllPass(left) || HasAllPass(right))
        {
            AddPairRow(table, "All-pass", AllPassText(left), AllPassText(right));
        }
        if (HasPeq(left) || HasPeq(right))
        {
            AddPairRow(table, "PEQ", PeqSummary(left), PeqSummary(right));
        }

        KeepTogether(table);

        string channelName = VirtualCrossoverSheet.ChannelName(index);
        AddPeqCards(sheet, $"Channel {channelName} Left — PEQ", left);
        AddPeqCards(sheet, $"Channel {channelName} Right — PEQ", right);
    }

    // The value strings shared by the pair table and the single-channel
    // section, so the two layouts cannot print the same field differently.
    private static string DelayText(VirtualCrossoverChannelSettings channel) =>
        $"{Number(channel.DelayMs, "0.00")} ms " +
        $"(= {Number(channel.DelayMs * Acoustics.SpeedOfSoundAt20CMetersPerSecond, "0.#")} mm in air)";

    private static string PolarityText(VirtualCrossoverChannelSettings channel) =>
        channel.InvertPolarity ? "Inverted" : "Normal";

    private static bool HasAllPass(VirtualCrossoverChannelSettings channel) =>
        channel.AllPassType != AllPassType.Off;

    private static string AllPassText(VirtualCrossoverChannelSettings channel) =>
        HasAllPass(channel)
            ? VirtualCrossoverSheet.DescribeAllPass(channel)
            : "—";

    private static bool HasPeq(VirtualCrossoverChannelSettings channel) =>
        channel.PeqBands.Count > 0 || channel.PeqPreampDb != 0;

    // Names the profile, how many filters it holds and its preamp — the three things
    // needed to check a channel against the DSP being typed into, without counting cards.
    private static string PeqSummary(VirtualCrossoverChannelSettings channel)
    {
        if (!HasPeq(channel))
        {
            return "—";
        }

        string filters = channel.PeqBands.Count == 1
            ? "1 filter"
            : $"{channel.PeqBands.Count} filters";
        return $"{channel.PeqSourceName ?? "custom"} · {filters} · " +
            $"preamp {Signed(channel.PeqPreampDb)} dB";
    }

    // The pair table names each side's PEQ in one summary row; the band cards
    // print below it per side, captioned, so the two sides cannot be mixed up.
    private static void AddPeqCards(
        PdfSheet sheet,
        string caption,
        VirtualCrossoverChannelSettings channel)
    {
        if (channel.PeqBands.Count == 0)
        {
            return;
        }

        // The caption goes INSIDE the card table as its heading row, so it repeats when a
        // long bank breaks across pages; a paragraph above the table would name only the
        // first page and leave the rest looking like the previous channel's filters.
        sheet.AddFilterCards(channel.PeqBands, caption);
    }

    // Binds a value table into one block so a page break cannot strand the crossover and
    // PEQ rows on the next page, away from the channel heading that names them.
    private static void KeepTogether(Table table)
    {
        if (table.Rows.Count > 1)
        {
            table.Rows[0].KeepWith = table.Rows.Count - 1;
        }
    }

    private static void AddPairRow(
        Table table,
        string label,
        string leftValue,
        string rightValue)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;
        row.Cells[1].AddParagraph(leftValue).Format.Font.Bold = true;
        row.Cells[2].AddParagraph(rightValue).Format.Font.Bold = true;
    }

    private static void AddChannelSection(
        PdfSheet sheet,
        int index,
        string sideSuffix,
        VirtualCrossoverChannelSettings channel)
    {
        Section section = sheet.Section;
        AddSectionHeading(
            section,
            $"Channel {VirtualCrossoverSheet.ChannelName(index)}{sideSuffix} — " +
            channel.DisplayName);

        Table table = AddValueTable(section);
        Column valueColumn = table.AddColumn(Unit.FromCentimeter(11.0));
        valueColumn.LeftPadding = Unit.FromMillimeter(2);

        AddRow(table, "Gain", $"{Signed(channel.GainDb)} dB");
        AddRow(table, "Delay", DelayText(channel));
        AddRow(table, "Polarity", PolarityText(channel));
        AddRow(table, "Crossover", VirtualCrossoverSheet.DescribeCrossover(channel));
        if (HasAllPass(channel))
        {
            AddRow(table, "All-pass", AllPassText(channel));
        }
        if (HasPeq(channel))
        {
            AddRow(table, "PEQ", PeqSummary(channel));
        }

        KeepTogether(table);
        // Captioned like the pair layout, so a page of cards always says whose they are.
        AddPeqCards(
            sheet,
            $"Channel {VirtualCrossoverSheet.ChannelName(index)}{sideSuffix} — PEQ",
            channel);
    }

    // The section heading and the label-column value table shared by the pair
    // and single-channel layouts; the caller adds the value column(s) it needs.
    private static void AddSectionHeading(Section section, string title)
    {
        Paragraph heading = section.AddParagraph(title);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
        // Never break between a channel heading and the values it introduces.
        heading.Format.KeepWithNext = true;
    }

    private static Table AddValueTable(Section section)
    {
        Table table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = PdfSheet.CardBorderColor;
        Column labelColumn = table.AddColumn(Unit.FromCentimeter(3.0));
        labelColumn.LeftPadding = Unit.FromMillimeter(2);
        return table;
    }

    private static void AddRow(Table table, string label, string value)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;

        Paragraph valueParagraph = row.Cells[1].AddParagraph(value);
        valueParagraph.Format.Font.Bold = true;
    }

    // A compact white graph of every participating channel side's DSP chain
    // magnitude (gain + crossover + PEQ; the delay has no magnitude effect).
    // Left and right sides of one pair share a hue; the right side is dashed.
    private static byte[] RenderChainsGraph(
        IReadOnlyList<(int Index, string SideSuffix, bool Dashed,
            VirtualCrossoverChannelSettings Channel)> channels,
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
        foreach ((int index, string sideSuffix, bool dashed,
            VirtualCrossoverChannelSettings channel) in channels)
        {
            DspChannelChain chain = channel.ToChain() with { DelayMs = 0 };
            var series = new LineSeries
            {
                Color = ChainColors[index % ChainColors.Length],
                StrokeThickness = 2,
                LineStyle = dashed ? LineStyle.Dash : LineStyle.Solid,
                Title = $"Channel {VirtualCrossoverSheet.ChannelName(index)}{sideSuffix}"
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
