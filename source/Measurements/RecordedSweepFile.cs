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

    public static string DescribeChannel(int channelIndex, int channelCount) =>
        channelCount == 2
            ? channelIndex == 0 ? "left" : "right"
            : $"channel {channelIndex + 1}";
}
