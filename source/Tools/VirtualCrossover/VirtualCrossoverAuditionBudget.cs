namespace Resonalyze;

/// <summary>How much audio a render may hold: a duration cap and a projected-bytes cap, because memory scales with rate.
/// See docs/tech/spatial-average.md#audition-render.</summary>
internal static class VirtualCrossoverAuditionBudget
{
    public const int MaximumTrackMinutes = 10;
    public const long MaximumPipelineBytes = 1_000_000_000;

    /// <summary>Why a picked track is refused before Render, or null.</summary>
    public static string? Refusal(AudioFileInfo info, int projectRate)
    {
        long projectedBytes = ProjectedPipelineBytes(info, projectRate);
        if (info.Duration > TimeSpan.FromMinutes(MaximumTrackMinutes))
        {
            return $"REFUSED: longer than {MaximumTrackMinutes} minutes — " +
                "use a shorter excerpt.";
        }

        if (projectedBytes <= MaximumPipelineBytes)
        {
            return null;
        }

        double allowedMinutes = MaximumPipelineBytes
            / (ProjectedPipelineBytes(
                info with { Duration = TimeSpan.FromMinutes(1) },
                projectRate) * 1.0);
        return $"REFUSED: rendering this would hold ~" +
            $"{projectedBytes / 1_000_000} MB of audio in memory " +
            $"(bound {MaximumPipelineBytes / 1_000_000} MB). At these " +
            $"rates keep the excerpt under ~{allowedMinutes:0} minutes.";
    }

    /// <summary>Checks the decoded frame count, since the header may lie or the file may have changed since the pick.</summary>
    public static void CheckDecoded(long frameCount, int sourceRate, int projectRate)
    {
        long actualBytes = ProjectedPipelineBytes(frameCount, sourceRate, projectRate);
        if (actualBytes > MaximumPipelineBytes)
        {
            throw new InvalidOperationException(
                $"The decoded track is larger than its header promised: " +
                $"rendering would hold ~{actualBytes / 1_000_000} MB of audio " +
                $"in memory (bound {MaximumPipelineBytes / 1_000_000} MB). " +
                "Use a shorter excerpt.");
        }
    }

    // Peak working set: decoded stereo, resampled copy (if rates differ) and two rendered sides, all float32.
    public static long ProjectedPipelineBytes(long sourceFrames, int sourceRate, int projectRate)
    {
        long renderedFrames = (long)Math.Ceiling(
            sourceFrames * (double)projectRate / sourceRate);
        long resampledFrames = sourceRate == projectRate ? 0 : renderedFrames;
        return 4L * 2L * (sourceFrames + resampledFrames + renderedFrames);
    }

    public static long ProjectedPipelineBytes(AudioFileInfo info, int projectRate) =>
        ProjectedPipelineBytes(
            (long)Math.Ceiling(info.Duration.TotalSeconds * info.SampleRate),
            info.SampleRate,
            projectRate);
}
