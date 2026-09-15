using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Resolves calibration ids to curves (0 deg file, additional files, angle estimates), caches them, reports each unusable entry once.
/// <see cref="Get"/> runs on plot-build workers too: state is locked, definitions snapshotted, and the callback must marshal to UI.</summary>
internal sealed class MicrophoneCalibrationService
{
    private readonly object sync = new();
    private readonly Dictionary<string, CalibrationFile> cache = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedProblems = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Func<string?> getZeroDegreePath;
    private readonly Func<IReadOnlyList<MicrophoneCalibrationDefinition>> getDefinitions;
    private readonly Action<string, string?> reportProblem;
    private readonly string legacyZeroDegreePath;
    private MicrophoneCalibrationDefinition[] definitions;

    public MicrophoneCalibrationService(
        Func<string?> getZeroDegreePath,
        Func<IReadOnlyList<MicrophoneCalibrationDefinition>> getDefinitions,
        Action<string, string?> reportProblem,
        string? legacyZeroDegreeDirectory = null)
    {
        this.getZeroDegreePath = getZeroDegreePath;
        this.getDefinitions = getDefinitions;
        this.reportProblem = reportProblem;
        legacyZeroDegreePath = Path.Combine(
            legacyZeroDegreeDirectory ?? AppContext.BaseDirectory,
            "calibration.txt");
        definitions = Snapshot();
    }

    /// <summary>Unavailable entries stay selectable so a temporarily missing file does not erase the choice.</summary>
    public IReadOnlyList<MicrophoneCalibrationEntry> GetEntries()
    {
        MicrophoneCalibrationDefinition[] current = definitions;
        string? zeroDegreePath = ResolveZeroDegreePath();
        var entries = new List<MicrophoneCalibrationEntry>(current.Length + 1)
        {
            new(
                MicrophoneCalibrationIds.ZeroDegrees,
                "0°",
                HasUsableData(zeroDegreePath),
                FileNameOf(zeroDegreePath))
        };
        foreach (MicrophoneCalibrationDefinition definition in current)
        {
            entries.Add(new MicrophoneCalibrationEntry(
                definition.Id,
                definition.Name,
                IsAvailable(definition, current),
                definition.Kind == MicrophoneCalibrationKind.File
                    ? FileNameOf(definition.Path)
                    : null));
        }

        return entries;
    }

    public CalibrationFile? Get(string? calibrationId)
    {
        if (MicrophoneCalibrationIds.IsOff(calibrationId))
        {
            return null;
        }

        if (calibrationId == MicrophoneCalibrationIds.ZeroDegrees)
        {
            return GetZeroDegree();
        }

        MicrophoneCalibrationDefinition[] current = definitions;
        MicrophoneCalibrationDefinition? definition = Find(current, calibrationId);
        if (definition == null)
        {
            ReportOnce(
                $"calibration:{calibrationId}",
                $"Microphone calibration '{calibrationId}' is no longer configured.");
            return null;
        }

        return definition.Kind == MicrophoneCalibrationKind.Angle
            ? GetAngled(definition, current)
            : GetFile(definition.Path);
    }

    /// <summary>Problem reports survive invalidation: one warning per entry per session.</summary>
    public void InvalidateCache()
    {
        lock (sync)
        {
            cache.Clear();
        }

        definitions = Snapshot();
    }

    private MicrophoneCalibrationDefinition[] Snapshot() =>
        getDefinitions().Select(definition => definition.Clone()).ToArray();

