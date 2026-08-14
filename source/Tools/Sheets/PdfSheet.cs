using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Resonalyze.Dsp;
using Color = MigraDoc.DocumentObjectModel.Color;

namespace Resonalyze;

/// <summary>
/// The shared layout core of the tuning-sheet PDF exporters (TuningSheetPdf,
/// VirtualCrossoverSheetPdf): the A4 scaffold with the product banner, title and
/// subtitle, centred PNG images (via temp files — MigraDoc's AddImage takes a
/// path), the PEQ filter cards, and the render-and-save step. Dispose deletes
/// the temp images, so exporters wrap the sheet in a using block.
/// </summary>
internal sealed class PdfSheet : IDisposable
{
    /// <summary>
    /// Filters printed side by side in one block of the PEQ table. A bank longer than
    /// this continues in further blocks below, each the same width so they line up.
    /// </summary>
    public const int FiltersPerTableBlock = 10;

    // Label column plus the filter columns come to the 17.2 cm the rest of the sheet's
    // tables run to; every block declares all its columns even when the last one is
    // part-filled, so a continuation block cannot come out a different width.
    private static readonly Unit FilterLabelColumnWidth = Unit.FromCentimeter(2.8);
    private static readonly Unit FilterValueColumnWidth = Unit.FromCentimeter(1.44);

    public static readonly Color CaptionColor = Color.FromRgb(90, 90, 90);
    public static readonly Color CardBorderColor = Color.FromRgb(210, 210, 210);

    // Polarity is the one setting on the sheet that is silent when it is typed in wrong —
    // a flipped channel measures as a hole rather than as an error — so it is colour-coded
    // to be read at a glance: red for a deliberate flip, green for a channel left alone.
    // Both are dark enough to stay legible printed in greyscale.
    public static readonly Color InvertedPolarityColor = Color.FromRgb(180, 30, 30);
    public static readonly Color NormalPolarityColor = Color.FromRgb(20, 115, 65);

    private readonly Document document;
    private readonly List<string> tempImages = new();
    private readonly PeqQConvention qConvention;

    public Section Section { get; }

    // The built MigraDoc model, for tests that assert the layout before it is
    // rendered to a PDF.
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

    /// <summary>
    /// Lays the bands out as a compact table read down its columns: the filter numbers
    /// across the top, then one row each for gain, centre frequency and Q. A bank wider
    /// than <see cref="FiltersPerTableBlock"/> continues in further blocks below, spaced
    /// slightly apart, each declaring the full column set so the blocks line up.
    /// </summary>
    /// <remarks>
    /// An optional <paramref name="caption"/> is added as the block's HEADING row rather
    /// than as a paragraph above it: a heading row is the only thing MigraDoc repeats when
    /// a table breaks across pages, and a caption left outside would name the first page
    /// and leave the rest anonymous — which on a sheet holding several channels reads as
    /// the wrong channel's filters. Continuation blocks carry it too, marked as such,
    /// since a long bank can put them on a page of their own.
    /// </remarks>
    /// <remarks>
    /// Shelves are printed in a table of their own, after the bells. They are not
    /// the same filter with a different number: their frequency is the middle of a
    /// transition rather than a centre, their Q is a knee rather than a bandwidth
    /// (so the DSP's Q convention does not restate it), and they need a row saying
    /// which direction they shelve. Mixing them into the bell table would put four
    /// meanings under three row labels. Both tables keep the filter's number in the
    /// bank, so the sheet, the panel and an exported profile agree on what
    /// "filter 5" is.
    /// </remarks>
    public void AddFilterTable(IReadOnlyList<PeqBand> bands, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(bands);

        (IReadOnlyList<NumberedBand> peaking, IReadOnlyList<NumberedBand> shelving) =
            SplitByShape(bands);
        AddBandTable(peaking, caption, shelving: false);
        AddBandTable(shelving, ShelfCaption(caption), shelving: true);
    }

    /// <summary>
    /// Splits a bank into the bells and the shelves, each entry keeping its number
    /// in the bank rather than in its own table — the number is what the panel
    /// shows and what an exported profile calls the filter.
    /// </summary>
    internal static (IReadOnlyList<NumberedBand> Peaking, IReadOnlyList<NumberedBand> Shelving)
        SplitByShape(IReadOnlyList<PeqBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        List<NumberedBand> numbered = bands
            .Select((band, index) => new NumberedBand(index + 1, band))
            .ToList();
        return (
            numbered.Where(entry => !entry.Band.Type.IsShelving()).ToList(),
            numbered.Where(entry => entry.Band.Type.IsShelving()).ToList());
    }

