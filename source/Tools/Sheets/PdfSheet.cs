using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Resonalyze.Dsp;
using Color = MigraDoc.DocumentObjectModel.Color;

namespace Resonalyze;

/// <summary>Shared layout core of the tuning-sheet PDFs. Images go through temp files (MigraDoc takes a path); Dispose deletes them.</summary>
internal sealed class PdfSheet : IDisposable
{
    public const int FiltersPerTableBlock = 10;

    // Every block declares all columns even when part-filled, so continuation blocks keep the same width.
    private static readonly Unit FilterLabelColumnWidth = Unit.FromCentimeter(2.8);
    private static readonly Unit FilterValueColumnWidth = Unit.FromCentimeter(1.44);

    public static readonly Color CaptionColor = Color.FromRgb(90, 90, 90);
    public static readonly Color CardBorderColor = Color.FromRgb(210, 210, 210);

    // Colour-coded: a wrongly typed polarity is silent (it measures as a hole). Both dark enough for greyscale print.
    public static readonly Color InvertedPolarityColor = Color.FromRgb(180, 30, 30);
    public static readonly Color NormalPolarityColor = Color.FromRgb(20, 115, 65);

    private readonly Document document;
    private readonly List<string> tempImages = new();
    private readonly PeqQConvention qConvention;

    public Section Section { get; }

    internal Document Document => document;

    public PdfSheet(
        string title,
        string subtitleText,
        PeqQConvention qConvention = PeqQConvention.Rbj)
    {
        this.qConvention = qConvention;
        document = new Document();
        Style normalStyle = document.Styles["Normal"]!;
        normalStyle.Font.Name = "Segoe UI";
        normalStyle.Font.Size = 11;

        Section = document.AddSection();
        PageSetup pageSetup = Section.PageSetup!;
        pageSetup.PageFormat = PageFormat.A4;
        pageSetup.TopMargin = Unit.FromCentimeter(1.2);
        pageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        pageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        pageSetup.RightMargin = Unit.FromCentimeter(1.5);

        byte[]? banner = LoadBanner();
        if (banner != null)
        {
            AddImage(banner, Unit.FromCentimeter(11));
        }

        Paragraph titleParagraph = Section.AddParagraph(title);
        titleParagraph.Format.Alignment = ParagraphAlignment.Center;
        titleParagraph.Format.Font.Size = 24;
        titleParagraph.Format.Font.Bold = true;
        titleParagraph.Format.SpaceBefore = Unit.FromMillimeter(3);

        Paragraph subtitle = Section.AddParagraph(subtitleText);
        subtitle.Format.Alignment = ParagraphAlignment.Center;
        subtitle.Format.Font.Size = 9;
        subtitle.Format.Font.Color = Colors.Gray;
        subtitle.Format.SpaceAfter = Unit.FromMillimeter(4);
    }

    public void AddImage(byte[] pngBytes, Unit width)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"resonalyze_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempPath, pngBytes);
        tempImages.Add(tempPath);

