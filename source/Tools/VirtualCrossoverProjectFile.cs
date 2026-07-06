using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel of the virtual crossover: which measurement feeds it and the DSP
/// chain applied before the virtual sum. The source is re-resolved on load — by
/// history entry first, then by file path — so a renamed history label or a moved
/// file degrades gracefully instead of failing the whole project.
/// </summary>
public sealed class VirtualCrossoverChannelSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set (and the channel is enabled), the channel contributes its raw
    /// measured signal with the whole DSP chain bypassed — no gain, delay,
    /// polarity, crossover or PEQ — for an A/B against the processed result.
    /// </summary>
    public bool Bypass { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }
    public Guid? HistoryEntryId { get; set; }

    public double GainDb { get; set; }
    public double DelayMs { get; set; }
    public bool InvertPolarity { get; set; }

    public CrossoverKind CrossoverKind { get; set; } = CrossoverKind.Off;
    public CrossoverEdge LowPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
    public CrossoverEdge HighPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);

    public double PeqPreampDb { get; set; }
    public List<PeqBand> PeqBands { get; set; } = new();
    /// <summary>The PEQ file the bands came from; display only.</summary>
    public string? PeqSourceName { get; set; }

    // Per-channel curve visibility on the acoustic plot.
    public bool ShowRawCurve { get; set; }
    public bool ShowProcessedCurve { get; set; } = true;

    public bool HasSource =>
        HistoryEntryId.HasValue || !string.IsNullOrWhiteSpace(SourceFilePath);

    /// <summary>The DSP chain these settings describe.</summary>
    public DspChannelChain ToChain()
    {
        CrossoverSpec crossover = CrossoverKind switch
        {
            CrossoverKind.LowPass => new CrossoverSpec(CrossoverKind, LowPassEdge),
            CrossoverKind.HighPass => new CrossoverSpec(CrossoverKind, HighPassEdge: HighPassEdge),
            CrossoverKind.BandPass => new CrossoverSpec(CrossoverKind, LowPassEdge, HighPassEdge),
            _ => CrossoverSpec.Off
        };
        EqualizationCurve? peq = PeqBands.Count > 0 || PeqPreampDb != 0
            ? new EqualizationCurve(PeqBands, PeqPreampDb)
            : null;
        return new DspChannelChain(GainDb, DelayMs, InvertPolarity, crossover, peq);
    }

    public void Validate()
    {
        if (!double.IsFinite(GainDb) || Math.Abs(GainDb) > 60)
        {
            throw new InvalidDataException("The channel gain is invalid.");
        }
        if (!double.IsFinite(DelayMs) || DelayMs is < 0 or > 1_000)
        {
            throw new InvalidDataException("The channel delay is invalid.");
        }
        if (!Enum.IsDefined(CrossoverKind))
        {
            throw new InvalidDataException("The crossover kind is invalid.");
        }
        ValidateEdge(LowPassEdge);
        ValidateEdge(HighPassEdge);
        if (!double.IsFinite(PeqPreampDb) || Math.Abs(PeqPreampDb) > 60)
        {
            throw new InvalidDataException("The PEQ preamp is invalid.");
        }
        if (PeqBands.Count > EqualizationCurve.MaxBandCount)
        {
            throw new InvalidDataException("The PEQ band count is invalid.");
        }
        foreach (PeqBand band in PeqBands)
        {
            if (!double.IsFinite(band.FrequencyHz) || band.FrequencyHz <= 0 ||
                !double.IsFinite(band.Q) || band.Q <= 0 ||
                !double.IsFinite(band.GainDb))
            {
                throw new InvalidDataException("A PEQ band is invalid.");
            }
        }
    }

    // Both edges are validated even when the kind ignores them: they are still
    // shown (greyed out) in the UI and must round-trip as sane values.
    private static void ValidateEdge(CrossoverEdge edge)
    {
        if (!Enum.IsDefined(edge.Family))
        {
            throw new InvalidDataException("The crossover family is invalid.");
        }
        if (!double.IsFinite(edge.FrequencyHz) || edge.FrequencyHz is < 10 or > 24_000)
        {
            throw new InvalidDataException("The crossover corner frequency is invalid.");
        }
        if (!CrossoverFilter.SupportedSlopes(edge.Family).Contains(edge.SlopeDbPerOctave))
        {
            throw new InvalidDataException("The crossover slope is invalid.");
        }
    }
}

/// <summary>
/// Persists the Virtual DSP tool state (channels, their DSP chains and the
/// plot view flags) so a tuning session survives an application restart. The
/// channel count is user-resizable in the tool, from two up to
/// <see cref="MaximumChannelCount"/>.
/// </summary>
public sealed class VirtualCrossoverProjectFile
{
    public const string CurrentFormat = "resonalyze-virtual-crossover";
    public const int CurrentVersion = 1;
    public const int MaximumChannelCount = 8;
    private const string FileName = "virtual-crossover.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }

