using System.Text;

namespace Resonalyze;

/// <summary>
/// Word-wraps tooltip text. A WinForms tooltip never wraps by itself: one long sentence
/// is drawn as a single line that can span — and run off — the whole screen, so the prose
/// tooltips this app leans on have to arrive pre-broken.
/// </summary>
/// <remarks>
/// The author's own newlines are kept as they are (they separate bullets and caveats) and
/// each such paragraph is wrapped within them. Continuation lines of a bulleted paragraph
/// are indented under the item's text so the list stays scannable. Wrapping is idempotent:
/// text that already fits comes back unchanged, so re-wrapping is harmless.
/// </remarks>
internal static class ToolTipTextWrapper
{
    /// <summary>
    /// Longest line the wrapper aims for, in characters. At the tooltip's default font
    /// that is roughly 450 px — wide enough for a technical sentence, narrow enough to
    /// stay next to the control it explains.
    /// </summary>
    public const int DefaultLineLength = 64;

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    // Markers that open a list item; a wrapped continuation is indented past them.
    private static readonly string[] BulletMarkers = ["• ", "- ", "– ", "* "];

    public static string Wrap(string? text, int maxLineLength = DefaultLineLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineLength, 8);
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string[] paragraphs = text.Split(LineBreaks, StringSplitOptions.None);
        var lines = new List<string>(paragraphs.Length);
        foreach (string paragraph in paragraphs)
        {
            WrapParagraph(paragraph, maxLineLength, lines);
        }

        // Windows tooltips take CRLF; normalize to it even when the input used \n.
        return string.Join("\r\n", lines);
    }

    private static void WrapParagraph(
        string paragraph,
        int maxLineLength,
        List<string> output)
    {
        if (paragraph.Length <= maxLineLength)
        {
            output.Add(paragraph);
            return;
        }

        string leading = paragraph[..(paragraph.Length - paragraph.TrimStart().Length)];
        string indent = leading + new string(' ', BulletWidth(paragraph));
        // A deeply indented paragraph must not squeeze the text down to nothing: give up
        // on the alignment rather than on the wrapping.
        if (indent.Length > maxLineLength - 8)
        {
            indent = string.Empty;
        }

        var line = new StringBuilder();
        int emitted = 0;
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0)
            {
                line.Append(emitted == 0 ? leading : indent);
            }
            else if (line.Length + 1 + word.Length > maxLineLength)
            {
                output.Add(line.ToString());
                emitted++;
                line.Clear();
                line.Append(indent);
            }
            else
            {
                line.Append(' ');
            }

            line.Append(word);

            // A single unbreakable token — a path, a URL — can outgrow the whole line
            // budget on its own. Hard-split the overflow: one break inside a path beats
            // a tooltip wider than the screen.
            while (line.Length > maxLineLength)
            {
                output.Add(line.ToString(0, maxLineLength));
                emitted++;
                string remainder = line.ToString(
                    maxLineLength,
                    line.Length - maxLineLength);
                line.Clear();
                line.Append(indent).Append(remainder);
            }
        }

        if (line.Length > 0)
        {
            output.Add(line.ToString());
        }
    }

    private static int BulletWidth(string paragraph)
    {
        string trimmed = paragraph.TrimStart();
        foreach (string marker in BulletMarkers)
        {
            if (trimmed.StartsWith(marker, StringComparison.Ordinal))
            {
                return marker.Length;
            }
        }

        return 0;
    }
}
