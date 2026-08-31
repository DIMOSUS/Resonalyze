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
        string? metricLine,
        PeqQConvention qConvention = PeqQConvention.Rbj)
    {
        ArgumentNullException.ThrowIfNull(project);

        var builder = new StringBuilder();
        builder.AppendLine("Resonalyze — Virtual DSP tuning sheet");
        builder.AppendLine(
            $"Generated {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        // Named on every sheet, not only when it deviates: a sheet that stays silent
        // about its Q convention is exactly how a tune ends up measuring wider than it
        // was designed, and the reader cannot tell a silent sheet from an RBJ one.
        builder.AppendLine($"PEQ Q convention: {PeqQConventions.Describe(qConvention)}");
        if (!string.IsNullOrWhiteSpace(metricLine))
        {
            builder.AppendLine(metricLine);
        }

        // One run of sections per zone, in the order a tune is typed into a DSP
        // (Sub, Front, Rear, Center) — with the zone named above its run. A
        // single-zone project skips the headings and keeps the flat sheet it
        // always had: a heading that names the only group there is would be
        // scaffolding around nothing.
        IReadOnlyList<(VirtualCrossoverZone Zone, IReadOnlyList<int> PairIndices)>
            sections = VirtualCrossoverSheetGroups.Sections(project);
        bool grouped = sections.Count > 1;
        foreach ((VirtualCrossoverZone zone, IReadOnlyList<int> pairIndices)
            in sections)
        {
            if (grouped)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"=== {VirtualCrossoverZones.DisplayName(zone)} ===");
            }

            foreach (int i in pairIndices)
            {
                AppendPairSections(builder, project, i, qConvention);
            }
        }

        return builder.ToString();
    }

    // The body of one pair's printed sections, shared by the grouped and flat
    // layouts above so the two cannot come to print a channel differently.
    private static void AppendPairSections(
        StringBuilder builder,
        VirtualCrossoverProjectFile project,
        int i,
        PeqQConvention qConvention)
    {
        foreach ((VirtualCrossoverChannelSettings channel, string sideSuffix)
            in SideSections(project.Pairs[i]))
        {
            if (!channel.HasSource)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine(
                $"Channel {ChannelName(i)}{sideSuffix} — {channel.DisplayName}");
            // Many DSPs have no separate preamp for their equalizer, so the PEQ preamp
            // has to be folded into the channel gain when the tune is typed in. The
            // combined figure rides along on the same line, where it cannot be
            // mistaken for a second, independent gain to enter.
            string gainLine = $"  Gain       {Signed(channel.GainDb)} dB";
            if (channel.PeqPreampDb != 0)
            {
                gainLine +=
                    $"  (with PEQ preamp: {Signed(channel.GainDb + channel.PeqPreampDb)} dB)";
            }

            builder.AppendLine(gainLine);
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
                    PeqBand peq = PeqQConventions.ToConvention(
                        channel.PeqBands[band], qConvention);
                    // The keyword comes from the profile writer rather than being
                    // spelled here: a shelf printed as PK is an instruction to
                    // dial in the wrong filter. An all-pass line carries no gain
                    // (the filter has none) and drops the Q on a first order,
                    // which has no Q to dial in.
                    string tail = peq.Type.IsAllPass()
                        ? peq.Type == PeqBandType.AllPassFirstOrder
                            ? string.Empty
                            : $" Q {Number(peq.Q, "0.0#")}"
                        : $" Gain {Signed(peq.GainDb)} dB Q {Number(peq.Q, "0.0#")}";
                    builder.AppendLine(
                        $"    Filter {band + 1}: ON {PeqTextFile.TypeToken(peq.Type)} " +
                        $"Fc {Number(peq.FrequencyHz, "0.###")} Hz{tail}");
                }
            }
        }
    }

    public static string ChannelName(int index) => ((char)('A' + index)).ToString();

    /// <summary>
    /// The printable sides of one channel pair: a mono pair is a single
    /// "(mono)" section, a stereo pair prints its left and right sides
    /// separately.
    /// </summary>
    // The side suffixes a section heading carries. Spelled out rather than "L"/"R":
    // a printed sheet is read in a car, often upside down on a phone. Named constants
    // because consumers switch on the suffix, and a literal would silently stop matching
    // the moment the wording changed.
    internal const string LeftSuffix = " Left";
    internal const string RightSuffix = " Right";
    internal const string MonoSuffix = " (mono)";

    internal static IEnumerable<(VirtualCrossoverChannelSettings Settings, string SideSuffix)>
        SideSections(VirtualCrossoverChannelPairSettings pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (pair.Mono)
        {
            yield return (pair.Left, MonoSuffix);
            yield break;
        }

        yield return (pair.Left, LeftSuffix);
        yield return (pair.Right, RightSuffix);
    }

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
            _ => OffText
        };
    }

    /// <summary>The value printed where a channel does not use that edge at all.</summary>
    public const string OffText = "Off";

    /// <summary>
    /// The high-pass (resp. low-pass) edge as a value of its own — family, slope and
    /// corner, without naming which edge it is. The PDF sheet gives each edge a row of
    /// its own, so the row label already says that; a channel whose crossover kind does
    /// not use the edge prints <see cref="OffText"/>, which is itself a setting to dial
    /// in rather than a blank to skip over.
    /// </summary>
    public static string DescribeHighPass(VirtualCrossoverChannelSettings channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.CrossoverKind is CrossoverKind.HighPass or CrossoverKind.BandPass
            ? DescribeEdgeSettings(channel.HighPassEdge)
            : OffText;
    }

    /// <inheritdoc cref="DescribeHighPass"/>
    public static string DescribeLowPass(VirtualCrossoverChannelSettings channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.CrossoverKind is CrossoverKind.LowPass or CrossoverKind.BandPass
            ? DescribeEdgeSettings(channel.LowPassEdge)
            : OffText;
    }

    private static string DescribeEdge(string kind, CrossoverEdge edge) =>
        $"{kind} {DescribeEdgeSettings(edge)}";

    private static string DescribeEdgeSettings(CrossoverEdge edge)
    {
        string family = edge.Family switch
        {
            CrossoverFilterFamily.LinkwitzRiley => "Linkwitz-Riley",
            CrossoverFilterFamily.Bessel => "Bessel",
            CrossoverFilterFamily.Chebyshev => "Chebyshev",
            _ => "Butterworth"
        };
        string ripple = edge.Family == CrossoverFilterFamily.Chebyshev
            ? $" ({Number(edge.RippleDb, "0.#")} dB ripple)"
            : string.Empty;
        return $"{family} {edge.SlopeDbPerOctave} dB/oct @ " +
            $"{Number(edge.FrequencyHz, "0.###")} Hz{ripple}";
    }

    internal static string Signed(double value) =>
        SheetFormat.Signed(value);

    internal static string Number(double value, string format) =>
        SheetFormat.Number(value, format);
}
