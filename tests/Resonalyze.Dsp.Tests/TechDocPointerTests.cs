using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp.Tests;

// Code comments point into docs/tech by heading anchor; a renamed heading must not leave them dangling.
public sealed partial class TechDocPointerTests
{
    private static readonly string[] ScannedDirectories = ["source", "dsp", "audio", "tests", "tools", "docs"];

    [Fact]
    public void EveryDocsTechPointer_ResolvesToAnExistingHeading()
    {
        string root = RepositoryRoot();
        string docsDirectory = Path.Combine(root, "docs", "tech");
        Dictionary<string, HashSet<string>> anchorsByDoc = Directory
            .EnumerateFiles(docsDirectory, "*.md")
            .ToDictionary(path => Path.GetFileName(path), ReadAnchors, StringComparer.Ordinal);

        List<string> broken = [];
        int pointers = 0;
        foreach (string file in ScannedFiles(root))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in PointerPattern().Matches(lines[i]))
                {
                    pointers++;
                    string doc = match.Groups["doc"].Value;
                    string anchor = match.Groups["anchor"].Value;
                    bool resolves = anchorsByDoc.TryGetValue(doc, out HashSet<string>? anchors) &&
                        (anchor.Length == 0 || anchors.Contains(anchor));
                    if (!resolves)
                    {
                        broken.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {match.Value}");
                    }
                }
            }
        }

        Assert.True(pointers > 0, "No docs/tech pointers found; the scan is looking in the wrong place.");
        Assert.True(broken.Count == 0, "Dangling docs/tech pointers:\n" + string.Join('\n', broken));
    }

    private static IEnumerable<string> ScannedFiles(string root)
    {
        IEnumerable<string> nested = ScannedDirectories
            .Select(name => Path.Combine(root, name))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories));
        return nested
            .Concat(Directory.EnumerateFiles(root, "*.md"))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(root, path));
    }

    private static bool IsBuildOutput(string root, string path)
    {
        string[] segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj");
    }

    // GitHub's heading slug: lower case, punctuation other than '-' and '_' dropped, spaces to hyphens.
    private static HashSet<string> ReadAnchors(string docPath)
    {
        HashSet<string> anchors = new(StringComparer.Ordinal);
        bool inFence = false;
        foreach (string line in File.ReadLines(docPath))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            Match heading = HeadingPattern().Match(line);
            if (!inFence && heading.Success)
            {
                string text = heading.Groups["text"].Value.Trim().ToLowerInvariant();
                anchors.Add(SlugPunctuation().Replace(text, string.Empty).Replace(' ', '-'));
            }
        }

        return anchors;
    }

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        DirectoryInfo? directory = new FileInfo(sourcePath).Directory;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No AGENTS.md above {sourcePath}.");
    }

    [GeneratedRegex(@"docs/tech/(?<doc>[A-Za-z0-9_.-]+\.md)(?:#(?<anchor>[A-Za-z0-9_-]+))?")]
    private static partial Regex PointerPattern();

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"[^\w\- ]")]
    private static partial Regex SlugPunctuation();
}
