namespace Resonalyze;

/// <summary>
/// Writes a file so that an interrupted write cannot destroy what was there
/// before: the content goes to a sibling temporary file first and only replaces
/// the target once it is complete.
///
/// The application's own stores (settings, history) already did this inline. The
/// files the USER exports and shares — tuning sheets, PEQ profiles, Virtual DSP
/// projects — did not: they opened the destination with <c>File.Create</c>, which
/// truncates on open, so a crash or a full disk mid-write left a zero-length or
/// half-written file where a good one used to be. Overwriting an existing export
/// is exactly when that matters.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
        });

    public static void Write(string path, Action<Stream> writeContents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeContents);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Beside the target, so the replace stays on one volume and File.Move
        // can be a rename rather than a copy.
        string tempPath = path + ".tmp";
        try
        {
            using (FileStream stream = File.Create(tempPath))
            {
                writeContents(stream);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // Leave the original intact and take the debris with us.
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // The write already failed; a stuck temp file is not worth
                // masking the real exception for.
            }

            throw;
        }
    }
}
