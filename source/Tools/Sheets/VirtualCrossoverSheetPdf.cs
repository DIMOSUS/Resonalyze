using System.Globalization;
using System.Numerics;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
// Disambiguates against System.Drawing.Color, which the WinForms implicit usings pull in.
using Color = MigraDoc.DocumentObjectModel.Color;
// One printed side of a channel pair, as the graphs consume it.
using SheetEntry = (int Index, string SideSuffix, bool Dashed,
    Resonalyze.VirtualCrossoverChannelSettings Channel);

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
        int sampleRate,
        PeqQConvention qConvention = PeqQConvention.Rbj)
    {
        using PdfSheet sheet = Build(project, metricLine, sampleRate, qConvention);
        sheet.Save(filePath);
    }

    // Builds the sheet without rendering it, so a test can walk the MigraDoc
    // document model (section tables, rows, cells) and assert the layout — a
    // stereo pair as one L/R table, a mono/one-sided pair as a single column —
    // without parsing a rendered PDF. The caller owns disposal.
    internal static PdfSheet Build(
        VirtualCrossoverProjectFile project,
        string? metricLine,
        int sampleRate,
        PeqQConvention qConvention = PeqQConvention.Rbj)
    {
        ArgumentNullException.ThrowIfNull(project);

        string subtitleText =
            $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
        subtitleText += $"   ·   Q: {PeqQConventions.Describe(qConvention)}";
        if (!string.IsNullOrWhiteSpace(metricLine))
        {
            subtitleText += $"   ·   {metricLine}";
        }

        var sheet = new PdfSheet("Virtual DSP", subtitleText, qConvention);

        // One run of sections per zone, in the order a tune is typed into a DSP
        // (Sub, Front, Rear, Center), each run led by the zone's name and a
        // graph of ITS chains — a system of a dozen channels on one graph is a
        // tangle, and a group's is readable. A single-zone project keeps the
        // flat sheet it always had: one combined graph, no group scaffolding.
        IReadOnlyList<(VirtualCrossoverZone Zone, IReadOnlyList<int> PairIndices)>
            sections = VirtualCrossoverSheetGroups.Sections(project);
        if (sections.Count <= 1)
        {
            List<SheetEntry> participating = Participants(
                project,
                [.. sections.SelectMany(section => section.PairIndices)]);
            if (participating.Count > 0)
            {
                sheet.AddImage(
                    RenderPng(BuildChainsModel(
                        [.. participating.Select(ChannelCurve)], sampleRate)),
                    Unit.FromCentimeter(17));
            }

            foreach (int i in sections.SelectMany(section => section.PairIndices))
            {
                AddPairOrChannelSections(sheet, project, i);
            }

            return sheet;
        }

        // The subwoofer group's chains reappear pale on the FRONT group's graph:
        // the front chain hands its bass over to those subs, and the handover
        // cannot be judged on a graph that shows only one side of it.
        List<SheetEntry> subwooferMembers = [.. sections
            .Where(section => section.Zone == VirtualCrossoverZone.Sub)
            .SelectMany(section => Participants(project, section.PairIndices))];
        foreach ((VirtualCrossoverZone zone, IReadOnlyList<int> pairIndices)
            in sections)
        {
            AddGroupHeading(sheet.Section, VirtualCrossoverZones.DisplayName(zone));
            sheet.AddImage(
                RenderPng(BuildChainsModel(
                    GroupCurves(
                        zone, Participants(project, pairIndices), subwooferMembers),
                    sampleRate)),
                Unit.FromCentimeter(17));
            foreach (int i in pairIndices)
            {
                AddPairOrChannelSections(sheet, project, i);
            }
        }

        return sheet;
    }

    // Both sides of every pair print in one sheet; a mono pair prints once. On
    // the graphs the right side reuses the pair's hue dashed.
    private static List<SheetEntry> Participants(
        VirtualCrossoverProjectFile project,
        IReadOnlyList<int> pairIndices)
    {
        var participants = new List<SheetEntry>();
        foreach (int i in pairIndices)
        {
            foreach ((VirtualCrossoverChannelSettings channel, string sideSuffix)
                in VirtualCrossoverSheet.SideSections(project.Pairs[i]))
            {
                if (channel.HasSource)
                {
                    participants.Add(
                        (i, sideSuffix,
                         sideSuffix == VirtualCrossoverSheet.RightSuffix, channel));
                }
            }
        }

        return participants;
    }

    // A stereo pair with both sides loaded prints as ONE section with an
    // L/R value table — the two sides of a pair are dialed in together,
    // so their numbers belong side by side. A mono pair (or a pair with
    // one loaded side) keeps the single-channel layout.
    private static void AddPairOrChannelSections(
        PdfSheet sheet,
        VirtualCrossoverProjectFile project,
        int i)
    {
        VirtualCrossoverChannelPairSettings pair = project.Pairs[i];
        if (!pair.Mono && pair.Left.HasSource && pair.Right.HasSource)
        {
            AddPairSection(sheet, i, pair.Left, pair.Right);
            return;
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

    // The zone's name above its run of channel sections — a tier above the
    // channel headings (15 pt), so the sheet's two levels read at a glance.
    private static void AddGroupHeading(Section section, string title)
    {
        Paragraph heading = section.AddParagraph(title);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 19;
        heading.Format.SpaceBefore = Unit.FromMillimeter(7);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
        // Never break between the group's name and the graph it introduces.
        heading.Format.KeepWithNext = true;
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
            Column sideColumn = table.AddColumn(SideColumnWidth);
            sideColumn.LeftPadding = Unit.FromMillimeter(2);
        }

        Row header = table.AddRow();
        header.Cells[1].AddParagraph("Left").Format.Font.Bold = true;
        header.Cells[2].AddParagraph("Right").Format.Font.Bold = true;

        AddPairRow(table, "Source", left.DisplayName, right.DisplayName);
        AddPairRow(table, "Gain",
            $"{Signed(left.GainDb)} dB",
            $"{Signed(right.GainDb)} dB");
        if (HasPeqPreamp(left) || HasPeqPreamp(right))
        {
            AddPairRow(table, CombinedGainLabel,
                CombinedGainText(left),
                CombinedGainText(right));
        }
        AddPairRow(table, "Delay", DelayText(left), DelayText(right));
        AddPairRow(table, "Polarity", PolarityText(left), PolarityText(right),
            PolarityColor(left), PolarityColor(right));
        // A row per edge rather than one "Crossover" row: high- and low-pass are two
        // separate entries in the DSP, and a band-pass channel used to print both on a
        // single line joined by "+" — the one value on the sheet that was not one entry.
        AddPairRow(table, HighPassLabel,
            VirtualCrossoverSheet.DescribeHighPass(left),
            VirtualCrossoverSheet.DescribeHighPass(right));
        AddPairRow(table, LowPassLabel,
            VirtualCrossoverSheet.DescribeLowPass(left),
            VirtualCrossoverSheet.DescribeLowPass(right));
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

    private static Color PolarityColor(VirtualCrossoverChannelSettings channel) =>
        channel.InvertPolarity
            ? PdfSheet.InvertedPolarityColor
            : PdfSheet.NormalPolarityColor;

    // The crossover row labels, shared by both layouts so the pair table and the
    // single-channel table cannot end up naming the same edge differently.
    private const string HighPassLabel = "High-pass";
    private const string LowPassLabel = "Low-pass";

    // Many DSPs have no separate preamp for their equalizer, so the PEQ's preamp has to be
    // folded into the channel gain when the tune is typed in. Both numbers are printed:
    // the gain as dialled here, and the single figure such a DSP wants. Only shown when
    // there IS a preamp — otherwise the row would just repeat the gain.
    private const string CombinedGainLabel = "Gain + PEQ preamp";

    private static bool HasPeqPreamp(VirtualCrossoverChannelSettings channel) =>
        channel.PeqPreampDb != 0;

    private static string CombinedGainText(VirtualCrossoverChannelSettings channel) =>
        $"{Signed(channel.GainDb + channel.PeqPreampDb)} dB";

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
        sheet.AddFilterTable(channel.PeqBands, caption);
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
        string rightValue,
        Color? leftColor = null,
        Color? rightColor = null)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;
        WriteValue(row.Cells[1], leftValue, leftColor);
        WriteValue(row.Cells[2], rightValue, rightColor);
    }

    // Every value on the sheet is bold; a colour is set only where one was asked for,
    // so an uncoloured value keeps the document's default text colour.
    private static void WriteValue(Cell cell, string value, Color? color)
    {
        Paragraph paragraph = cell.AddParagraph(value);
        paragraph.Format.Font.Bold = true;
        if (color.HasValue)
        {
            paragraph.Format.Font.Color = color.Value;
        }
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
        Column valueColumn = table.AddColumn(SingleValueColumnWidth);
        valueColumn.LeftPadding = Unit.FromMillimeter(2);

        AddRow(table, "Gain", $"{Signed(channel.GainDb)} dB");
        if (HasPeqPreamp(channel))
        {
            AddRow(table, CombinedGainLabel, CombinedGainText(channel));
        }
        AddRow(table, "Delay", DelayText(channel));
        AddRow(table, "Polarity", PolarityText(channel), PolarityColor(channel));
        AddRow(table, HighPassLabel, VirtualCrossoverSheet.DescribeHighPass(channel));
        AddRow(table, LowPassLabel, VirtualCrossoverSheet.DescribeLowPass(channel));
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

    // The value tables run to the same right edge as the filter-card grid below them
    // (4 x 4.3 cm), so every block on the sheet lines up instead of the tables stopping
    // short. A4 less the 1.5 cm margins leaves 18 cm, so this still has room to spare.
    private static readonly Unit LabelColumnWidth = Unit.FromCentimeter(3.4);
    private static readonly Unit SideColumnWidth = Unit.FromCentimeter(6.9);
    private static readonly Unit SingleValueColumnWidth = Unit.FromCentimeter(13.8);

    private static Table AddValueTable(Section section)
    {
        Table table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = PdfSheet.CardBorderColor;
        Column labelColumn = table.AddColumn(LabelColumnWidth);
        labelColumn.LeftPadding = Unit.FromMillimeter(2);
        return table;
    }

    private static void AddRow(
        Table table,
        string label,
        string value,
        Color? valueColor = null)
    {
        Row row = table.AddRow();
        Paragraph caption = row.Cells[0].AddParagraph(label);
        caption.Format.Font.Color = PdfSheet.CaptionColor;
        WriteValue(row.Cells[1], value, valueColor);
    }

    /// <summary>
    /// One line on a chains graph: the magnitude of the SUM of its chains — a
    /// channel's own curve is a sum of one. Every chain is taken as its
    /// DESIGN: delay and polarity stripped (neither moves a single chain's
    /// magnitude, and folded into a sum they would mix the cabin's timing
    /// compensation into what this graph shows, which is the filters as typed
    /// into the DSP — the acoustic summation lives in the app's plot).
    /// </summary>
    internal sealed record ChainCurve(
        string Title,
        OxyColor Color,
        LineStyle Style,
        double Thickness,
        IReadOnlyList<DspChannelChain> Chains);

    // The neutral tones of the sum curves: dark for the group's own sum, pale
    // for the subwoofer context on the front graph — context must never compete
    // with the channels the graph is about.
    private static readonly OxyColor SumColor = OxyColor.FromRgb(0x38, 0x38, 0x38);
    private static readonly OxyColor SubContextColor =
        OxyColor.FromRgb(0xB4, 0xB4, 0xB4);
    private const double CurveThickness = 2;
    private const double SumThickness = 2.5;

    private static DspChannelChain DesignChain(
        VirtualCrossoverChannelSettings channel) =>
        channel.ToChain() with { DelayMs = 0, InvertPolarity = false };

    private static ChainCurve ChannelCurve(SheetEntry entry) =>
        new(
            $"Channel {VirtualCrossoverSheet.ChannelName(entry.Index)}{entry.SideSuffix}",
            ChainColors[entry.Index % ChainColors.Length],
            entry.Dashed ? LineStyle.Dash : LineStyle.Solid,
            CurveThickness,
            [DesignChain(entry.Channel)]);

    /// <summary>
    /// The curves of ONE group's graph: every member's own chain; the group's
    /// design sum per side — drawn only where a side has at least two chains
    /// to sum (a sum of one would retrace the channel), and never for the
    /// centre, whose signal is derived from L and R so no sum involving it is
    /// honest; and, on the front group, the subwoofer group's sum in a pale
    /// tone as context for the bass handover.
    /// </summary>
    internal static IReadOnlyList<ChainCurve> GroupCurves(
        VirtualCrossoverZone zone,
        IReadOnlyList<SheetEntry> members,
        IReadOnlyList<SheetEntry> subwooferMembers)
    {
        var curves = new List<ChainCurve>(members.Select(ChannelCurve));
        if (zone != VirtualCrossoverZone.Center)
        {
            AddSumCurves(curves, members, "Sum", SumColor, requireTwo: true);
        }

        if (zone == VirtualCrossoverZone.Front)
        {
            AddSumCurves(
                curves, subwooferMembers, "Sub sum", SubContextColor,
                requireTwo: false);
        }

        return curves;
    }

    // A sum is per SIDE — left and right carry different programs, so one line
    // through both would be the comb-filter fiction this tool refuses
    // everywhere. Mono members feed both sides identically; a group of nothing
    // but mono members has one sum, not two copies of it.
    private static void AddSumCurves(
        List<ChainCurve> curves,
        IReadOnlyList<SheetEntry> members,
        string title,
        OxyColor color,
        bool requireTwo)
    {
        List<DspChannelChain> SideChains(string excludedSuffix) =>
            [.. members
                .Where(member => member.SideSuffix != excludedSuffix)
                .Select(member => DesignChain(member.Channel))];

        List<DspChannelChain> left = SideChains(VirtualCrossoverSheet.RightSuffix);
        List<DspChannelChain> right = SideChains(VirtualCrossoverSheet.LeftSuffix);
        int floor = requireTwo ? 2 : 1;
        if (members.All(member =>
            member.SideSuffix == VirtualCrossoverSheet.MonoSuffix))
        {
            if (left.Count >= floor)
            {
                curves.Add(new ChainCurve(
                    title, color, LineStyle.Solid, SumThickness, left));
            }

            return;
        }

        if (left.Count >= floor)
        {
            curves.Add(new ChainCurve(
                $"{title} L", color, LineStyle.Solid, SumThickness, left));
        }

        if (right.Count >= floor)
        {
            curves.Add(new ChainCurve(
                $"{title} R", color, LineStyle.Dash, SumThickness, right));
        }
    }

    // A compact white graph of DSP chain magnitudes (gain + crossover + PEQ).
    // Left and right sides of one pair share a hue; the right side is dashed.
    internal static PlotModel BuildChainsModel(
        IReadOnlyList<ChainCurve> curves,
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
        // The legend lives OUTSIDE the plot area, laid out in rows below it:
        // inside it is an opaque box, and on a real car's front group it sat
        // exactly where the tweeters and the sums run (~0 dB, upper right),
        // hiding everything above the last crossover corner.
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPlacement = OxyPlot.Legends.LegendPlacement.Outside,
            LegendPosition = OxyPlot.Legends.LegendPosition.BottomCenter,
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Horizontal,
            LegendTextColor = OxyColors.Black,
            LegendBorder = OxyColors.Gray,
            LegendBackground = OxyColors.White
        });

        double minDb = -6;
        double maxDb = 6;
        foreach (ChainCurve curve in curves)
        {
            var series = new LineSeries
            {
                Color = curve.Color,
                StrokeThickness = curve.Thickness,
                LineStyle = curve.Style,
                Title = curve.Title
            };
            foreach (double frequency in grid)
            {
                Complex response = Complex.Zero;
                foreach (DspChannelChain chain in curve.Chains)
                {
                    response += chain.Response(frequency, sampleRate);
                }

                double db = DataHelper.AmplitudeToDecibels(response.Magnitude);
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

        return model;
    }

    private static byte[] RenderPng(PlotModel model)
    {
        // A little taller than the old 280: the legend now takes rows under
        // the plot area rather than a box inside it.
        var exporter = new PngExporter { Width = 900, Height = 330 };
        using var stream = new MemoryStream();
        exporter.Export(model, stream);
        return stream.ToArray();
    }

    private static string Signed(double value) =>
        VirtualCrossoverSheet.Signed(value);

    private static string Number(double value, string format) =>
        VirtualCrossoverSheet.Number(value, format);
}