    private static MicrophoneCalibrationDefinition? Find(
        MicrophoneCalibrationDefinition[] definitions,
        string? calibrationId) =>
        definitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, calibrationId, StringComparison.OrdinalIgnoreCase));

    private bool IsAvailable(
        MicrophoneCalibrationDefinition definition,
        MicrophoneCalibrationDefinition[] current) =>
        definition.Kind == MicrophoneCalibrationKind.Angle
            ? HasUsableData(ResolveBasePath(definition, current))
            : HasUsableData(definition.Path);

    // An unparsable file resolves to all-0 dB, so it counts as unavailable. No reporting: listing must not raise the correction warning.
    private bool HasUsableData(string? path) =>
        Exists(path) && GetLoaded(path!, report: false).HasData;

    private CalibrationFile? GetZeroDegree()
    {
        WarnIfConfiguredMissing(getZeroDegreePath());
        string? path = ResolveZeroDegreePath();
        return path == null ? null : GetLoaded(path);
    }

    private CalibrationFile? GetFile(string? configuredPath)
    {
        WarnIfConfiguredMissing(configuredPath);
        return Exists(configuredPath) ? GetLoaded(configuredPath!) : null;
    }

    private CalibrationFile? GetAngled(
        MicrophoneCalibrationDefinition definition,
        MicrophoneCalibrationDefinition[] current)
    {
        string? basePath = ResolveBasePath(definition, current);
        if (basePath == null)
        {
            WarnIfConfiguredMissing(
                definition.BaseId == null
                    ? getZeroDegreePath()
                    : Find(current, definition.BaseId)?.Path);
            return null;
        }

        CalibrationFile baseCalibration = GetLoaded(basePath);
        if (definition.AngleDegrees <= 0.0)
        {
            return baseCalibration;
        }

        // Keyed by recipe, so an edited angle or diameter does not hit the old curve.
        string cacheKey = FormattableString.Invariant(
            $"angle:{definition.AngleDegrees:R}:{definition.FrontDiameterMm:R}:{definition.Grid}:{definition.Reference}:{basePath}");
        lock (sync)
        {
            if (cache.TryGetValue(cacheKey, out CalibrationFile? cached))
            {
                return cached;
            }
        }

        MicrophoneAngleEstimate estimate =
            MicrophoneAngleModel.Estimate(definition.ToAngleRequest());
        CalibrationFile angled = CalibrationFile.CreateAngled(
            baseCalibration,
            estimate.DeltaDb);
        // First insert wins so concurrent builds share one instance.
        lock (sync)
        {
            if (cache.TryGetValue(cacheKey, out CalibrationFile? raced))
            {
                return raced;
            }

            cache[cacheKey] = angled;
            return angled;
        }
    }

    private string? ResolveBasePath(
        MicrophoneCalibrationDefinition definition,
        MicrophoneCalibrationDefinition[] current)
    {
        if (definition.BaseId == null)
        {
            return ResolveZeroDegreePath();
        }

        MicrophoneCalibrationDefinition? baseDefinition = Find(current, definition.BaseId);
        return baseDefinition is { Kind: MicrophoneCalibrationKind.File } &&
            Exists(baseDefinition.Path)
                ? baseDefinition.Path
                : null;
    }

    private CalibrationFile GetLoaded(string path, bool report = true)
    {
        CalibrationFile? calibrationFile;
        lock (sync)
        {
            if (!cache.TryGetValue(path, out calibrationFile))
            {
                calibrationFile = new CalibrationFile(path);
                cache[path] = calibrationFile;
            }
        }

        // Reported on the load result, not the cache miss: listing warms the cache and would silence it.
        if (report && !calibrationFile.HasData)
        {
            ReportOnce(path, calibrationFile.LoadError);
        }

        return calibrationFile;
    }

    private void WarnIfConfiguredMissing(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || File.Exists(configured))
        {
            return;
        }

        ReportOnce(configured, $"Calibration file not found: {configured}");
    }

    private void ReportOnce(string key, string? message)
    {
        bool reportNow;
        lock (sync)
        {
            reportNow = reportedProblems.Add(key);
        }

        if (reportNow)
        {
            reportProblem(key, message);
        }
    }

    private static bool Exists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string name = Path.GetFileName(path);
            return name.Length == 0 ? null : name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string? ResolveZeroDegreePath()
    {
        string? path = getZeroDegreePath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            return File.Exists(path) ? path : null;
        }

        return File.Exists(legacyZeroDegreePath) ? legacyZeroDegreePath : null;
    }
}
