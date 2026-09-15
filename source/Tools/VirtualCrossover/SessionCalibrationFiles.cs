namespace Resonalyze;

/// <summary>File and entry names for a session-carried calibration curve the user keeps.</summary>
internal static class SessionCalibrationFiles
{
    private const string DefaultExtension = ".txt";
    private const string FallbackName = "calibration";

    /// <summary>Refused characters become underscores, a missing extension becomes <c>.txt</c>, a taken name gets a counter.</summary>
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

    /// <summary>Adds <c>(2)</c>, <c>(3)</c>… on a case-insensitive clash, as the selectors show names.</summary>
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

        return builder.ToString().Trim(' ', '.');
    }
}
