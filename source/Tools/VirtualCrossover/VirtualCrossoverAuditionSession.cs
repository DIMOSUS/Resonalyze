using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record AuditionCabinOption(CabinBodyStyle? Style, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The audition dialog's state: the track and the output with the consent to replace it, the calibration, cabin
/// and magnitude choices, the report's sections and the render in flight. See docs/tech/virtual-dsp-panel.md#audition-code-map.</summary>
internal sealed class VirtualCrossoverAuditionSession
{
    // Duration cap plus a separate projected-bytes cap (memory scales with rate).
    public const int MaximumTrackMinutes = 10;
    public const long MaximumPipelineBytes = 1_000_000_000;

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

    // Remembered per process, not persisted: a session renders one track through several tunes. Re-probed on restore.
    private static string? lastSourcePath;
    private static string? lastTargetPath;
    private static bool lastSpatialAverage = true;
    private static CabinBodyStyle? lastCabinStyle = CabinBodyStyle.Sedan;

    private CancellationTokenSource? activeRender;

    private VirtualCrossoverAuditionSession(VirtualCrossoverAuditionContext context) => Context = context;

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

    /// <summary>The previous dialog's track (re-probed), output, cabin and magnitude choice.</summary>
    public static VirtualCrossoverAuditionSession Restore(VirtualCrossoverAuditionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var session = new VirtualCrossoverAuditionSession(context)
        {
            CabinStyle = lastCabinStyle,
            SpatialAverageRequested = context.SpatialAverage != null && lastSpatialAverage
        };
        if (lastSourcePath != null)
        {
            session.SelectSource(lastSourcePath);
        }

        // Restored without overwrite consent: the file is the previous render.
        session.TargetPath = lastTargetPath;
        return session;
    }

    // Unconditional: saving null stops the next opening retrying a dead path.
    public void Remember()
    {
        lastSourcePath = SourcePath;
        lastTargetPath = TargetPath;
        lastCabinStyle = CabinStyle;
        // Only a real choice: an unticked box with no averages is not a preference.
        if (Context.SpatialAverage != null)
        {
            lastSpatialAverage = SpatialAverageRequested;
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
            long projectedBytes = ProjectedPipelineBytes(info, Context.SampleRate);
            var section = new StringBuilder();
            section.AppendLine("== Track ==");
            section.AppendLine(Path.GetFileName(fileName));
            section.AppendLine(
                $"{info.ChannelCount} channel(s), {info.SampleRate} Hz, " +
                $"{FormatDuration(info.Duration)}");
            if (info.Duration > TimeSpan.FromMinutes(MaximumTrackMinutes))
            {
                section.Append(
                    $"REFUSED: longer than {MaximumTrackMinutes} minutes — " +
                    "use a shorter excerpt.");
                SourcePath = null;
            }
            else if (projectedBytes > MaximumPipelineBytes)
            {
                double allowedMinutes = MaximumPipelineBytes
                    / (ProjectedPipelineBytes(
                        info with { Duration = TimeSpan.FromMinutes(1) },
                        Context.SampleRate) * 1.0);
                section.Append(
                    $"REFUSED: rendering this would hold ~" +
                    $"{projectedBytes / 1_000_000} MB of audio in memory " +
                    $"(bound {MaximumPipelineBytes / 1_000_000} MB). At these " +
                    $"rates keep the excerpt under ~{allowedMinutes:0} minutes.");
                SourcePath = null;
            }
            else
            {
                if (info.SampleRate != Context.SampleRate)
                {
                    section.AppendLine(
                        $"Will be converted to the project's {Context.SampleRate} Hz " +
                        "(the measured responses are never resampled).");
                }
                if (info.ChannelCount == 1)
                {
                    section.AppendLine("Mono: the same signal will feed both sides.");
                }
                else if (info.ChannelCount > 2)
                {
                    section.AppendLine(
                        "Only the first two channels will feed the two sides.");
                }

                SourcePath = fileName;
            }

            TrackSection = section.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            SourcePath = null;
            TrackSection =
                "== Track ==\r\n" +
                $"{Path.GetFileName(fileName)}\r\n" +
                $"UNREADABLE: {exception.Message}";
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

    // Peak working set: decoded stereo, resampled copy (if rates differ) and two rendered sides, all float32.
    internal static long ProjectedPipelineBytes(long sourceFrames, int sourceRate, int projectRate)
    {
        long renderedFrames = (long)Math.Ceiling(
            sourceFrames * (double)projectRate / sourceRate);
        long resampledFrames = sourceRate == projectRate ? 0 : renderedFrames;
        return 4L * 2L * (sourceFrames + resampledFrames + renderedFrames);
    }

    private static long ProjectedPipelineBytes(AudioFileInfo info, int projectRate) =>
        ProjectedPipelineBytes(
            (long)Math.Ceiling(info.Duration.TotalSeconds * info.SampleRate),
            info.SampleRate,
            projectRate);

    // Total minutes, so over-an-hour durations do not show only the remainder.
    internal static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private static bool PathsEqual(string? first, string? second) =>
        first != null && second != null &&
        string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
}