        Paragraph paragraph = Section.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        var image = paragraph.AddImage(tempPath);
        image.Width = width;
        image.LockAspectRatio = true;
    }

    /// <summary>Compact PEQ tables read down columns, split into blocks of <see cref="FiltersPerTableBlock"/>.</summary>
    /// <remarks>The caption is a heading row, the only thing MigraDoc repeats across page breaks. Shelves and all-pass bands get
    /// their own tables (different meaning of F/Q, no gain for AP); every table keeps the band's number in the bank.</remarks>
    public void AddFilterTable(IReadOnlyList<PeqBand> bands, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(bands);

        (IReadOnlyList<NumberedBand> peaking,
            IReadOnlyList<NumberedBand> shelving,
            IReadOnlyList<NumberedBand> allPass) = SplitByShape(bands);
        AddBandTable(peaking, caption, TableShape.Bell);
        AddBandTable(shelving, ShapeCaption(caption, "shelving filters"), TableShape.Shelf);
        AddBandTable(allPass, ShapeCaption(caption, "all-pass filters"), TableShape.AllPass);
    }

    internal static (
        IReadOnlyList<NumberedBand> Peaking,
        IReadOnlyList<NumberedBand> Shelving,
        IReadOnlyList<NumberedBand> AllPass)
        SplitByShape(IReadOnlyList<PeqBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        List<NumberedBand> numbered = bands
            .Select((band, index) => new NumberedBand(index + 1, band))
            .ToList();
        return (
            numbered
                .Where(entry =>
                    !entry.Band.Type.IsShelving() && !entry.Band.Type.IsAllPass())
                .ToList(),
            numbered.Where(entry => entry.Band.Type.IsShelving()).ToList(),
            numbered.Where(entry => entry.Band.Type.IsAllPass()).ToList());
    }

    private enum TableShape
    {
        Bell,
        Shelf,
        AllPass
    }

    private void AddBandTable(
        IReadOnlyList<NumberedBand> bands,
        string? caption,
        TableShape shape)
    {
        for (int start = 0; start < bands.Count; start += FiltersPerTableBlock)
        {
            if (start > 0)
            {
                Paragraph gap = Section.AddParagraph();
                gap.Format.Font.Size = 4;
                gap.Format.SpaceAfter = 0;
            }

            AddFilterTableBlock(bands, start, BlockCaption(caption, start), shape);
        }
    }

    private static string? BlockCaption(string? caption, int start) =>
        string.IsNullOrWhiteSpace(caption) || start == 0
            ? caption
            : $"{caption} (cont.)";

    // Shelf/all-pass tables always name themselves: an unlabelled further table reads as a continuation.
    private static string ShapeCaption(string? caption, string shapeName) =>
        string.IsNullOrWhiteSpace(caption)
            ? char.ToUpperInvariant(shapeName[0]) + shapeName[1..]
            : $"{caption} — {shapeName}";

    internal readonly record struct NumberedBand(int Number, PeqBand Band);

    private void AddFilterTableBlock(
        IReadOnlyList<NumberedBand> bands,
        int start,
        string? caption,
        TableShape shape)
    {
        var table = Section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = CardBorderColor;

        Column labelColumn = table.AddColumn(FilterLabelColumnWidth);
        labelColumn.LeftPadding = Unit.FromMillimeter(1.5);
        for (int c = 0; c < FiltersPerTableBlock; c++)
        {
            Column valueColumn = table.AddColumn(FilterValueColumnWidth);
            valueColumn.Format.Alignment = ParagraphAlignment.Center;
            valueColumn.LeftPadding = Unit.FromMillimeter(0.8);
            valueColumn.RightPadding = Unit.FromMillimeter(0.8);
        }

        if (!string.IsNullOrWhiteSpace(caption))
        {
            Row captionRow = table.AddRow();
            captionRow.HeadingFormat = true;
            captionRow.KeepWith = 1;
            captionRow.Borders.Visible = false;
            captionRow.TopPadding = Unit.FromMillimeter(1.5);
            captionRow.Cells[0].MergeRight = FiltersPerTableBlock;

            Paragraph captionParagraph = captionRow.Cells[0].AddParagraph(caption);
            captionParagraph.Format.Font.Bold = true;
            captionParagraph.Format.Font.Size = 12;
        }

        int count = Math.Min(FiltersPerTableBlock, bands.Count - start);

        // Value rows are bound to the header so a block is not split from its column headings.
        Row header = table.AddRow();
        header.HeadingFormat = true;
        header.KeepWith = shape == TableShape.Bell ? 3 : 4;
        WriteLabel(
            header.Cells[0],
            shape switch
            {
                TableShape.Shelf => "Shelf",
                TableShape.AllPass => "All-pass",
                _ => "PK"
            },
            bold: true);
        for (int i = 0; i < count; i++)
        {
            Paragraph number = header.Cells[i + 1].AddParagraph(
                bands[start + i].Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            number.Format.Font.Bold = true;
            number.Format.Font.Size = 10;
        }

        if (shape == TableShape.Shelf)
        {
            AddFilterValueRow(table, "Type", bands, start, count, bold: true,
                value: band => band.Type == PeqBandType.LowShelf ? "LS" : "HS");
        }

        if (shape == TableShape.AllPass)
        {
            // AP1 and AP2 are different DSP slot types; an all-pass has no gain row.
            AddFilterValueRow(table, "Type", bands, start, count, bold: true,
                value: band =>
                    band.Type == PeqBandType.AllPassFirstOrder ? "AP1" : "AP2");
        }
        else
        {
            AddFilterValueRow(table, "Gain, dB", bands, start, count, bold: true,
                value: band => SheetFormat.Signed(band.GainDb));
        }

        // Q is restated in the DSP's convention and labelled per block; shelf/AP Q are not bandwidths, so no convention applies.
        AddFilterValueRow(table, "F, Hz", bands, start, count, bold: false,
            value: band => SheetFormat.Number(band.FrequencyHz, "0"));
        AddFilterValueRow(
            table,
            shape == TableShape.Bell ? $"Q · {PeqQConventions.DescribeShort(qConvention)}" : "Q",
            bands, start, count, bold: false,
            value: band => band.Type == PeqBandType.AllPassFirstOrder
                ? "—"
                : SheetFormat.Number(band.Q, "0.0#"));
    }

    private void AddFilterValueRow(
        Table table,
        string label,
        IReadOnlyList<NumberedBand> bands,
        int start,
        int count,
        bool bold,
        Func<PeqBand, string> value)
    {
        Row row = table.AddRow();
        row.TopPadding = Unit.FromMillimeter(0.8);
        row.BottomPadding = Unit.FromMillimeter(0.8);
        WriteLabel(row.Cells[0], label, bold: false);

        for (int i = 0; i < count; i++)
        {
            PeqBand band = PeqQConventions.ToConvention(bands[start + i].Band, qConvention);
            Paragraph paragraph = row.Cells[i + 1].AddParagraph(value(band));
            paragraph.Format.Font.Size = 10;
            paragraph.Format.Font.Bold = bold;
        }
    }

    private static void WriteLabel(Cell cell, string text, bool bold)
    {
        Paragraph paragraph = cell.AddParagraph(text);
        paragraph.Format.Font.Size = 9;
        paragraph.Format.Font.Bold = bold;
        paragraph.Format.Font.Color = CaptionColor;
    }

    public void Save(string filePath)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        // Through AtomicFile: PdfDocument.Save(path) truncates on open, leaving a broken PDF on failure. AtomicFile owns the stream.
        AtomicFile.Write(
            filePath,
            stream => renderer.PdfDocument.Save(stream, closeStream: false));
    }

    public void Dispose()
    {
        foreach (string temp in tempImages)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
            }
        }

        tempImages.Clear();
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
}
