using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Formats the Virtual DSP settings as a human-readable tuning sheet — the
/// exact list a user types into the DSP: per channel the gain, delay (ms and the
/// mm ruler check), polarity, crossover filters and PEQ bands.
/// </summary>
internal static class VirtualCrossoverSheet
{

    public static string FormatText(
        VirtualCrossoverProjectFile project,
        string? metricLine)
    {
        ArgumentNullException.ThrowIfNull(project);

        var builder = new StringBuilder();
        builder.AppendLine("Resonalyze — Virtual DSP tuning sheet");
        builder.AppendLine(
            $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(metricLine))
        {
            builder.AppendLine(metricLine);
        }

        for (int i = 0; i < project.Channels.Count; i++)
        {
            VirtualCrossoverChannelSettings channel = project.Channels[i];
            if (!channel.HasSource)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"Channel {ChannelName(i)} — {channel.DisplayName}");
            builder.AppendLine($"  Gain       {Signed(channel.GainDb)} dB");
            builder.AppendLine(
                $"  Delay      {Number(channel.DelayMs, "0.00")} ms" +
                $"  (= {Number(channel.DelayMs * Acoustics.SpeedOfSoundAt20CMetersPerSecond, "0.#")} mm)");
            builder.AppendLine(
                $"  Polarity   {(channel.InvertPolarity ? "Inverted" : "Normal")}");
            builder.AppendLine($"  Crossover  {DescribeCrossover(channel)}");
            if (channel.PeqBands.Count > 0 || channel.PeqPreampDb != 0)
            {
                builder.AppendLine(
                    $"  PEQ        {channel.PeqSourceName ?? "custom"}, " +
                    $"preamp {Signed(channel.PeqPreampDb)} dB");
                for (int band = 0; band < channel.PeqBands.Count; band++)
                {
                    PeqBand peq = channel.PeqBands[band];
                    builder.AppendLine(
                        $"    Filter {band + 1}: ON PK Fc {Number(peq.FrequencyHz, "0.###")} Hz " +
                        $"Gain {Signed(peq.GainDb)} dB Q {Number(peq.Q, "0.0#")}");
                }
            }
        }

        return builder.ToString();
    }

    public static string ChannelName(int index) => ((char)('A' + index)).ToString();

    public static string DescribeCrossover(VirtualCrossoverChannelSettings channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.CrossoverKind switch
        {
            CrossoverKind.LowPass => DescribeEdge("Low-pass", channel.LowPassEdge),
            CrossoverKind.HighPass => DescribeEdge("High-pass", channel.HighPassEdge),
            CrossoverKind.BandPass =>
                DescribeEdge("High-pass", channel.HighPassEdge) + " + " +
                DescribeEdge("Low-pass", channel.LowPassEdge),
            _ => "Off"
        };
    }

    private static string DescribeEdge(string kind, CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "Linkwitz-Riley",
            CrossoverFilterFamily.Bessel => "Bessel",
            _ => "Butterworth"
        };
        return $"{kind} {family} {edge.SlopeDbPerOctave} dB/oct @ {Number(edge.FrequencyHz, "0.###")} Hz";
    }

    internal static string Signed(double value) =>
        SheetFormat.Signed(value);

    internal static string Number(double value, string format) =>
        SheetFormat.Number(value, format);
}
