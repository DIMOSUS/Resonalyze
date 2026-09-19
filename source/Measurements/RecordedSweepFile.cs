namespace Resonalyze;

/// <summary>Reads an externally recorded sweep within import cost bounds; channel choice belongs to the measurement layer.</summary>
internal static class RecordedSweepFile
{
    // The byte cap holds during decode, so a file lying about its duration stops at the budget.
    private const double MaximumRecordingMinutes = 10.0;
    private const long MaximumDecodedBytes = 512L * 1024 * 1024;

    public static AudioFileContent Load(
        string path,
        CancellationToken cancellationToken = default)
    {
        AudioFileContent content = AudioFileCodec.Read(
            path,
            TimeSpan.FromMinutes(MaximumRecordingMinutes),
            maximumStoredBytes: MaximumDecodedBytes,
            cancellationToken: cancellationToken);
        if (content.ChannelCount == 0 || content.FrameCount == 0)
        {
            throw new InvalidOperationException("The file carries no audio samples.");
        }

        return content;
    }

    /// <summary>What the import decided, for the notice after it; empty when it decided nothing (one channel, no stretch).</summary>
    public static IReadOnlyList<string> DescribeImport(AudioFileContent recording, RecordedSweepImport import)
    {
        var notes = new List<string>();
        if (recording.ChannelCount > 1)
        {
            int chosen = import.Channel;
            AudioChannelLevel level = RecordedLevelMetering.MeasureSamples(recording.Channels[chosen]);
            notes.Add(FormattableString.Invariant(
                $"The recording has {recording.ChannelCount} channels; {DescribeChannel(chosen, recording.ChannelCount)} was measured — {level.RmsDbFs:0.0} dBFS RMS, peak {level.PeakDbFs:0.0} dBFS."));
        }

        if (import.TimeScalePpm is { } scalePpm)
        {
            notes.Add(FormattableString.Invariant(
                $"The recording ran {Math.Abs(scalePpm):0} ppm {(scalePpm > 0 ? "slower" : "faster")} than the configured sweep, and the reference was rebuilt to match. That is what two devices with their own clocks do — and what a per-octave time in whole milliseconds cannot always express. Left uncorrected it smears the arrival and the phase at the top of the band."));
        }

        return notes;
    }

    public static string DescribeChannel(int channelIndex, int channelCount) =>
        channelCount == 2
            ? channelIndex == 0 ? "left" : "right"
            : $"channel {channelIndex + 1}";
}
