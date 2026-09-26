using System.Globalization;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What the user stated about a response file attached as a spatial average; the file records none of it.</summary>
public sealed class SpatialAverageFileSettings
{
    /// <summary>The microphone correction the file's levels already carry; null when they are uncalibrated or <see cref="CalibratedAsIs"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? Calibration { get; set; }

    /// <summary>Stated as already correct (e.g. an array averaged through each capsule's own file): drawn as stored, Mic cal never reaches it.</summary>
    public bool CalibratedAsIs { get; set; }

    /// <summary>Protective high-pass that was in the measured path, divided out on import; Off when there was none.</summary>
    public ProtectiveHighPassKind HighPassKind { get; set; }

    public double HighPassFrequencyHz { get; set; } = 2_000.0;

    public int HighPassSlopeDbPerOctave { get; set; } = 24;

    /// <summary>Rate the high-pass model is realized at: the channel measurement's, which divided the same filter out.</summary>
    public int HighPassSampleRateHz { get; set; }

    [JsonIgnore]
    public ProtectiveHighPassConfiguration HighPass =>
        ProtectiveHighPassConfiguration.Normalize(
            new ProtectiveHighPassConfiguration(HighPassKind, HighPassFrequencyHz, HighPassSlopeDbPerOctave));

    public void Validate()
    {
        Calibration?.Validate();
        if (!Enum.IsDefined(HighPassKind) ||
            !double.IsFinite(HighPassFrequencyHz) || HighPassFrequencyHz <= 0 ||
            HighPassSampleRateHz < 0)
        {
            throw new InvalidDataException("The response file's high-pass is invalid.");
        }
    }
}

/// <summary>One answer to "which calibration do the file's levels carry"; null curve = uncalibrated unless <see cref="AsIs"/>.</summary>
internal sealed record SpatialAverageFileCalibrationChoice(
    string Label,
    VirtualCrossoverCalibrationSettings? Calibration,
    bool AsIs = false)
{
    public const int AsIsIndex = 0;

    public const int NoneIndex = 1;

    public override string ToString() => Label;

    /// <summary>As-is (the default), none, the measurement's own file, then the app's list; a stated curve none of them holds is kept.</summary>
    public static List<SpatialAverageFileCalibrationChoice> Offer(
        VirtualCrossoverCalibrationSettings? stated,
        VirtualCrossoverCalibrationSettings? measurement,
        IEnumerable<(string Name, string? FileName, CalibrationFile Curve)> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var choices = new List<SpatialAverageFileCalibrationChoice>
        {
            new("Already correct — no calibration on top (Mic cal ignored)", null, AsIs: true),
            new("None — the levels are uncalibrated", null)
        };
        if (measurement != null)
        {
            choices.Add(new($"As this channel's measurement: {measurement.Name}", measurement));
        }

        foreach ((string name, string? fileName, CalibrationFile curve) in available)
        {
            if (curve.HasData && !choices.Any(choice => Same(choice.Calibration, curve)))
            {
                choices.Add(new(name, VirtualCrossoverCalibrationSettings.From(curve, name, fileName)));
            }
        }

        if (stated != null && IndexOf(choices, stated) == NoneIndex)
        {
            choices.Add(new($"{stated.Name} (as stated before)", stated));
        }

        return choices;
    }

    /// <summary>The choice holding <paramref name="stated"/>'s curve; <see cref="NoneIndex"/> when there is none.</summary>
    public static int IndexOf(
        IReadOnlyList<SpatialAverageFileCalibrationChoice> choices,
        VirtualCrossoverCalibrationSettings? stated)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (stated == null)
        {
            return NoneIndex;
        }

        CalibrationFile curve = stated.ToCalibrationFile();
        for (int i = 0; i < choices.Count; i++)
        {
            if (Same(choices[i].Calibration, curve))
            {
                return i;
            }
        }

        return NoneIndex;
    }

    private static bool Same(VirtualCrossoverCalibrationSettings? settings, CalibrationFile curve) =>
        settings != null && CalibrationFile.SameCurve(settings.ToCalibrationFile(), curve);
}

/// <summary>Turns a response file and the user's answers into the capture document the hybrid reads.
/// See docs/tech/spatial-average.md#imported-text-files.</summary>
internal static class SpatialAverageFileImport
{
    /// <summary>Coarser than this between 20 Hz and 20 kHz, and the interpolated curve hides detail the plot seems to show.</summary>
    public const double CoarsePointsPerOctave = 12.0;

    private const int FallbackSampleRateHz = 48_000;

