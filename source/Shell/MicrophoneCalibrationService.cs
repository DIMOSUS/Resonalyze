using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Owns the microphone-calibration state that used to live on <c>Form1</c>:
/// resolves the configured 0°/90° paths (including the legacy
/// <c>calibration.txt</c> fallback and the 90°-from-0° approximation), caches
/// loaded files, and reports each unusable path at most once per session
/// through the callback. <see cref="Get"/> runs on <c>Task.Run</c> plot-build
/// workers as well as the UI thread, so all mutable state is guarded here and
/// the problem callback must marshal to the UI itself.
/// </summary>
internal sealed class MicrophoneCalibrationService
{
    private readonly object sync = new();
    private readonly Dictionary<string, CalibrationFile> cache = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedProblems = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly Func<MicrophoneCalibrationMode, string?> getConfiguredPath;
    private readonly Action<string, string?> reportProblem;
    private readonly string legacyZeroDegreePath;

    public MicrophoneCalibrationService(
        Func<MicrophoneCalibrationMode, string?> getConfiguredPath,
        Action<string, string?> reportProblem,
        string? legacyZeroDegreeDirectory = null)
    {
        this.getConfiguredPath = getConfiguredPath;
        this.reportProblem = reportProblem;
        legacyZeroDegreePath = Path.Combine(
            legacyZeroDegreeDirectory ?? AppContext.BaseDirectory,
            "calibration.txt");
    }

    /// <summary>
    /// Whether <see cref="Get"/> would return a calibration for the mode. 90°
    /// counts as available when only a 0° file is configured, because the 90°
    /// approximation can be derived from it.
    /// </summary>
    public bool Has(MicrophoneCalibrationMode mode) =>
        mode == MicrophoneCalibrationMode.Degrees90
            ? ResolvePath(MicrophoneCalibrationMode.Degrees90) != null ||
              ResolvePath(MicrophoneCalibrationMode.Degrees0) != null
            : ResolvePath(mode) != null;

    public CalibrationFile? Get(MicrophoneCalibrationMode mode)
    {
        WarnIfConfiguredMissing(mode);
        string? path = ResolvePath(mode);
        if (path == null)
        {
            return mode == MicrophoneCalibrationMode.Degrees90
                ? GetApproximateNinetyDegree()
                : null;
        }

        return GetLoaded(path);
    }

    /// <summary>
    /// Drops the cached files so the next <see cref="Get"/> reloads from disk
    /// (called when the configured paths may have changed). The problem
    /// reports deliberately survive: each unusable path warns once per
    /// session, not once per settings change.
    /// </summary>
    public void InvalidateCache()
    {
        lock (sync)
        {
            cache.Clear();
        }
    }

    private CalibrationFile GetLoaded(string path)
    {
        CalibrationFile? calibrationFile;
        string? problemReason = null;
        bool reportNow = false;
        lock (sync)
        {
            if (!cache.TryGetValue(path, out calibrationFile))
            {
                calibrationFile = new CalibrationFile(path);
                cache[path] = calibrationFile;
                if (!calibrationFile.HasData)
                {
                    reportNow = reportedProblems.Add(path);
                    problemReason = calibrationFile.LoadError;
                }
            }
        }

        if (reportNow)
        {
            reportProblem(path, problemReason);
        }

        return calibrationFile;
    }

    // ResolvePath maps a configured-but-deleted file to null, which used to
    // silently disable the correction for every plot.
    private void WarnIfConfiguredMissing(MicrophoneCalibrationMode mode)
    {
        string? configured = getConfiguredPath(mode);
        if (string.IsNullOrWhiteSpace(configured) || File.Exists(configured))
        {
            return;
        }

        bool reportNow;
        lock (sync)
        {
            reportNow = reportedProblems.Add(configured);
        }

        if (reportNow)
        {
            reportProblem(configured, $"Calibration file not found: {configured}");
        }
    }

    private CalibrationFile? GetApproximateNinetyDegree()
    {
        string? zeroDegreePath = ResolvePath(MicrophoneCalibrationMode.Degrees0);
        if (zeroDegreePath == null)
        {
            return null;
        }

        string cacheKey = $"approx90:{zeroDegreePath}";
        lock (sync)
        {
            if (cache.TryGetValue(cacheKey, out CalibrationFile? cached))
            {
                return cached;
            }
        }

        CalibrationFile zeroDegreeCalibration =
            Get(MicrophoneCalibrationMode.Degrees0)
            ?? throw new InvalidOperationException(
                "0 degree microphone calibration is not available.");
        CalibrationFile approximation = CalibrationFile.CreateNinetyDegreeApproximation(
            zeroDegreeCalibration);
        // Two concurrent plot builds can both reach this point; the first
        // insert wins so every caller sees the same instance.
        lock (sync)
        {
            if (cache.TryGetValue(cacheKey, out CalibrationFile? raced))
            {
                return raced;
            }

            cache[cacheKey] = approximation;
            return approximation;
        }
    }

    private string? ResolvePath(MicrophoneCalibrationMode mode)
    {
        string? path = getConfiguredPath(mode);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return File.Exists(path) ? path : null;
        }

        if (mode == MicrophoneCalibrationMode.Degrees0 &&
            File.Exists(legacyZeroDegreePath))
        {
            return legacyZeroDegreePath;
        }

        return null;
    }
}
