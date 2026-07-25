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
