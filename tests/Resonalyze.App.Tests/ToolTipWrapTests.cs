using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Resonalyze.App.Tests;

/// <summary>Tooltip text is word-wrapped by <see cref="ToolTipTextWrapper"/> at
/// <see cref="ToolTipTextWrapper.DefaultLineLength"/>, and author newlines are kept. A line the author wrapped by
/// hand that overshoots the limit is therefore wrapped a SECOND time, leaving its last word alone on a line.</summary>
public sealed partial class ToolTipWrapTests
{
    /// <summary>A trailing fragment under a quarter of the line reads as an orphan. Longer than that is an ordinary
    /// last line of a paragraph the wrapper owns, which is what an unbroken tooltip is supposed to look like.</summary>
    private const int OrphanLength = ToolTipTextWrapper.DefaultLineLength / 4;

    [Fact]
    public void NoToolTipWrapsToAnOrphanedWord()
    {
        string root = RepositoryRoot();
        var offenders = new List<string>();
        int scanned = 0;
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "source"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(root, file))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach ((int line, string message) in ToolTipMessages(text))
            {
                scanned++;
                string[] authored = message.Split('\n');
                // A tooltip with no line break of its own belongs entirely to the wrapper, and a short last line is
                // just how wrapped prose ends. The defect is mixing: the author broke the lines AND one of them
                // overshoots, so the wrapper breaks it again and the author's structure comes apart.
                if (authored.Count(paragraph => paragraph.Trim().Length > 0) < 2)
                {
                    continue;
                }

                foreach (string paragraph in authored)
                {
                    if (Orphan(paragraph) is not { } orphan)
                    {
                        continue;
                    }

                    offenders.Add(
                        $"{Path.GetRelativePath(root, file)}:{line}: " +
                        $"\"{paragraph}\" leaves \"{orphan}\" alone on a line. " +
                        "Shorten the line under " +
                        $"{ToolTipTextWrapper.DefaultLineLength} characters, or drop the " +
                        "hand-wrapping and let the wrapper do all of it.");
                }
            }
        }

        Assert.True(scanned > 50, $"Only {scanned} tooltips found; the scan is looking in the wrong place.");
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>The wrapped tail of <paramref name="paragraph"/> when it is an orphan, else null. An interpolation
    /// hole stands in for the number it will print: measuring the expression instead would call a long property name
    /// a long line.</summary>
    private static string? Orphan(string paragraph)
    {
        paragraph = InterpolationHole().Replace(paragraph, "0000");
        if (paragraph.Length <= ToolTipTextWrapper.DefaultLineLength)
        {
            return null;
        }

        string[] wrapped = ToolTipTextWrapper.Wrap(paragraph).Split("\r\n");
        string tail = wrapped[^1].Trim();
        return wrapped.Length > 1 && tail.Length is > 0 and < OrphanLength ? tail : null;
    }

    /// <summary>Every string handed to a SetToolTip call, one entry per concatenation run: the arms of a conditional
    /// are separate messages, and joining them would invent a line neither arm has.</summary>
    private static IEnumerable<(int Line, string Message)> ToolTipMessages(string text)
    {
        const string marker = "SetToolTip";
        int at = 0;
        while ((at = text.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
        {
            int open = text.IndexOf('(', at);
            at += marker.Length;
            if (open < 0)
            {
                continue;
            }

            int line = text.Take(open).Count(character => character == '\n') + 1;
            foreach (string message in Runs(text, open + 1, Close(text, open)))
            {
                yield return (line, message);
            }
        }
    }

    private static int Close(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return text.Length;
    }

    private static List<string> Runs(string text, int from, int to)
    {
        var runs = new List<string>();
        var current = new StringBuilder();
        int previousEnd = -1;
        int i = from;
        while (i < to)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            int start = i;
            var literal = new StringBuilder();
            i++;
            while (i < to && text[i] != '"')
            {
                if (text[i] == '\\' && i + 1 < to)
                {
                    literal.Append(text[i + 1] switch
                    {
                        'r' => string.Empty,
                        'n' => "\n",
                        't' => "\t",
                        _ => text[i + 1].ToString()
                    });
                    i += 2;
                    continue;
                }

                literal.Append(text[i]);
                i++;
            }

            i++;
            // Only a '+' joins two literals into one message; anything else (a ':', a ',', a '?') starts a new one.
            bool joined = previousEnd >= 0 &&
                text[previousEnd..start].Replace(" ", string.Empty)
                    .Replace("\r", string.Empty).Replace("\n", string.Empty) == "+";
            if (!joined && current.Length > 0)
            {
                runs.Add(current.ToString());
                current.Clear();
            }

            current.Append(literal);
            previousEnd = i;
        }

        if (current.Length > 0)
        {
            runs.Add(current.ToString());
        }

        return runs;
    }

    private static bool IsBuildOutput(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex InterpolationHole();

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        DirectoryInfo? directory = new FileInfo(sourcePath).Directory;
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"No AGENTS.md above {sourcePath}.");
    }
}
