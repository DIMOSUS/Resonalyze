namespace Resonalyze;

/// <summary>
/// Reads a sweep recorded outside Resonalyze — a phone, a handheld recorder, a
/// DAW — with the bounds one import is allowed to cost. Which channel carries the
/// measurement is not decided here: that is a question about the sweep, and only
/// the measurement layer knows what the sweep is.
/// </summary>
internal static class RecordedSweepFile
{
    // Bounds on what one import may cost. A recording of a sweep is seconds
    // long; ten minutes is already far past any plausible take, and the byte
    // cap holds during the decode itself, so a file that lies about its
    // duration stops at the budget instead of exhausting memory first.
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

    /// <summary>
    /// How a channel is named to the user: "left"/"right" for the stereo case
    /// everyone recognizes, a number beyond it.
    /// </summary>
    public static string DescribeChannel(int channelIndex, int channelCount) =>
        channelCount == 2
            ? channelIndex == 0 ? "left" : "right"
            : $"channel {channelIndex + 1}";
}