    // One entry per channel block in the tool (A, B, C); a channel without a
    // source simply does not participate in the sum.
    public List<VirtualCrossoverChannelSettings> Channels { get; set; } =
    [
        new VirtualCrossoverChannelSettings(),
        new VirtualCrossoverChannelSettings(),
        new VirtualCrossoverChannelSettings()
    ];

    // Acoustic-plot view state shared by all channels.
    public bool ShowSumCurve { get; set; } = true;
    public bool ShowLossCurve { get; set; }
    public bool ShowPhaseView { get; set; }
    public int SmoothingInverseOctaves { get; set; } = 12;

    // Microphone calibration applied to the magnitude curves. The measurement is
    // loopback-referenced, so calibration is optional and off by default.
    // Additive: older files lack the field and default to Off.
    public MicrophoneCalibrationMode CalibrationMode { get; set; } =
        MicrophoneCalibrationMode.Off;

    // Phase-view gate, mirroring the Phase mode: a Tukey window of left + plateau
    // + right milliseconds whose left shoulder ends at the gate offset, so room
    // reflections can be gated out of the phase traces. A null offset follows the
    // earliest processed arrival automatically (the pre-configuration default).
    public double? PhaseGateOffsetMs { get; set; }
    public double PhaseGateLeftMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseLeftMs;
    public double PhaseGatePlateauMs { get; set; } =
        FrequencyResponseOptions.DefaultPhasePlateauMs;
    public double PhaseGateRightMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseRightMs;

    // Phase-view τ detrend (ms, absolute from the IR start): one linear-phase
    // reference removed from every channel and the sum alike, so the traces stay
    // readable while their relative phase is preserved. Null follows the
    // earliest processed arrival automatically.
    public double? PhaseDetrendMs { get; set; }

    public static string GetPath(string? rootDirectory = null) =>
        Path.Combine(
            rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "tools"),
            FileName);

    public void Save(string? rootDirectory = null)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;

        string path = GetPath(rootDirectory);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The virtual crossover directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Exports the session to a user-chosen file (same format as the internal
    /// project file), so a tuning setup can be shared or archived.
    /// </summary>
    public void SaveTo(string path)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, SerializerOptions);
    }

    /// <summary>
    /// Imports a session from a user-chosen file. Unlike <see cref="LoadOrDefault"/>
    /// this throws on a broken or incompatible file — an explicit import deserves
    /// an explicit error instead of silently starting fresh.
    /// </summary>
    public static VirtualCrossoverProjectFile LoadFrom(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        VirtualCrossoverProjectFile file =
            JsonSerializer.Deserialize<VirtualCrossoverProjectFile>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException("The session file is empty.");
        file.Validate();
        return file;
    }

    /// <summary>
    /// Loads the saved project, falling back to a fresh two-channel default when
    /// the file is missing, unreadable or from an unknown version — the tool state
    /// is a convenience, so it must never block startup.
    /// </summary>
    public static VirtualCrossoverProjectFile LoadOrDefault(string? rootDirectory = null)
    {
        try
        {
            string path = GetPath(rootDirectory);
            if (!File.Exists(path))
            {
                return new VirtualCrossoverProjectFile();
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            VirtualCrossoverProjectFile? file =
                JsonSerializer.Deserialize<VirtualCrossoverProjectFile>(
                    stream,
                    SerializerOptions);
            if (file == null)
            {
                return new VirtualCrossoverProjectFile();
            }

            file.Validate();
            return file;
        }
        catch
        {
            return new VirtualCrossoverProjectFile();
        }
    }

    public void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported virtual crossover format '{Format}'.");
        }
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported virtual crossover version {Version}.");
        }
        if (Channels.Count is < 2 or > MaximumChannelCount)
        {
            throw new InvalidDataException(
                "The virtual crossover channel count is invalid.");
        }
        if (!OverlaySmoothing.IsValid(SmoothingInverseOctaves))
        {
            throw new InvalidDataException(
                "The virtual crossover smoothing setting is invalid.");
        }
        if (!Enum.IsDefined(CalibrationMode))
        {
            throw new InvalidDataException(
                "The virtual crossover calibration mode is invalid.");
        }
        if (PhaseGateOffsetMs is { } gateOffset &&
            (!double.IsFinite(gateOffset) || gateOffset is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase gate offset is invalid.");
        }
        if (PhaseDetrendMs is { } detrend &&
            (!double.IsFinite(detrend) || detrend is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase detrend is invalid.");
        }
        if (!IsValidGatePart(PhaseGateLeftMs) ||
            !IsValidGatePart(PhaseGatePlateauMs) ||
            !IsValidGatePart(PhaseGateRightMs) ||
            PhaseGateLeftMs + PhaseGatePlateauMs + PhaseGateRightMs <= 0)
        {
            throw new InvalidDataException("The phase gate window is invalid.");
        }

        foreach (VirtualCrossoverChannelSettings channel in Channels)
        {
            channel.Validate();
        }
    }

    private static bool IsValidGatePart(double milliseconds) =>
        double.IsFinite(milliseconds) && milliseconds is >= 0 and <= 1_000;
}
