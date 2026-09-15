namespace Resonalyze;

/// <summary>Temp file then replace, for user exports: File.Create truncates on open, so a crash leaves a broken file.</summary>
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

        // Same volume, so File.Move is a rename.
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
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Do not mask the real exception for a stuck temp file.
            }

            throw;
        }
    }
}