    public static FrequencyResponseTextFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!FrequencyResponseTextFile.TryParse(File.ReadAllText(path), out FrequencyResponseTextFile? file, out string? problem))
        {
            throw new InvalidDataException($"The file is not a frequency response: {problem}.");
        }

        return file!;
    }

    public static LiveCaptureDocument Load(string path, SpatialAverageFileSettings answers) =>
        Build(Read(path), answers, path);

    public static LiveCaptureDocument Build(
        FrequencyResponseTextFile file,
        SpatialAverageFileSettings answers,
        string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(answers);
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        double[] curve = SpatialAverage.FromLevels(file.FrequenciesHz, file.LevelsDb);

        double[] calibrationDb = [];
        if (!answers.CalibratedAsIs &&
            answers.Calibration?.ToCalibrationFile() is { HasData: true } calibration)
        {
            calibrationDb = grid.Select(calibration.GetDecibelCorrection).ToArray();
        }

        double[] highPassDb = [];
        ProtectiveHighPassConfiguration highPass = answers.HighPass;
        if (highPass.Enabled)
        {
            highPassDb = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
                highPass.ToEdge(),
                answers.HighPassSampleRateHz > 0
                    ? answers.HighPassSampleRateHz
                    : file.SampleRateHz ?? FallbackSampleRateHz,
                ProtectiveHighPassConfiguration.MaximumCompensationBoostDb,
                grid);
            for (int i = 0; i < curve.Length; i++)
            {
                curve[i] += highPassDb[i];
            }
        }

        var document = new LiveCaptureDocument
        {
            Format = LiveCaptureDocument.CurrentFormat,
            SavedAtUtc = MeasuredAt(file, path),
            Title = string.IsNullOrWhiteSpace(file.MeasurementName)
                ? Path.GetFileNameWithoutExtension(path)
                : file.MeasurementName,
            Method = SpatialAverageMethod.File,
            Recipe = new LiveCaptureRecipe
            {
                SampleRateHz = file.SampleRateHz ?? 0,
                SequenceLength = 0,
                MagnitudeScale = MagnitudeScale.SoundPressureLevel,
                ProtectiveHighPassKind = highPass.Kind,
                ProtectiveHighPassFrequencyHz = highPass.FrequencyHz,
                ProtectiveHighPassSlopeDbPerOctave = highPass.SlopeDbPerOctave
            },
            Calibration = calibrationDb.Length > 0 ? answers.Calibration : null,
            CalibrationFixed = answers.CalibratedAsIs,
            CurveDb = curve,
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            CalibrationCorrectionDb = calibrationDb,
            ProtectiveHighPassCorrectionDb = highPassDb
        };
        document.Validate();
        return document;
    }

    /// <summary>One line on what was read: source, points and span, smoothing.</summary>
    public static string Describe(FrequencyResponseTextFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        string name = file.MeasurementName is { } measurement
            ? $"'{measurement}' in {Path.GetFileName(path)}"
            : Path.GetFileName(path);
        return $"{(file.WrittenByRew ? "REW export" : "Text response")}: {name}" + "\r\n" +
            $"{file.FrequenciesHz.Length.ToString("N0", CultureInfo.InvariantCulture)} points, " +
            $"{Hz(file.FrequenciesHz[0])} to {Hz(file.FrequenciesHz[^1])}" +
            (file.Smoothing is { } smoothing ? $", smoothing {smoothing}" : string.Empty);
    }

    /// <summary>What the file itself gives away that the user should know before attaching it.</summary>
    public static IReadOnlyList<string> Warnings(FrequencyResponseTextFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var warnings = new List<string>();
        if (!file.WrittenByRew)
        {
            warnings.Add("No REW header: nothing about how it was measured could be read. The second " +
                "column is taken as a level in dB.");
        }

        if (file.StatesSmoothing)
        {
            warnings.Add($"Already smoothed ({file.Smoothing}). The plot's smoothing goes on top, and " +
                "what the file's smoothing removed does not come back. Export with smoothing off where you can.");
        }

        double low = file.FrequenciesHz[0];
        double high = file.FrequenciesHz[^1];
        if (low > SpatialAverage.GridStartHz * 1.001 || high < SpatialAverage.GridStopHz * 0.999)
        {
            warnings.Add($"Covers {Hz(low)} to {Hz(high)} only; outside that the hybrid curve breaks.");
        }

        double coarsest = file.CoarsestPointsPerOctave(SpatialAverage.GridStartHz, SpatialAverage.GridStopHz);
        if (coarsest < CoarsePointsPerOctave)
        {
            warnings.Add($"Coarse: {coarsest:0} {(coarsest < 1.5 ? "point" : "points")} per octave where it is sparsest. The curve is " +
                "interpolated between them and cannot show anything narrower.");
        }

        return warnings;
    }

    private static DateTimeOffset MeasuredAt(FrequencyResponseTextFile file, string path)
    {
        // REW writes "2026 Sep 26 19:02:55" in the measuring machine's local time.
        if (file.Dated is { } dated &&
            DateTime.TryParseExact(
                dated, "yyyy MMM d HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime local))
        {
            return new DateTimeOffset(local).ToUniversalTime();
        }

        return File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;
    }

    private static string Hz(double hz) =>
        hz >= 1_000
            ? $"{(hz / 1_000).ToString("0.#", CultureInfo.InvariantCulture)} kHz"
            : $"{hz.ToString("0.#", CultureInfo.InvariantCulture)} Hz";
}