    private void AddBandTable(
        IReadOnlyList<NumberedBand> bands,
        string? caption,
        bool shelving)
    {
        for (int start = 0; start < bands.Count; start += FiltersPerTableBlock)
        {
            if (start > 0)
            {
                // Separates the blocks without the weight of a blank line: they are one
                // logical table continued, not four unrelated ones.
                Paragraph gap = Section.AddParagraph();
                gap.Format.Font.Size = 4;
                gap.Format.SpaceAfter = 0;
            }

            AddFilterTableBlock(bands, start, BlockCaption(caption, start), shelving);
        }
    }

    private static string? BlockCaption(string? caption, int start) =>
        string.IsNullOrWhiteSpace(caption) || start == 0
            ? caption
            : $"{caption} (cont.)";

    // The shelf table always names itself, even where the bell table above it needs
    // no caption: an unlabelled second table of filters reads as a continuation.
    private static string ShelfCaption(string? caption) =>
        string.IsNullOrWhiteSpace(caption)
            ? "Shelving filters"
            : $"{caption} — shelving filters";

    /// <summary>A band together with its position in the bank it came from.</summary>
    internal readonly record struct NumberedBand(int Number, PeqBand Band);

    private void AddFilterTableBlock(
        IReadOnlyList<NumberedBand> bands,
        int start,
        string? caption,
        bool shelving)
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
            // Never leave the caption alone at the foot of a page.
            captionRow.KeepWith = 1;
            captionRow.Borders.Visible = false;
            captionRow.TopPadding = Unit.FromMillimeter(1.5);
            captionRow.Cells[0].MergeRight = FiltersPerTableBlock;

            Paragraph captionParagraph = captionRow.Cells[0].AddParagraph(caption);
            captionParagraph.Format.Font.Bold = true;
            captionParagraph.Format.Font.Size = 12;
        }

        int count = Math.Min(FiltersPerTableBlock, bands.Count - start);

        // The corner cell labels what kind of filter the numbers below are — the type to
        // select in the DSP alongside them. The value rows are bound to this row so a
        // block cannot be split from its own column headings.
        Row header = table.AddRow();
        header.HeadingFormat = true;
        header.KeepWith = shelving ? 4 : 3;
        WriteLabel(header.Cells[0], shelving ? "Shelf" : "PK", bold: true);
        for (int i = 0; i < count; i++)
        {
            Paragraph number = header.Cells[i + 1].AddParagraph(
                bands[start + i].Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            number.Format.Font.Bold = true;
            number.Format.Font.Size = 10;
        }

        if (shelving)
        {
            // Which way the shelf runs is the first thing to dial in, and the one thing
            // a bell table never has to say.
            AddFilterValueRow(table, "Type", bands, start, count, bold: true,
                value: band => band.Type == PeqBandType.LowShelf ? "LS" : "HS");
        }

        // Gain first: it is the value most often changed by ear once the sheet is in hand.
        // Q is restated in the target DSP's convention and says so on its own row, because
        // frequency and gain mean the same thing everywhere but Q does not — and a bank
        // running to several blocks must not rely on a note printed once in the subtitle.
        // A shelf's Q is not a bandwidth and no convention restates it, so its row is
        // labelled plainly rather than with a convention it does not follow.
        AddFilterValueRow(table, "Gain, dB", bands, start, count, bold: true,
            value: band => SheetFormat.Signed(band.GainDb));
        AddFilterValueRow(table, "F, Hz", bands, start, count, bold: false,
            value: band => SheetFormat.Number(band.FrequencyHz, "0"));
        AddFilterValueRow(
            table,
            shelving ? "Q" : $"Q · {PeqQConventions.DescribeShort(qConvention)}",
            bands, start, count, bold: false,
            value: band => SheetFormat.Number(band.Q, "0.0#"));
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

        // Through AtomicFile like every other export: PdfDocument.Save(path)
        // truncates the destination on open, so overwriting an existing sheet
        // and failing partway left a broken PDF where a good one had been.
        // closeStream: false — AtomicFile owns the stream's lifetime.
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
                // Best-effort cleanup; a leftover temp image must not fail
                // (or mask the failure of) an export.
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
