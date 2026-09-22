using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record AuditionCabinOption(CabinBodyStyle? Style, string Label)
{
    public override string ToString() => Label;
}

/// <summary>What an audition dialog leaves for the next one: kept per process, not persisted, since a session renders
/// one track through several tunes. The track is probed again on restore.</summary>
internal sealed class VirtualCrossoverAuditionMemory
{
    public static VirtualCrossoverAuditionMemory Process { get; } = new();

    public string? SourcePath { get; set; }

    public string? TargetPath { get; set; }

    public bool SpatialAverage { get; set; } = true;

    public CabinBodyStyle? CabinStyle { get; set; } = CabinBodyStyle.Sedan;
}

/// <summary>The audition dialog's state: the track and the output with the consent to replace it, the calibration, cabin
/// and magnitude choices, the report's sections and the render in flight. See docs/tech/virtual-dsp-panel.md#audition-code-map.</summary>
internal sealed class VirtualCrossoverAuditionSession
{
    // "off" first: index 0 is the fallback everywhere.
    public static readonly IReadOnlyList<AuditionCabinOption> CabinOptions =
    [
        new(null, "off (as measured)"),
        new(CabinBodyStyle.Sedan, "average sedan"),
        new(CabinBodyStyle.CompactSedan, "compact sedan"),
        new(CabinBodyStyle.Hatchback, "hatchback"),
        new(CabinBodyStyle.Wagon, "wagon"),
        new(CabinBodyStyle.Suv, "SUV / crossover"),
        new(CabinBodyStyle.BmwF30SkiHatch, "BMW F30, ski hatch open")
    ];

    private readonly VirtualCrossoverAuditionMemory memory;
    private CancellationTokenSource? activeRender;

    private VirtualCrossoverAuditionSession(
        VirtualCrossoverAuditionContext context, VirtualCrossoverAuditionMemory memory)
    {
        Context = context;
        this.memory = memory;
    }

    public VirtualCrossoverAuditionContext Context { get; }

    public string? SourcePath { get; private set; }

    public string? TargetPath { get; private set; }

    /// <summary>Granted in this dialog only (an existing file picked past its overwrite prompt, or the render's question):
    /// a restored path is the previous render, and replacing it silently would collapse an A/B pair.</summary>
    public bool TargetOverwriteConfirmed { get; private set; }

    public string? CalibrationId { get; private set; }

    /// <summary>The calibration as its list names it.</summary>
    public string CalibrationName { get; private set; } = string.Empty;

    public CabinBodyStyle? CabinStyle { get; set; }

    public string CabinLabel => CabinOptions.First(option => option.Style == CabinStyle).Label;

    /// <summary>The tick, which the dialog mutes rather than disables when the tune has no averages.</summary>
    public bool SpatialAverageRequested { get; set; }

    public VirtualCrossoverAuditionSpatialAverage? RequestedSpatialAverage =>
        SpatialAverageRequested ? Context.SpatialAverage : null;

    public string TrackSection { get; private set; } = string.Empty;

    public string ResultSection { get; set; } = string.Empty;

    public bool Rendering => activeRender != null;

    /// <summary>Close was asked for mid-render: the dialog closes once the cancelled render returns.</summary>
    public bool CloseRequested { get; set; }

    /// <summary>A track and an output that are two different files.</summary>
    public bool HasDistinctFiles =>
        SourcePath != null && TargetPath != null && !PathsEqual(SourcePath, TargetPath);

    public bool TargetNeedsConsent =>
        TargetPath != null && File.Exists(TargetPath) && !TargetOverwriteConfirmed;

    /// <summary>The previous dialog's track, output, cabin and magnitude choice.</summary>
    public static VirtualCrossoverAuditionSession Restore(
        VirtualCrossoverAuditionContext context, VirtualCrossoverAuditionMemory memory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(memory);
        var session = new VirtualCrossoverAuditionSession(context, memory)
        {
            // A style the list does not offer falls back to its first entry, as the list does.
            CabinStyle = CabinOptions.Any(option => option.Style == memory.CabinStyle) ? memory.CabinStyle : null,
            SpatialAverageRequested = context.SpatialAverage != null && memory.SpatialAverage
        };
        if (memory.SourcePath != null)
        {
            session.SelectSource(memory.SourcePath);
        }

        // Restored without overwrite consent: the file is the previous render.
        session.TargetPath = memory.TargetPath;
        return session;
    }

    // Unconditional: saving null stops the next opening retrying a dead path.
    public void Remember()
    {
        memory.SourcePath = SourcePath;
        memory.TargetPath = TargetPath;
        memory.CabinStyle = CabinStyle;
        // Only a real choice: an unticked box with no averages is not a preference.
        if (Context.SpatialAverage != null)
        {
            memory.SpatialAverage = SpatialAverageRequested;
        }
    }

    public void SelectCalibration(string? calibrationId, string name)
    {
        CalibrationId = calibrationId;
        CalibrationName = name;
    }

    public bool IsSource(string path) => PathsEqual(path, SourcePath);

    public bool IsTarget(string path) => PathsEqual(path, TargetPath);

    /// <summary>Probes at pick time so an unreadable, too long or too large file is refused before Render.</summary>
    public void SelectSource(string fileName)
    {
        try
        {
            AudioFileInfo info = AudioFileCodec.Probe(fileName);
            string? refusal = VirtualCrossoverAuditionBudget.Refusal(info, Context.SampleRate);
            TrackSection = VirtualCrossoverAuditionReport.Track(fileName, info, Context.SampleRate, refusal);
            SourcePath = refusal == null ? fileName : null;
        }
        catch (Exception exception)
        {
            SourcePath = null;
            TrackSection = VirtualCrossoverAuditionReport.UnreadableTrack(fileName, exception.Message);
        }

        ResultSection = string.Empty;
    }

    /// <summary>An existing file was just confirmed by the save dialog's prompt; a new one asks on the next render.</summary>
    public void SelectTarget(string fileName)
    {
        TargetOverwriteConfirmed = File.Exists(fileName);
        TargetPath = fileName;
    }

    public void ConfirmOverwrite() => TargetOverwriteConfirmed = true;

    public CancellationToken BeginRender()
    {
        activeRender = new CancellationTokenSource();
        return activeRender.Token;
    }

    public void EndRender()
    {
        activeRender?.Dispose();
        activeRender = null;
    }

    /// <summary>True when this call cancelled the render in flight.</summary>
    public bool RequestCancel()
    {
        if (activeRender is not { IsCancellationRequested: false } cancellation)
        {
            return false;
        }

        cancellation.Cancel();
        return true;
    }

    private static bool PathsEqual(string? first, string? second) =>
        first != null && second != null &&
        string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
}
