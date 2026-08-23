using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Owns the microphone-calibration state: resolves a calibration id to a curve —
/// the configured 0° file (including the legacy <c>calibration.txt</c>
/// fallback), one of the user's additional files, or a curve estimated from one
/// of those for an angle of incidence — caches what it builds, and reports each
/// unusable entry at most once per session through the callback.
/// <see cref="Get"/> runs on <c>Task.Run</c> plot-build workers as well as the UI
/// thread, so all mutable state is guarded here and the problem callback must
/// marshal to the UI itself. The definition list is snapshotted rather than read
/// live: the settings list it comes from is edited on the UI thread while those
/// workers are running.
/// </summary>
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

    /// <summary>
    /// Every selectable calibration in order — the 0° slot first, then the
    /// additional entries — with the name to show and whether it currently
    /// resolves. Drives the selectors, which keep an unavailable entry
    /// selectable so a temporarily missing file does not erase the choice.
    /// </summary>
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
            // A view can outlive the entry it pointed at (a deleted entry, a
            // project from another machine). Say so once instead of silently
            // dropping the correction.
            ReportOnce(
                $"calibration:{calibrationId}",
                $"Microphone calibration '{calibrationId}' is no longer configured.");
            return null;
        }

        return definition.Kind == MicrophoneCalibrationKind.Angle
            ? GetAngled(definition, current)
            : GetFile(definition.Path);
    }

    /// <summary>
    /// Drops the cached files and re-reads the configured entries, so the next
    /// <see cref="Get"/> reflects the edited list (called whenever a calibration
    /// is selected, edited or cleared). The problem reports deliberately
    /// survive: each unusable entry warns once per session, not once per edit.
    /// </summary>
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

    // Availability means the entry yields a real correction, so an existing but
    // unparsable file counts as unavailable: it resolves to a calibration whose
    // every correction is 0 dB, which a selector marked "ready" would hide.
    // Loaded through the same cache as the analysis path, and deliberately
    // WITHOUT reporting: merely listing the entries must not raise the warning
    // that belongs to actually correcting a measurement with them.
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
        // On axis the estimate is the identity, so the source curve is returned
        // as it is rather than run through a model that would add zero.
        if (definition.AngleDegrees <= 0.0)
        {
            return baseCalibration;
        }

        // Keyed by the recipe, not by the entry id: an edited angle or diameter
        // must not read the previous curve back out of the cache.
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
        // Two concurrent plot builds can both reach this point; the first insert
        // wins so every caller sees the same instance.
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

        // Reported on the LOAD RESULT rather than on the cache miss: an entry
        // listed before it is used warms the cache, and hanging the warning on
        // the miss would let that silence it. ReportOnce still keeps it to one
        // warning per path per session.
        if (report && !calibrationFile.HasData)
        {
            ReportOnce(path, calibrationFile.LoadError);
        }

        return calibrationFile;
    }

    // A configured-but-deleted file resolves to null, which would otherwise
    // silently disable the correction for every plot.
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
            // A hand-edited settings file can hold a path no file system accepts;
            // the entry then simply has no file name to report.
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
