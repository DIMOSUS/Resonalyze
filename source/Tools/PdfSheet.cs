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
    public const int FilterCardColumns = 4;

    public static readonly Color CaptionColor = Color.FromRgb(90, 90, 90);
    public static readonly Color CardBorderColor = Color.FromRgb(210, 210, 210);

    private readonly Document document;
    private readonly List<string> tempImages = new();

    public Section Section { get; }

    // The built MigraDoc model, for tests that assert the layout before it is
    // rendered to a PDF.
    internal Document Document => document;

    public PdfSheet(string title, string subtitleText)
    {
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

    public void AddFilterCards(IReadOnlyList<PeqBand> bands)
    {
        if (bands.Count == 0)
        {
            return;
        }

        var table = Section.AddTable();
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

            cell.AddParagraph($"{SheetFormat.Number(band.FrequencyHz, "0")} Hz").Format.Font.Size = 12;
            Paragraph gain = cell.AddParagraph($"{SheetFormat.Signed(band.GainDb)} dB");
            gain.Format.Font.Bold = true;
            gain.Format.Font.Size = 12;
            cell.AddParagraph($"Q {SheetFormat.Number(band.Q, "0.0")}").Format.Font.Size = 12;
        }
    }

    public void Save(string filePath)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(filePath);
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
