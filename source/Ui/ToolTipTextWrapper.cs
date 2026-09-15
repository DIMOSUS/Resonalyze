using System.Text;

namespace Resonalyze;

/// <summary>Word-wraps tooltip text (WinForms tooltips never wrap). Author newlines are kept, bullet continuations indented; idempotent.</summary>
internal static class ToolTipTextWrapper
{
    /// <summary>About 450 px at the default tooltip font.</summary>
    public const int DefaultLineLength = 64;

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

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

            // An unbreakable token (path, URL) is hard-split rather than making the tooltip wider than the screen.
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
