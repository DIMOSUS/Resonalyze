namespace Resonalyze;

/// <summary>
/// The one channel of a recorded sweep file the analysis runs on, together with
/// the file's shape and the levels that picked it.
/// </summary>
internal sealed record RecordedSweepChannel(
    float[] Samples,
    int SampleRate,
    int ChannelIndex,
    int ChannelCount,
    double PeakDbFs,
    double RmsDbFs);

/// <summary>
/// Reads a sweep recorded outside Resonalyze — a phone, a handheld recorder, a
/// DAW — down to the single microphone channel the measurement layer analyzes.
/// </summary>
internal static class RecordedSweepFile
{
    // Bounds on what one import may cost. A recording of a sweep is seconds
    // long; ten minutes is already far past any plausible take, and the byte
    // cap holds during the decode itself, so a file that lies about its
    // duration stops at the budget instead of exhausting memory first.
    private const double MaximumRecordingMinutes = 10.0;
    private const long MaximumDecodedBytes = 512L * 1024 * 1024;

    public static RecordedSweepChannel Load(
        string path,
        CancellationToken cancellationToken = default)
    {
        AudioFileContent content = AudioFileCodec.Read(
            path,
            TimeSpan.FromMinutes(MaximumRecordingMinutes),
            maximumStoredBytes: MaximumDecodedBytes,
            cancellationToken: cancellationToken);
        return SelectLoudestChannel(content);
    }

    /// <summary>
    /// Picks the channel carrying the measurement. By RMS, not peak: a
    /// recording routinely holds one live microphone and one channel of
    /// near-silence, and a single click or a DC step in the dead channel would
    /// win a peak comparison while carrying no sweep at all. Ties keep the
    /// lower channel, so a genuinely dual-mono file behaves predictably.
    /// </summary>
    public static RecordedSweepChannel SelectLoudestChannel(AudioFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.ChannelCount == 0 || content.FrameCount == 0)
        {
            throw new InvalidOperationException("The file carries no audio samples.");
        }

        int loudest = 0;
        double loudestRms = double.NegativeInfinity;
        AudioChannelLevel[] levels = RecordedLevelMetering.MeasureChannels(content.Channels);
        for (int channel = 0; channel < levels.Length; channel++)
        {
            if (levels[channel].RmsDbFs > loudestRms)
            {
                loudestRms = levels[channel].RmsDbFs;
                loudest = channel;
            }
        }

        return new RecordedSweepChannel(
            content.Channels[loudest],
            content.SampleRate,
            loudest,
            content.ChannelCount,
            levels[loudest].PeakDbFs,
            levels[loudest].RmsDbFs);
    }

    /// <summary>
    /// How the picked channel is named to the user: "left"/"right" for the
    /// stereo case everyone recognizes, a number beyond it.
    /// </summary>
    public static string DescribeChannel(int channelIndex, int channelCount) =>
        channelCount == 2
            ? channelIndex == 0 ? "left" : "right"
            : $"channel {channelIndex + 1}";
}
