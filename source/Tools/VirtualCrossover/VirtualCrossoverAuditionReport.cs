using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The audition dialog's report: the result once there is one, then the tune, the magnitudes, the calibration
/// note and the track.</summary>
internal static class VirtualCrossoverAuditionReport
{
    public const string Cancelled = "== Result ==\r\nCancelled; nothing was written.";

    public static string Failed(string message) => $"== Result ==\r\nFAILED: {message}";

    public static string Compose(VirtualCrossoverAuditionSession session) =>
        Compose(
            session.Context,
            session.SpatialAverageRequested,
            VirtualCrossoverAuditionCalibration.Note(session)?.Text,
            session.TrackSection,
            session.ResultSection);

    /// <summary>The result leads once present, so a finished render is visible without scrolling.</summary>
    public static string Compose(
        VirtualCrossoverAuditionContext context,
        bool spatialAverageRequested,
        string? calibrationNote,
        string trackSection,
        string resultSection)
    {
        var report = new StringBuilder();
        if (resultSection.Length > 0)
        {
            report.AppendLine(resultSection);
            report.AppendLine();
        }

        report.AppendLine("== Tune ==");
        report.AppendLine($"Project rate: {context.SampleRate} Hz");
        report.AppendLine($"Left side:  {context.LeftChannelCount} channel(s)");
        report.AppendLine($"Right side: {context.RightChannelCount} channel(s)");
        if (context.BorrowedSide != null)
        {
            report.AppendLine(
                $"WARNING: the {context.BorrowedSide} side has no sources of its own — both " +
                "ears will render from the other one and the image will sound " +
                "perfectly centred. That is the missing measurement, not the tune.");
        }

        AppendMagnitudeSection(report, context, spatialAverageRequested);

        if (calibrationNote != null)
        {
            report.AppendLine();
            report.AppendLine("== Calibration ==");
            report.AppendLine(calibrationNote);
        }

        if (trackSection.Length > 0)
        {
            report.AppendLine();
            report.AppendLine(trackSection);
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>A picked track: its format, then the refusal or what the render will do to it.</summary>
    public static string Track(string fileName, AudioFileInfo info, int projectRate, string? refusal)
    {
        var section = new StringBuilder();
        section.AppendLine("== Track ==");
        section.AppendLine(Path.GetFileName(fileName));
        section.AppendLine(
            $"{info.ChannelCount} channel(s), {info.SampleRate} Hz, " +
            $"{Duration(info.Duration)}");
        if (refusal != null)
        {
            section.Append(refusal);
        }
        else
        {
            if (info.SampleRate != projectRate)
            {
                section.AppendLine(
                    $"Will be converted to the project's {projectRate} Hz " +
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
        }

        return section.ToString().TrimEnd();
    }

    public static string UnreadableTrack(string fileName, string message) =>
        "== Track ==\r\n" +
        $"{Path.GetFileName(fileName)}\r\n" +
        $"UNREADABLE: {message}";

    public static string Result(AuditionRenderOutcome outcome, string targetPath)
    {
        AuralizationResult rendered = outcome.Rendered;
        double durationSeconds =
            rendered.Channels[0].Length / (double)rendered.SampleRate;
        var section = new StringBuilder();
        section.AppendLine("== Result ==");
        section.AppendLine($"Magnitudes: {outcome.MagnitudeLabel}");
        section.AppendLine($"Calibration: {outcome.CalibrationLabel}");
        section.AppendLine($"Cabin subtracted: {outcome.CabinLabel}" +
            (outcome.CabinApplied
                ? $" (−{outcome.CabinTwentyHzDb:0.#} dB at 20 Hz)"
                : string.Empty));
        if (outcome.CorrectionFirTaps > 0)
        {
            section.AppendLine(
                $"Correction FIR: {outcome.CorrectionFirTaps} taps, linear " +
                "phase (calibration and cabin combined)");
        }
        section.AppendLine(
            $"Kernels: {outcome.LeftKernelTaps} taps left, " +
            $"{outcome.RightKernelTaps} taps right; decay kept " +
            $"{outcome.LeftTrim.TailMilliseconds:0} / " +
            $"{outcome.RightTrim.TailMilliseconds:0} ms");
        if (rendered.Resampled)
        {
            section.AppendLine(
                $"Track converted {outcome.SourceSampleRate} → " +
                $"{rendered.SampleRate} Hz (the responses were left untouched).");
        }

        section.AppendLine(
            $"Level: {rendered.AppliedGainDb:+0.0;-0.0} dB applied to both " +
            (outcome.CabinApplied
                ? "channels, matched to the no-cabin render (its peak at " +
                    $"{Auralization.DefaultPeakTarget:0.0} dBFS) so the A/B is " +
                    "level-honest"
                : $"channels (peak at {Auralization.DefaultPeakTarget:0.0} dBFS)"));
        section.AppendLine(
            $"Written: {targetPath}");
        section.AppendLine(
            $"Stereo, {rendered.SampleRate} Hz, 24-bit, " +
            $"{Duration(TimeSpan.FromSeconds(durationSeconds))}");
        section.AppendLine();
        if (outcome.CabinApplied)
        {
            section.AppendLine(
                $"The typical {outcome.CabinLabel} bass rise was subtracted: " +
                "at low frequencies you are hearing this car's deviation from " +
                "that typical curve, not the in-car level.");
            section.AppendLine();
        }

        section.Append(
            "Listen through headphones only. The left and right channels are " +
            "the measured acoustic response of the corresponding side at the " +
            "microphone position — drivers, cabin and capsule included, not a " +
            "binaural head simulation. Playing it back through the same system " +
            "would convolve the car twice.");
        return section.ToString();
    }

    // Total minutes, so over-an-hour durations do not show only the remainder.
    private static string Duration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private static void AppendMagnitudeSection(
        StringBuilder report,
        VirtualCrossoverAuditionContext context,
        bool spatialAverageRequested)
    {
        report.AppendLine();
        report.AppendLine("== Magnitudes ==");
        if (context.SpatialAverage == null)
        {
            report.AppendLine(
                "From the impulse responses, measured at one microphone position.");
            report.AppendLine(
                "No spatial average is available: " +
                (context.SpatialAverageReason ?? "this tune has none."));
            return;
        }

        if (!spatialAverageRequested)
        {
            report.AppendLine(
                "From the impulse responses, measured at one microphone position — " +
                "tick the box to hear the spatial averages instead.");
            return;
        }

        report.AppendLine(
            "From the spatial averages: every channel is filtered onto its own " +
            "average over the listening volume instead of the one position the " +
            "responses were measured at.");
        foreach (string line in context.SpatialAverage.ReportLines)
        {
            report.AppendLine(line);
        }

        report.AppendLine(
            "Timing, polarity and the interference between channels are unchanged: " +
            "an average carries no phase, so a junction still cancels the way it " +
            "does at that one position.");
    }
}
