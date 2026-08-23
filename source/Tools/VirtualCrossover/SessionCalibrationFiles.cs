namespace Resonalyze;

/// <summary>
/// Where a calibration curve that arrived inside a Virtual DSP session lands
/// when the user keeps it: a file name safe for the file system, distinct from
/// anything already there, and an entry name distinct from the configured ones.
/// </summary>
internal static class SessionCalibrationFiles
{
    private const string DefaultExtension = ".txt";
    private const string FallbackName = "calibration";

    /// <summary>
    /// A path under <paramref name="directory"/> for a curve named
    /// <paramref name="preferredName"/> (a file name or a free-form entry name):
    /// characters the file system refuses become underscores, a missing extension
    /// becomes <c>.txt</c>, and a taken name gets a counter before its extension.
    /// </summary>
    public static string UniquePath(
        string directory,
        string preferredName,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(preferredName);
        ArgumentNullException.ThrowIfNull(exists);

        string fileName = Sanitize(preferredName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        if (stem.Length == 0)
        {
            stem = FallbackName;
        }

        if (extension.Length == 0)
        {
            extension = DefaultExtension;
        }

        string candidate = Path.Combine(directory, stem + extension);
        for (int counter = 2; exists(candidate); counter++)
        {
            candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");
        }

        return candidate;
    }

    /// <summary>
    /// <paramref name="name"/>, or <c>name (2)</c>, <c>name (3)</c>… when an
    /// entry of that name already exists (compared case-insensitively, as the
    /// selectors show them).
    /// </summary>
    public static string UniqueName(string name, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(existingNames);

        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        string stem = name.Trim();
        if (stem.Length == 0)
        {
            stem = FallbackName;
        }

        string candidate = stem;
        for (int counter = 2; taken.Contains(candidate); counter++)
        {
            candidate = $"{stem} ({counter})";
        }

        return candidate;
    }

    private static string Sanitize(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char character in name.Trim())
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        // A name of only dots or spaces is not a file name on Windows.
        return builder.ToString().Trim(' ', '.');
    }
}
