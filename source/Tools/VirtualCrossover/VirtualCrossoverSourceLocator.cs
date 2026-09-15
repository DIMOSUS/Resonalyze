namespace Resonalyze;

/// <summary>
/// Finds a session's measurement when its stored absolute path is dead: first the export-relative path, then the
/// stored path's tails (longest first) under the session folder. Never enumerates directories.
/// See docs/tech/virtual-dsp-session-file.md#source-paths.
/// </summary>
internal static class VirtualCrossoverSourceLocator
{
    // Deep enough for real trees, shallow enough that a tail never becomes generic enough to match by accident.
    private const int MaximumTailDepth = 6;

    /// <summary>Stored path if it exists, else the relative or first tail match under <paramref name="searchDirectory"/>; null when nothing resolves.</summary>
    internal static string? Locate(
        string? storedPath, string? relativePath, string? searchDirectory)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }
        if (File.Exists(storedPath))
        {
            return storedPath;
        }
        if (string.IsNullOrWhiteSpace(searchDirectory))
        {
            return null;
        }

        if (Resolve(searchDirectory, relativePath) is { } relative)
        {
            return relative;
        }

        foreach (string tail in TrailingSegments(storedPath))
        {
            if (Resolve(searchDirectory, tail) is { } candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Path relative to <paramref name="exportDirectory"/>; null across volumes or for a path that is not fully qualified.</summary>
    internal static string? Relativize(string? absolutePath, string exportDirectory)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) ||
            !Path.IsPathFullyQualified(absolutePath))
        {
            return null;
        }

        try
        {
            string? sourceRoot = Path.GetPathRoot(Path.GetFullPath(absolutePath));
            string? exportRoot = Path.GetPathRoot(Path.GetFullPath(exportDirectory));
            if (string.IsNullOrEmpty(sourceRoot) ||
                !string.Equals(sourceRoot, exportRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string relative = Path.GetRelativePath(exportDirectory, absolutePath);
            return Path.IsPathRooted(relative) ? null : relative;
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return null;
        }
    }

    // A rooted relative part is refused: a hand-edited session must not gain an absolute reference this way.
    private static string? Resolve(string searchDirectory, string? relativePart)
    {
        if (string.IsNullOrWhiteSpace(relativePart) || Path.IsPathRooted(relativePart))
        {
            return null;
        }

        try
        {
            string candidate =
                Path.GetFullPath(Path.Combine(searchDirectory, relativePart));
            return File.Exists(candidate) ? candidate : null;
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return null;
        }
    }

    // Longest first: the count of agreeing components is the only evidence, and a shallow match could swap measurements.
    private static IEnumerable<string> TrailingSegments(string storedPath)
    {
        string tail = Path.GetFileName(storedPath);
        if (tail.Length == 0)
        {
            yield break;
        }

        var tails = new List<string> { tail };
        string? remainder = Path.GetDirectoryName(storedPath);
        while (tails.Count < MaximumTailDepth)
        {
            string? segment = Path.GetFileName(remainder);
            if (string.IsNullOrEmpty(segment))
            {
                break;
            }

            tail = Path.Combine(segment, tail);
            tails.Add(tail);
            remainder = Path.GetDirectoryName(remainder);
        }

        for (int index = tails.Count - 1; index >= 0; index--)
        {
            yield return tails[index];
        }
    }

    private static bool IsPathFailure(Exception exception) =>
        exception is ArgumentException or PathTooLongException or NotSupportedException;
}
