namespace Resonalyze;

/// <summary>
/// Finds the measurement file a Virtual DSP session refers to when the stored
/// absolute path no longer resolves.
/// <para>
/// A session file travels: it is exported, mailed, copied to another machine or
/// simply moved together with the measurements it was tuned on. Its channel
/// paths, however, are absolute and written on the machine that made them, so on
/// arrival every one of them points at a folder that does not exist and the whole
/// session opens with unresolved channels. Since the measurements normally travel
/// WITH the session, the folder the session file itself was opened from is the one
/// honest hint about where they went — so the stored path is retried against it,
/// in two steps.
/// </para>
/// <para>
/// FIRST the path stored RELATIVE to the exporting session's own folder (see
/// <see cref="Relativize"/>), which reproduces the original layout exactly,
/// including measurements kept in a SIBLING folder (<c>..\v4\mid.json</c>) — the
/// common case in a real tuning session, and one no search under the session's
/// folder can reach.
/// </para>
/// <para>
/// THEN the stored path's TAIL, LONGEST first: the deepest chunk of the stored
/// path under the session's folder, then one folder less, down to the bare file
/// name. That resolves a whole tree copied across and, last, the flat case
/// (everything in one folder), and it is the only route for a session exported by
/// a build that wrote no relative path. Neither step ever enumerates a directory —
/// only paths the stored ones actually name are probed, so a same-named
/// measurement sitting in an unrelated sibling folder is never picked up.
/// </para>
/// </summary>
internal static class VirtualCrossoverSourceLocator
{
    // How many leading folders of the stored path may be dropped. Deep enough for
    // the real trees (car\v5\left\woofer.json), shallow enough that a stored path
    // can never be reduced to a tail so generic that it matches by accident.
    private const int MaximumTailDepth = 6;

    /// <summary>
    /// The path the measurement can actually be read from: the stored one when it
    /// still exists, otherwise the relative path or the first tail match under
    /// <paramref name="searchDirectory"/> — the folder the session file was loaded
    /// from, or one the user pointed at to relink. Null for the internal autosave,
    /// which has no companion folder to search. Null result: nothing resolves and
    /// the channel stays unresolved.
    /// </summary>
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

    /// <summary>
    /// The measurement's path as an EXPORTED session records it beside the absolute
    /// one: relative to <paramref name="exportDirectory"/>, so a session copied to
    /// another machine together with its measurements finds them wherever the pair
    /// landed, as long as their relative arrangement survived the copy.
    /// <para>
    /// Null across volumes — a relative path cannot cross one (<see
    /// cref="Path.GetRelativePath"/> would just hand back the absolute path, which
    /// is already stored) — and null for a path that is not fully qualified, which
    /// has no fixed meaning to be relative TO.
    /// </para>
    /// </summary>
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

    // One candidate under the search directory, or null when it does not exist (or
    // cannot even be formed). A rooted "relative" part is refused rather than
    // silently probed as an absolute path: only Combine's own behaviour would make
    // it one, and a hand-edited session must not gain a second absolute reference
    // that never passed through Relativize.
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

    // The stored path's tails, LONGEST first: "v5\left\woofer.json",
    // "left\woofer.json", "woofer.json". Longest first because the number of path
    // components that still agree is the whole evidence a tail match has — a new
    // tree holding both <session>\left\woofer.json and a different
    // <session>\woofer.json would otherwise answer with the shallow one, and two
    // measurements of the same rate and format swap silently. Dropping components
    // one at a time still reaches the flat case, just last. Collection stops at the
    // root (the drive or UNC share is not a folder name that could repeat under the
    // session's folder).
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

    // A path the platform refuses to even form (illegal characters, too long, a
    // device name). It is not a locate failure worth reporting — the candidate
    // simply does not exist.
    private static bool IsPathFailure(Exception exception) =>
        exception is ArgumentException or PathTooLongException or NotSupportedException;
}
