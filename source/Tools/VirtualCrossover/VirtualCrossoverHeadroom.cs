using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One side's headroom: 0 dB less the chain's peak gain in the audio band. Negative clips a full-scale signal.
/// <see cref="Pending"/>: a FIR chain still being read, no figure yet.</summary>
internal readonly record struct HeadroomReading(double HeadroomDb, double PeakHz, bool Bypass, bool Pending = false)
{
    public bool Clips => !Pending && IsClip(HeadroomDb);

    /// <summary>On the figure as shown (a tenth of a dB), so the colour always agrees with its sign and float noise at
    /// 0 dB stays green. Every headroom read-out colours by it.</summary>
    public static bool IsClip(double headroomDb) => Math.Round(headroomDb, 1, MidpointRounding.AwayFromZero) < 0;
}

/// <summary>An enabled block's two sides; a mono block reads its left only, a side without a measurement reads null.</summary>
internal sealed record HeadroomRow(string Channel, bool Mono, HeadroomReading? Left, HeadroomReading? Right);

/// <summary>Headroom of every enabled block on both sides, whatever the view shows: the device clips on any output.
/// See docs/tech/virtual-dsp-analysis.md#headroom.</summary>
internal static class VirtualCrossoverHeadroom
{
    /// <summary>What <see cref="Read"/> needs, taken on the UI thread; null chains are sides without a measurement.</summary>
    internal sealed record Input(
        string Channel, bool Mono, bool Bypass, DspChainResponseKey? Left, DspChainResponseKey? Right);

    /// <summary>Every enabled block, measured or not: an unmeasured side reads —.</summary>
    public static List<Input> Capture(IReadOnlyList<VirtualCrossoverChannel> channels) =>
        [
            .. channels
                .Where(channel => channel.Pair.Enabled)
                .Select(channel => new Input(
                    channel.Name,
                    channel.Pair.Mono,
                    channel.Pair.Bypass,
                    SideKey(channel, rightSide: false),
                    channel.Pair.Mono ? null : SideKey(channel, rightSide: true)))
        ];

    /// <summary>The chain a side's output runs through, as the chain plot draws it; null without a measurement or a
    /// usable rate (it reads as unmeasured rather than failing the frame).</summary>
    public static DspChainResponseKey? SideKey(VirtualCrossoverChannel channel, bool rightSide) =>
        channel.SideState(rightSide).TransferImpulseResponse == null ||
        channel.ProcessorSampleRateFor(rightSide) <= 0
            ? null
            // The bulk delay cannot change a magnitude.
            : new DspChainResponseKey(
                channel.Pair.Bypass
                    ? DspChannelChain.Identity
                    : channel.Pair.ToChain(rightSide) with { DelayMs = 0 },
                channel.ProcessorSampleRateFor(rightSide));

    /// <summary>Reads through <paramref name="reader"/>, which then keeps only these chains; a FIR side not read yet is
    /// <see cref="HeadroomReading.Pending"/> until the reader's fill lands.</summary>
    public static List<HeadroomRow> Read(ChainHeadroomReader reader, IReadOnlyList<Input> inputs)
    {
        reader.Keep([.. inputs.SelectMany(input => new[] { input.Left, input.Right }).OfType<DspChainResponseKey>()]);
        return
        [
            .. inputs.Select(input => new HeadroomRow(
                input.Channel,
                input.Mono,
                Reading(reader, input.Left, input.Bypass),
                Reading(reader, input.Right, input.Bypass)))
        ];
    }

    private static HeadroomReading? Reading(ChainHeadroomReader reader, DspChainResponseKey? side, bool bypass) =>
        side == null
            ? null
            : reader.Peak(side) is { } peak
                ? new HeadroomReading(-peak.PeakDb, peak.PeakHz, bypass)
                : new HeadroomReading(0, 0, bypass, Pending: true);

    /// <summary>The runs of a curve above 0 dB, each opening and closing on 0 dB at its interpolated crossings, for the
    /// chain plot's fill; a curve that never rises above 0 dB has none.</summary>
    public static List<List<DataPoint>> OverUnity(IReadOnlyList<DataPoint> points)
    {
        var runs = new List<List<DataPoint>>();
        List<DataPoint>? run = null;
        for (int i = 0; i < points.Count; i++)
        {
            bool over = points[i].Y > 0;
            if (i > 0 && over != points[i - 1].Y > 0)
            {
                DataPoint crossing = Crossing(points[i - 1], points[i]);
                if (over)
                {
                    run = [crossing];
                    runs.Add(run);
                }
                else
                {
                    run!.Add(crossing);
                    run = null;
                }
            }
            else if (over && run == null)
            {
                run = [];
                runs.Add(run);
            }

            if (over)
            {
                run!.Add(points[i]);
            }
        }

        return runs;
    }

    // Linear in log frequency, as the axis draws the segment.
    private static DataPoint Crossing(DataPoint a, DataPoint b)
    {
        double t = a.Y / (a.Y - b.Y);
        return new DataPoint(Math.Exp(Math.Log(a.X) + t * (Math.Log(b.X) - Math.Log(a.X))), 0);
    }

    /// <summary>The read-out column's block: a row per block, L and R cells green or red.</summary>
    public static List<ToneLine> FormatCompact(IReadOnlyList<HeadroomRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var lines = new List<ToneLine>
        {
            ToneLine.Of("Headroom (dB)"),
            ToneLine.Of("          L      R")
        };
        foreach (HeadroomRow row in rows)
        {
            lines.Add(new ToneLine(
            [
                new ToneSpan(row.Channel.PadRight(4)),
                Cell(row.Left),
                row.Mono ? new ToneSpan("   mono") : Cell(row.Right)
            ]));
        }

        return lines;
    }

    private static ToneSpan Cell(HeadroomReading? reading) =>
        reading switch
        {
            null => new ToneSpan("      —"),
            { Bypass: true } => new ToneSpan("    byp", TextTone.Good),
            { Pending: true } => new ToneSpan("      …"),
            { } value => new ToneSpan(
                $"{value.HeadroomDb,7:+0.0;-0.0;0.0}", value.Clips ? TextTone.Bad : TextTone.Good)
        };

    public static string FormatDetail(IReadOnlyList<HeadroomRow> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { "Headroom: 0 dB less the chain's peak gain, 20 Hz – 20 kHz; below 0 clips. Peak at:" };
        foreach (HeadroomRow row in rows)
        {
            List<string> sides = row.Mono
                ? [Peak(row.Left)]
                : [
                    .. new[] { ("L", row.Left), ("R", row.Right) }
                        .Where(side => side.Item2 != null)
                        .Select(side => $"{side.Item1} {Peak(side.Item2)}")
                ];
            lines.Add($"{row.Channel}: {(sides.Count > 0 ? string.Join(", ", sides) : "—")}");
        }

        return string.Join("\r\n", lines);
    }

    private static string Peak(HeadroomReading? reading) =>
        reading switch
        {
            null => "—",
            { Bypass: true } => "bypassed",
            { Pending: true } => "…",
            { } value => FrequencyText.Format(value.PeakHz)
        };
}
