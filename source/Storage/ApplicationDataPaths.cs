namespace Resonalyze;

/// <summary>
/// Owns every implicit application-data path. Installed builds use the current
/// user's LocalAppData directory; placing <c>portable.flag</c> beside the
/// executable keeps all data beside the application. Existing side-by-side
/// data is copied forward once, without overwriting newer destination files.
/// </summary>
internal sealed class ApplicationDataPaths
{
    private const string ApplicationDirectoryName = "Resonalyze";
    private const string PortableMarkerFileName = "portable.flag";
    private const string MigrationMarkerFileName = ".migration-v1-complete";

    public static ApplicationDataPaths Current { get; } = CreateDefault();

    private readonly string executableDirectory;

    internal ApplicationDataPaths(
        string executableDirectory,
        string localApplicationDataDirectory,
        bool? portable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);

        this.executableDirectory = Path.GetFullPath(executableDirectory);
        IsPortable = portable ?? File.Exists(Path.Combine(
            this.executableDirectory,
            PortableMarkerFileName));
        RootDirectory = IsPortable
            ? this.executableDirectory
            : Path.Combine(
                Path.GetFullPath(localApplicationDataDirectory),
                ApplicationDirectoryName);
    }

    public bool IsPortable { get; }
    public string RootDirectory { get; }
    public string SettingsFile => Path.Combine(RootDirectory, "measurement-settings.json");
    public string HistoryFile => Path.Combine(RootDirectory, "measurement-history.json");
    public string OverlaysDirectory => Path.Combine(RootDirectory, "overlays");
    public string ToolsDirectory => Path.Combine(RootDirectory, "tools");
    public string CrashLogFile => Path.Combine(RootDirectory, "crash.log");
    public string MeasurementErrorLogFile =>
        Path.Combine(RootDirectory, "measurement-error.log");
    public string VirtualDspAlignmentLogFile =>
        Path.Combine(ToolsDirectory, "virtual-dsp-align.log");
    internal string MigrationMarkerFile =>
        Path.Combine(RootDirectory, MigrationMarkerFileName);

    public IReadOnlyList<string> Prepare()
    {
        var warnings = new List<string>();
        try
        {
            Directory.CreateDirectory(RootDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"The application data directory '{RootDirectory}' cannot be created: {exception.Message}");
            return warnings;
        }

        if (IsPortable ||
            PathsEqual(RootDirectory, executableDirectory) ||
            File.Exists(MigrationMarkerFile))
        {
            return warnings;
        }

        CopyLegacyFile("measurement-settings.json", SettingsFile, warnings);
        CopyLegacyFile("measurement-history.json", HistoryFile, warnings);
        CopyLegacyFile("crash.log", CrashLogFile, warnings);
        CopyLegacyFile("measurement-error.log", MeasurementErrorLogFile, warnings);
        CopyLegacyDirectory("overlays", OverlaysDirectory, warnings);
        CopyLegacyDirectory("tools", ToolsDirectory, warnings);

        if (warnings.Count == 0)
        {
            try
            {
                File.WriteAllText(MigrationMarkerFile, string.Empty);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add(
                    $"Legacy data migration completed, but its marker '{MigrationMarkerFile}' " +
                    $"could not be written: {exception.Message}");
            }
        }

        return warnings;
    }

    private void CopyLegacyFile(
        string legacyRelativePath,
        string destination,
        List<string> warnings)
    {
        string source = Path.Combine(executableDirectory, legacyRelativePath);
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"Existing user data '{source}' could not be migrated to '{destination}': {exception.Message}");
        }
    }

    private void CopyLegacyDirectory(
        string legacyRelativePath,
        string destination,
        List<string> warnings)
    {
        string source = Path.Combine(executableDirectory, legacyRelativePath);
        if (!Directory.Exists(source))
        {
            return;
        }

        try
        {
            foreach (string sourceFile in Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(source, sourceFile);
                string destinationFile = Path.Combine(destination, relativePath);
                if (File.Exists(destinationFile))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                File.Copy(sourceFile, destinationFile, overwrite: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"Existing user data directory '{source}' could not be fully migrated to " +
                $"'{destination}': {exception.Message}");
        }
    }

    private static ApplicationDataPaths CreateDefault() => new(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
