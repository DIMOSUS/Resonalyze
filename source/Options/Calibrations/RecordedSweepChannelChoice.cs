namespace Resonalyze.Options;

internal sealed record RecordedSweepChannelRow(string Channel, string Match, string Rms, string Peak);

/// <summary>The tracks of an ambiguous recording as the channel dialog lists them, and the one to measure: the best
/// sweep match until the user picks another.</summary>
internal sealed class RecordedSweepChannelChoice
{
    public RecordedSweepChannelChoice(IReadOnlyList<float[]> channels, IReadOnlyList<double> qualities)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(qualities);
        var rows = new List<RecordedSweepChannelRow>(channels.Count);
        for (int channel = 0; channel < channels.Count; channel++)
        {
            AudioChannelLevel level = RecordedLevelMetering.MeasureSamples(channels[channel]);
            rows.Add(new RecordedSweepChannelRow(
                RecordedSweepFile.DescribeChannel(channel, channels.Count),
                FormattableString.Invariant($"{qualities[channel]:0.000}"),
                FormattableString.Invariant($"{level.RmsDbFs:0.0} dBFS"),
                FormattableString.Invariant($"{level.PeakDbFs:0.0} dBFS")));
        }

        Rows = rows;
        SelectedChannel = RecordedSweepChannels.Best(qualities);
    }

    public IReadOnlyList<RecordedSweepChannelRow> Rows { get; }

    public int SelectedChannel { get; private set; }

    public void Select(int channel)
    {
        if (channel >= 0 && channel < Rows.Count)
        {
            SelectedChannel = channel;
        }
    }
}
