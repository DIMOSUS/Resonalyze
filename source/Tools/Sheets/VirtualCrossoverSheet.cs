using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The Virtual DSP settings as the plain-text list a user types into the DSP.</summary>
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
        // Named on every sheet: a silent sheet cannot be told from an RBJ one, and a wrong convention measures wider.
        builder.AppendLine($"PEQ Q convention: {PeqQConventions.Describe(qConvention)}");
        if (!string.IsNullOrWhiteSpace(metricLine))
        {
            builder.AppendLine(metricLine);
        }

        // One run per zone in DSP typing order; a single-zone project stays flat.
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
            // Many DSPs have no separate EQ preamp; the combined figure rides on the same line so it is not read as a second gain.
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
            // Only where dialled in: a "Phase 0" line sends the reader looking for a knob the device may not have.
            if (channel.PhaseRotationDegrees > 0)
            {
                builder.AppendLine(
                    $"  Phase      {Number(channel.PhaseRotationDegrees, "0.###")}°");
            }
            if (channel.HasFir)
            {
                builder.AppendLine($"  FIR        {DescribeFir(channel)}");
            }
            if (channel.PeqBands.Count > 0 || channel.PeqPreampDb != 0)
            {
                builder.AppendLine(
                    $"  PEQ        {channel.PeqSourceName ?? "custom"}, " +
                    $"preamp {Signed(channel.PeqPreampDb)} dB");
                for (int band = 0; band < channel.PeqBands.Count; band++)
                {
                    PeqBand peq = PeqQConventions.ToConvention(
                        channel.PeqBands[band], qConvention);
                    // Keyword from the profile writer: a shelf printed as PK dials in the wrong filter.
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

    // Spelled out (read in a car on a phone); constants because consumers switch on the suffix.
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

    public const string OffText = "Off";

    /// <summary>Imported kernel: file name and length (to verify the loaded file); designed kernel: its crossover. Empty without one.</summary>
    public static string DescribeFir(VirtualCrossoverChannelSettings channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return channel.Fir is { } fir
            ? channel.FirDesign is { } design
                ? FirCrossoverDescription.Long(design)
                : $"{channel.FirSourceName ?? "FIR"} ({fir.Length} taps)"
            : string.Empty;
    }

    /// <summary>The edge's family, slope and corner without naming the edge; an unused edge prints <see cref="OffText"/>, itself a setting.</summary>
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
