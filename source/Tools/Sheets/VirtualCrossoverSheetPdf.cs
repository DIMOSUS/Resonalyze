using System.Globalization;
using System.Numerics;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Color = MigraDoc.DocumentObjectModel.Color;
using SheetEntry = (int Index, string SideSuffix, bool Dashed,
    Resonalyze.VirtualCrossoverChannelSettings Channel,
    Resonalyze.VirtualCrossoverZone Zone);

namespace Resonalyze;

// Virtual DSP settings as a phone-friendly tuning-sheet PDF: a stereo pair prints L/R side by side, mono or one-sided pairs a single column.
// Shared layout lives in PdfSheet.
internal static class VirtualCrossoverSheetPdf
{
    // Print-friendly variants of the on-screen channel palette, one per channel letter.
    private static readonly OxyColor[] ChainColors =
    [
        OxyColor.FromRgb(0x1F, 0x77, 0xB4),
        OxyColor.FromRgb(0xE0, 0x7A, 0x28),
        OxyColor.FromRgb(0x2C, 0xA0, 0x50),
        OxyColor.FromRgb(0x8A, 0x56, 0xC8),
        OxyColor.FromRgb(0x1F, 0x9A, 0xA8),
        OxyColor.FromRgb(0xC8, 0x50, 0x6E),
        OxyColor.FromRgb(0x9A, 0x8A, 0x20),
        OxyColor.FromRgb(0x5A, 0x9A, 0x28)
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

    // Unrendered so tests can walk the MigraDoc document model. The caller owns disposal.
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

        // One run per zone in DSP typing order, each with its own graph (a dozen channels on one graph is a tangle); single-zone stays flat.
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

        // Sub chains reappear pale on the front graph: the bass handover cannot be judged from one side of it.
        List<SheetEntry> subwooferMembers = [.. sections
            .Where(section => section.Zone == VirtualCrossoverZone.Sub)
            .SelectMany(section => Participants(project, section.PairIndices))];
        bool firstGroup = true;
        foreach ((VirtualCrossoverZone zone, IReadOnlyList<int> pairIndices)
            in sections)
        {
            // Each group after the first starts a new page; the first stays on the title page.
            AddGroupHeading(
                sheet.Section,
                VirtualCrossoverZones.DisplayName(zone),
                newPage: !firstGroup);
            firstGroup = false;
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
                         sideSuffix == VirtualCrossoverSheet.RightSuffix, channel,
                         project.Pairs[i].Zone));
                }
            }
        }

        return participants;
    }

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

    // Tests find group headings by this size.
    internal const int GroupHeadingPointSize = 22;

    private static void AddGroupHeading(Section section, string title, bool newPage)
    {
        Paragraph heading = section.AddParagraph(title);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = GroupHeadingPointSize;
        heading.Format.PageBreakBefore = newPage;
        heading.Format.SpaceBefore = Unit.FromMillimeter(7);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
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
        // A row per edge: high- and low-pass are separate DSP entries.
        AddPairRow(table, HighPassLabel,
            VirtualCrossoverSheet.DescribeHighPass(left),
            VirtualCrossoverSheet.DescribeHighPass(right));
        AddPairRow(table, LowPassLabel,
            VirtualCrossoverSheet.DescribeLowPass(left),
            VirtualCrossoverSheet.DescribeLowPass(right));
        // Only where dialled in: devices without the control would send the reader looking for a missing knob.
        if (HasPhaseRotation(left) || HasPhaseRotation(right))
        {
            AddPairRow(table, "Phase", PhaseText(left), PhaseText(right));
        }
        if (left.HasFir || right.HasFir)
        {
            AddPairRow(table, "FIR", FirText(left), FirText(right));
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

    private static string DelayText(VirtualCrossoverChannelSettings channel) =>
        $"{Number(channel.DelayMs, "0.00")} ms " +
        $"(= {Number(channel.DelayMs * Acoustics.SpeedOfSoundAt20CMetersPerSecond, "0.#")} mm in air)";

    private static string PolarityText(VirtualCrossoverChannelSettings channel) =>
        channel.InvertPolarity ? "Inverted" : "Normal";

    private static bool HasPhaseRotation(VirtualCrossoverChannelSettings channel) =>
        channel.PhaseRotationDegrees > 0;

    // The all-pass corner is not printed: the device derives it, and it would read as another value to type.
    private static string PhaseText(VirtualCrossoverChannelSettings channel) =>
        HasPhaseRotation(channel)
            ? $"{Number(channel.PhaseRotationDegrees, "0.###")} deg"
            : "0 deg";

    private static string FirText(VirtualCrossoverChannelSettings channel) =>
        channel.HasFir ? VirtualCrossoverSheet.DescribeFir(channel) : "—";

    private static Color PolarityColor(VirtualCrossoverChannelSettings channel) =>
        channel.InvertPolarity
            ? PdfSheet.InvertedPolarityColor
            : PdfSheet.NormalPolarityColor;

    private const string HighPassLabel = "High-pass";
    private const string LowPassLabel = "Low-pass";

    // Many DSPs have no separate EQ preamp, so the combined figure is printed too (only when a preamp exists).
    private const string CombinedGainLabel = "Gain + PEQ preamp";

    private static bool HasPeqPreamp(VirtualCrossoverChannelSettings channel) =>
        channel.PeqPreampDb != 0;

    private static string CombinedGainText(VirtualCrossoverChannelSettings channel) =>
        $"{Signed(channel.GainDb + channel.PeqPreampDb)} dB";

    private static bool HasPeq(VirtualCrossoverChannelSettings channel) =>
        channel.PeqBands.Count > 0 || channel.PeqPreampDb != 0;

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

    private static void AddPeqCards(
        PdfSheet sheet,
        string caption,
        VirtualCrossoverChannelSettings channel)
    {
        if (channel.PeqBands.Count == 0)
        {
            return;
        }

        // Caption as the card table's heading row, so it repeats when a long bank breaks across pages.
        sheet.AddFilterTable(channel.PeqBands, caption);
    }

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
        if (HasPhaseRotation(channel))
        {
            AddRow(table, "Phase", PhaseText(channel));
        }
        if (channel.HasFir)
        {
            AddRow(table, "FIR", FirText(channel));
        }
        if (HasPeq(channel))
        {
            AddRow(table, "PEQ", PeqSummary(channel));
        }

        KeepTogether(table);
        AddPeqCards(
            sheet,
            $"Channel {VirtualCrossoverSheet.ChannelName(index)}{sideSuffix} — PEQ",
            channel);
    }

    private static void AddSectionHeading(Section section, string title)
    {
        Paragraph heading = section.AddParagraph(title);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
        heading.Format.KeepWithNext = true;
    }

    // Value tables end at the filter-card grid's right edge (4 x 4.3 cm).
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

    /// <summary>Magnitude of the complex sum of its chains (a channel is a sum of one); chains omit delay, see <see cref="DesignChain"/>.</summary>
    internal sealed record ChainCurve(
        string Title,
        OxyColor Color,
        LineStyle Style,
        double Thickness,
        IReadOnlyList<DspChannelChain> Chains);

    private static readonly OxyColor SumColor = OxyColor.FromRgb(0x38, 0x38, 0x38);
    private static readonly OxyColor SubContextColor =
        OxyColor.FromRgb(0xB4, 0xB4, 0xB4);
    private const double CurveThickness = 2;
    private const double SumThickness = 2.5;

    // Only delay is stripped (it would bury the graph in combing). Polarity stays as a design phase term (LR2 knits through its inversion);
    // a junction inverted for acoustic reasons honestly shows a notch here.
    private static DspChannelChain DesignChain(SheetEntry entry) =>
        entry.Channel.ToChain(entry.Zone) with { DelayMs = 0 };

    private static ChainCurve ChannelCurve(SheetEntry entry) =>
        new(
            $"Channel {VirtualCrossoverSheet.ChannelName(entry.Index)}{entry.SideSuffix}",
            ChainColors[entry.Index % ChainColors.Length],
            entry.Dashed ? LineStyle.Dash : LineStyle.Solid,
            CurveThickness,
            [DesignChain(entry)]);

    /// <summary>Members' chains; per-side design sum where a side has 2+ chains (never centre, derived from L/R); front adds the pale sub sum.</summary>
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

    // Sums are per side (L and R carry different programs); mono members feed both, an all-mono group has one sum.
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
                .Select(DesignChain)];

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

    // The right side of a pair shares the hue, dashed.
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
        // OxyPlot 2.x renders no legend unless added. Outside the plot area: inside, it hid the tweeters and sums near 0 dB.
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
