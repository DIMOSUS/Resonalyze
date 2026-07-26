using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using Resonalyze;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverSheetPdfTests
{
    [Fact]
    public void Build_StereoPair_RendersOneLeftRightTable()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[1].Left.SourceFilePath = "l.json";
        project.Pairs[1].Left.DisplayName = "L woof";
        project.Pairs[1].Left.DelayMs = 4.82;
        project.Pairs[1].Left.GainDb = -1.5;
        project.Pairs[1].Right.SourceFilePath = "r.json";
        project.Pairs[1].Right.DisplayName = "R woof";
        project.Pairs[1].Right.DelayMs = 3.18;
        project.Pairs[1].Right.InvertPolarity = true;

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);
        Table pairTable = PairTables(sheet.Document).Single();

        // A pair table is three columns (label + left + right) with a spelled-out
        // header row: a bare "L"/"R" is easy to misread on a printed sheet.
        Assert.Equal(3, pairTable.Columns.Count);
        Assert.Equal("Left", CellText(pairTable.Rows[0].Cells[1]));
        Assert.Equal("Right", CellText(pairTable.Rows[0].Cells[2]));

        // Both sides' values sit side by side in one row each.
        Assert.Equal("L woof", RowValue(pairTable, "Source", left: true));
        Assert.Equal("R woof", RowValue(pairTable, "Source", left: false));
        Assert.Contains("-1.5 dB", RowValue(pairTable, "Gain", left: true));
        Assert.Contains("4.82 ms", RowValue(pairTable, "Delay", left: true));
        Assert.Contains("mm in air", RowValue(pairTable, "Delay", left: true));
        Assert.Contains("mm in air", RowValue(pairTable, "Delay", left: false));
        Assert.Equal("Normal", RowValue(pairTable, "Polarity", left: true));
        Assert.Equal("Inverted", RowValue(pairTable, "Polarity", left: false));
    }

    [Fact]
    public void Build_MonoPairAndOneSidedPair_UseSingleColumnTables()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        // A stereo pair with only the left side loaded is NOT a pair table.
        project.Pairs[1].Left.SourceFilePath = "half.json";
        project.Pairs[1].Left.DisplayName = "Half";

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);

        // No three-column pair table — every section is a single-channel table.
        Assert.Empty(PairTables(sheet.Document));
    }

    [Fact]
    public void Build_PairSection_PrintsAllPassAndNamesEachSidesPeqInFull()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[1].Left.SourceFilePath = "l.json";
        project.Pairs[1].Left.DisplayName = "L mid";
        project.Pairs[1].Left.AllPassType = AllPassType.SecondOrder;
        project.Pairs[1].Left.AllPassFrequencyHz = 250;
        project.Pairs[1].Left.AllPassQ = 0.7;
        project.Pairs[1].Left.PeqPreampDb = -5;
        project.Pairs[1].Left.PeqSourceName = "L_MID_eq.txt";
        project.Pairs[1].Left.PeqBands.Add(new PeqBand(1000, 2.0, -3.0));
        project.Pairs[1].Left.PeqBands.Add(new PeqBand(250, 4.0, -2.0));
        project.Pairs[1].Right.SourceFilePath = "r.json";
        project.Pairs[1].Right.DisplayName = "R mid";

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);
        Table pairTable = PairTables(sheet.Document).Single();

        // The all-pass is a dialled-in stage; the text sheet always printed it and the
        // PDF must not quietly drop it.
        Assert.Contains("250", RowValue(pairTable, "All-pass", left: true));
        Assert.Equal("—", RowValue(pairTable, "All-pass", left: false));

        // The summary names the profile, its filter count and its preamp, so a channel
        // can be checked against the DSP without counting cards.
        string peq = RowValue(pairTable, "PEQ", left: true);
        Assert.Contains("L_MID_eq.txt", peq);
        Assert.Contains("2 filters", peq);
        Assert.Contains("-5", peq);

        // The cards below are captioned with the channel AND the spelled-out side.
        string document = AllText(sheet.Document);
        Assert.Contains("Channel B Left — PEQ", document);
        Assert.DoesNotContain("PEQ B L", document);
    }

    [Fact]
    public void Build_PrintsTheGainWithThePeqPreampFoldedIn()
    {
        // Many DSPs have no separate preamp for their equalizer, so the tune has to be
        // entered as one gain. Both numbers are printed: the gain as dialled, and the sum
        // such a DSP wants. The right side has no preamp, so its sum is just its gain.
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[1].Left.SourceFilePath = "l.json";
        project.Pairs[1].Left.DisplayName = "L twt";
        project.Pairs[1].Left.GainDb = -1.5;
        project.Pairs[1].Left.PeqPreampDb = -5.0;
        project.Pairs[1].Left.PeqBands.Add(new PeqBand(2_000, 2.0, 6.0));
        project.Pairs[1].Right.SourceFilePath = "r.json";
        project.Pairs[1].Right.DisplayName = "R twt";
        project.Pairs[1].Right.GainDb = -2.0;

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);
        Table pairTable = PairTables(sheet.Document).Single();

        Assert.Contains("-1.5 dB", RowValue(pairTable, "Gain", left: true));
        Assert.Contains("-6.5 dB", RowValue(pairTable, "Gain + PEQ preamp", left: true));
        Assert.Contains("-2.0 dB", RowValue(pairTable, "Gain + PEQ preamp", left: false));
    }

    [Fact]
    public void Build_WithoutAPeqPreamp_OmitsTheCombinedGainRow()
    {
        // With no preamp the row would just repeat the gain.
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[1].Left.SourceFilePath = "l.json";
        project.Pairs[1].Left.DisplayName = "L";
        project.Pairs[1].Right.SourceFilePath = "r.json";
        project.Pairs[1].Right.DisplayName = "R";

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);

        Assert.DoesNotContain("Gain + PEQ preamp", AllText(sheet.Document));
    }

    [Fact]
    public void Build_ValueTables_RunToTheSameWidthAsTheFilterCards()
    {
        // The tables used to stop 3 cm short of the card grid below them, leaving the
        // sheet looking ragged with room to spare. A4 less the 1.5 cm margins is 18 cm.
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.PeqBands.Add(new PeqBand(45, 3.0, -4.0));
        project.Pairs[1].Left.SourceFilePath = "l.json";
        project.Pairs[1].Left.DisplayName = "L";
        project.Pairs[1].Right.SourceFilePath = "r.json";
        project.Pairs[1].Right.DisplayName = "R";

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);

        double cards = TableWidthCentimetres(CardTables(sheet.Document).First());
        foreach (Table table in ValueTables(sheet.Document))
        {
            Assert.Equal(cards, TableWidthCentimetres(table), 3);
        }

        Assert.True(cards <= 18.0, $"The sheet is {cards:0.0} cm wide — past the margins.");
    }

    private static double TableWidthCentimetres(Table table)
    {
        double total = 0;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            total += table.Columns[i].Width.Centimeter;
        }

        return total;
    }

    // The label-plus-value tables: one value column (single channel) or two (a pair).
    private static IEnumerable<Table> ValueTables(Document document)
    {
        DocumentElements elements = document.LastSection.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is Table { Columns.Count: 2 or 3 } table)
            {
                yield return table;
            }
        }
    }

    [Fact]
    public void Build_PeqCaption_IsARepeatingHeadingRowOfEveryBlock()
    {
        // A full bank is 32 filters, so it runs to four blocks and can break across pages.
        // A caption in a paragraph above the table would name only the first page and
        // leave the rest looking like the previous channel's filters. Only a heading row
        // is repeated by MigraDoc on every page a table spans, so the caption must live
        // INSIDE each block as one.
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        for (int i = 0; i < EqualizationCurve.MaxBandCount; i++)
        {
            project.Pairs[0].Left.PeqBands.Add(new PeqBand(100 + (i * 100), 2.0, -1.0));
        }

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);

        Table[] blocks = CardTables(sheet.Document).ToArray();
        Assert.Equal(4, blocks.Length);

        foreach (Table block in blocks)
        {
            Row caption = block.Rows[0];
            Assert.Contains("Channel A (mono) — PEQ", CellText(caption.Cells[0]));
            Assert.True(
                caption.HeadingFormat, "The caption row does not repeat across pages.");
            // Spans the whole block, and cannot be left stranded above its own filters.
            Assert.Equal(block.Columns.Count - 1, caption.Cells[0].MergeRight);
            Assert.True(caption.KeepWith >= 1);
            // Caption, the numbers, then gain / F / Q.
            Assert.Equal(5, block.Rows.Count);
        }

        // A continuation is marked as one, so a block landing alone on a page is not read
        // as a second, separate bank for the same channel.
        Assert.DoesNotContain("(cont.)", CellText(blocks[0].Rows[0].Cells[0]));
        Assert.Contains("(cont.)", CellText(blocks[1].Rows[0].Cells[0]));
    }

    // The bank reads down its columns: filter numbers across the top, then gain, F and Q.
    [Fact]
    public void Build_FilterTable_IsFourRowsWithOneColumnPerFilter()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.PeqBands.Add(new PeqBand(45, 3.0, -4.0));
        project.Pairs[0].Left.PeqBands.Add(new PeqBand(1_250, 6.0, 2.5));

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);
        Table block = CardTables(sheet.Document).Single();

        // Caption, numbers, gain, F, Q — and no row per filter.
        Assert.Equal(5, block.Rows.Count);
        Assert.Equal("PK", CellText(block.Rows[1].Cells[0]));
        Assert.Equal("1", CellText(block.Rows[1].Cells[1]));
        Assert.Equal("2", CellText(block.Rows[1].Cells[2]));

        Assert.Equal("Gain, dB", CellText(block.Rows[2].Cells[0]));
        Assert.Equal("-4.0", CellText(block.Rows[2].Cells[1]));
        Assert.Equal("+2.5", CellText(block.Rows[2].Cells[2]));

        Assert.Equal("F, Hz", CellText(block.Rows[3].Cells[0]));
        Assert.Equal("45", CellText(block.Rows[3].Cells[1]));
        Assert.Equal("1250", CellText(block.Rows[3].Cells[2]));

        Assert.Equal("Q · RBJ", CellText(block.Rows[4].Cells[0]));
        Assert.Equal("3.0", CellText(block.Rows[4].Cells[1]));
        Assert.Equal("6.0", CellText(block.Rows[4].Cells[2]));

        // The slots past the bank stay blank rather than shrinking the block: every block
        // declares the full column set so a continuation lines up under the first.
        Assert.Equal(string.Empty, CellText(block.Rows[3].Cells[3]));
    }

    // The PEQ table is the wide one — a label column plus a column per filter slot; the
    // value tables are two or three columns.
    private static IEnumerable<Table> CardTables(Document document)
    {
        DocumentElements elements = document.LastSection.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is Table table &&
                table.Columns.Count == PdfSheet.FiltersPerTableBlock + 1)
            {
                yield return table;
            }
        }
    }

    [Fact]
    public void Build_SingleChannelSection_CaptionsItsPeqCards()
    {
        // The one-sided layout used to drop the filter cards straight after the table
        // with no caption, so a page of cards did not say whose they were.
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.PeqBands.Add(new PeqBand(45, 3.0, -4.0));

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(project, null, 48_000);

        Assert.Contains("Channel A (mono) — PEQ", AllText(sheet.Document));
    }

    // The filter card is what gets typed into the DSP, so its Q must be the target's.
    // 45 Hz Q 3.0 -4 dB restates to 3.0 * 10^(4/40) = 3.775 under Symmetric and to
    // 3.0 * 10^(-4/40) = 2.384 under Classic — a cut moves them opposite ways.
    [Theory]
    [InlineData(PeqQConvention.Rbj, "Q · RBJ", "3.0")]
    [InlineData(PeqQConvention.Symmetric, "Q · Symmetric", "3.78")]
    [InlineData(PeqQConvention.Classic, "Q · Classic", "2.38")]
    public void Build_FilterTable_RestatesQInTheTargetDspConvention(
        PeqQConvention convention,
        string expectedLabel,
        string expectedQ)
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.PeqBands.Add(new PeqBand(45, 3.0, -4.0));

        using PdfSheet sheet = VirtualCrossoverSheetPdf.Build(
            project, null, 48_000, convention);
        Table block = CardTables(sheet.Document).Single();

        // The Q row names its own convention: a bank runs to several blocks and pages,
        // and the subtitle carrying it is only on the first.
        Assert.Equal(expectedLabel, CellText(block.Rows[4].Cells[0]));
        Assert.Equal(expectedQ, CellText(block.Rows[4].Cells[1]));

        // Only Q moves — frequency and gain mean the same thing on every device.
        Assert.Equal("45", CellText(block.Rows[3].Cells[1]));
        Assert.Equal("-4.0", CellText(block.Rows[2].Cells[1]));
    }

    // Every caption and cell in the sheet, flattened, so a test can assert that a piece
    // of wording appears (or no longer appears) anywhere in the document.
    private static string AllText(Document document)
    {
        var builder = new StringBuilder();
        DocumentElements elements = document.LastSection.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            switch (elements[i])
            {
                case Paragraph paragraph:
                    AppendParagraphText(paragraph, builder);
                    builder.Append('\n');
                    break;
                case Table table:
                    for (int r = 0; r < table.Rows.Count; r++)
                    {
                        for (int c = 0; c < table.Columns.Count; c++)
                        {
                            builder.Append(CellText(table.Rows[r].Cells[c])).Append(' ');
                        }
                    }

                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<Table> PairTables(Document document)
    {
        DocumentElements elements = document.LastSection.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is Table { Columns.Count: 3 } table)
            {
                yield return table;
            }
        }
    }

    // The bold value cell (L or R) of the row whose label matches.
    private static string RowValue(Table table, string label, bool left)
    {
        for (int r = 0; r < table.Rows.Count; r++)
        {
            if (CellText(table.Rows[r].Cells[0]) == label)
            {
                return CellText(table.Rows[r].Cells[left ? 1 : 2]);
            }
        }

        throw new InvalidOperationException($"No row labelled '{label}'.");
    }

    private static string CellText(Cell cell)
    {
        var builder = new StringBuilder();
        AppendText(cell.Elements, builder);
        return builder.ToString();
    }

    private static void AppendText(DocumentElements elements, StringBuilder builder)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i] is Paragraph paragraph)
            {
                AppendParagraphText(paragraph, builder);
            }
        }
    }

    private static void AppendParagraphText(Paragraph paragraph, StringBuilder builder)
    {
        for (int j = 0; j < paragraph.Elements.Count; j++)
        {
            switch (paragraph.Elements[j])
            {
                case Text text:
                    builder.Append(text.Content);
                    break;
                case FormattedText formatted:
                    for (int k = 0; k < formatted.Elements.Count; k++)
                    {
                        if (formatted.Elements[k] is Text inner)
                        {
                            builder.Append(inner.Content);
                        }
                    }
                    break;
            }
        }
    }

    [Fact]
    public void Export_WritesAValidPdfFile()
    {
        var project = new VirtualCrossoverProjectFile();
        project.Pairs[0].Mono = true;
        project.Pairs[0].Left.SourceFilePath = "sub.json";
        project.Pairs[0].Left.DisplayName = "Sub";
        project.Pairs[0].Left.GainDb = -3.0;
        project.Pairs[0].Left.DelayMs = 1.5;
        project.Pairs[0].Left.CrossoverKind = CrossoverKind.LowPass;
        project.Pairs[1].Left.SourceFilePath = "top.json";
        project.Pairs[1].Left.DisplayName = "Top";
        project.Pairs[1].Left.CrossoverKind = CrossoverKind.HighPass;
        project.Pairs[1].Left.PeqBands.Add(new PeqBand(1000, 2.0, -3.0));
        project.Pairs[1].Right.SourceFilePath = "top r.json";
        project.Pairs[1].Right.DisplayName = "Top R";
        project.Pairs[1].Right.CrossoverKind = CrossoverKind.HighPass;
        // A stereo pair with ONE loaded side falls back to the single-channel
        // layout instead of an L/R table with an empty column.
        project.Pairs[2].Right.SourceFilePath = "half.json";
        project.Pairs[2].Right.DisplayName = "Half";

        string path = Path.Combine(Path.GetTempPath(), $"vdsp_{Guid.NewGuid():N}.pdf");
        try
        {
            VirtualCrossoverSheetPdf.Export(path, project, "metric: 0.42", 48_000);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            // Every PDF starts with the "%PDF" signature.
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Export_HandlesProjectWithoutSources()
    {
        var project = new VirtualCrossoverProjectFile();
        string path = Path.Combine(Path.GetTempPath(), $"vdsp_{Guid.NewGuid():N}.pdf");
        try
        {
            VirtualCrossoverSheetPdf.Export(path, project, metricLine: null, 44_100);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
