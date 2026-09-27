using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One side's headroom: 0 dB less the chain's peak gain in the audio band. Negative clips a full-scale signal.</summary>
internal readonly record struct HeadroomReading(double HeadroomDb, double PeakHz, bool Bypass)
{
    // On the figure as shown, so the colour always agrees with its sign and float noise at 0 dB stays green.
    public bool Clips => Math.Round(HeadroomDb, 1, MidpointRounding.AwayFromZero) < 0;
}

/// <summary>An enabled block's two sides; a mono block reads its left only, a side without a measurement reads null.</summary>
internal sealed record HeadroomRow(string Channel, bool Mono, HeadroomReading? Left, HeadroomReading? Right);

/// <summary>Headroom of every enabled block on both sides, whatever the view shows: the device clips on any output.
/// Remembers each chain's peak, so a frame re-reads only the chains an edit changed. See
/// docs/tech/virtual-dsp-analysis.md#headroom.</summary>
internal sealed class VirtualCrossoverHeadroom
{
    private const double LowHz = 20;
    private const double HighHz = 20_000;

    // Just under Nyquist, where a bilinear filter's response is still defined.
    private const double HighestRateFraction = 0.49;

    // Only the last read's chains: an older chain can hold a replaced FIR kernel and its caches.
    private readonly Dictionary<SideInput, (double PeakHz, double PeakDb)> peaks = [];

    /// <summary>What <see cref="Read"/> needs, taken on the UI thread; null chains are sides without a measurement.</summary>
    internal sealed record Input(string Channel, bool Mono, bool Bypass, SideInput? Left, SideInput? Right);

    /// <summary>Equal when the response is: the PEQ is compared band by band, not by instance.</summary>
    internal sealed record SideInput(DspChannelChain Chain, int ProcessorSampleRate)
    {
        public bool Equals(SideInput? other) =>
            other != null &&
            ProcessorSampleRate == other.ProcessorSampleRate &&
            Chain with { Peq = null } == other.Chain with { Peq = null } &&
            (Chain.Peq?.PreampDb ?? 0) == (other.Chain.Peq?.PreampDb ?? 0) &&
            (Chain.Peq?.Bands ?? []).SequenceEqual(other.Chain.Peq?.Bands ?? []);

        public override int GetHashCode() =>
            HashCode.Combine(Chain with { Peq = null }, ProcessorSampleRate, Chain.Peq?.Bands.Count ?? 0);
    }

    public static List<Input> Capture(IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        var inputs = new List<Input>();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            if (!channel.Pair.Enabled)
            {
                continue;
            }

            // A side without a rate cannot be realized; it reads as unmeasured rather than failing the frame.
            SideInput? Side(bool rightSide) =>
                channel.SideState(rightSide).TransferImpulseResponse == null ||
                channel.ProcessorSampleRateFor(rightSide) <= 0
                    ? null
                    // The bulk delay cannot change a magnitude.
                    : new SideInput(
                        channel.Pair.Bypass
                            ? DspChannelChain.Identity
                            : channel.Pair.ToChain(rightSide) with { DelayMs = 0 },
                        channel.ProcessorSampleRateFor(rightSide));

            SideInput? left = Side(rightSide: false);
            SideInput? right = channel.Pair.Mono ? null : Side(rightSide: true);
            if (left != null || right != null)
            {
                inputs.Add(new Input(channel.Name, channel.Pair.Mono, channel.Pair.Bypass, left, right));
            }
        }

        return inputs;
    }

    public List<HeadroomRow> Read(IReadOnlyList<Input> inputs)
    {
        List<HeadroomRow> rows =
        [
            .. inputs.Select(input => new HeadroomRow(
                input.Channel,
                input.Mono,
                Reading(input.Left, input.Bypass),
                Reading(input.Right, input.Bypass)))
        ];
        var live = inputs.SelectMany(input => new[] { input.Left, input.Right }).OfType<SideInput>().ToHashSet();
        lock (peaks)
        {
            foreach (SideInput stale in peaks.Keys.Where(key => !live.Contains(key)).ToList())
            {
                peaks.Remove(stale);
            }
        }

        return rows;
    }

    private HeadroomReading? Reading(SideInput? side, bool bypass)
    {
        if (side == null)
        {
            return null;
        }

        (double PeakHz, double PeakDb) peak;
        bool known;
        lock (peaks)
        {
            known = peaks.TryGetValue(side, out peak);
        }

        if (!known)
        {
            peak = DspChainPeak.Find(
                side.Chain,
                side.ProcessorSampleRate,
                LowHz,
                Math.Min(HighHz, HighestRateFraction * side.ProcessorSampleRate));
            lock (peaks)
            {
                peaks[side] = peak;
            }
        }

        return new HeadroomReading(-peak.PeakDb, peak.PeakHz, bypass);
    }

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
            IEnumerable<string> sides = row.Mono
                ? [Peak(row.Left)]
                : new[] { ("L", row.Left), ("R", row.Right) }
                    .Where(side => side.Item2 != null)
                    .Select(side => $"{side.Item1} {Peak(side.Item2)}");
            lines.Add($"{row.Channel}: {string.Join(", ", sides)}");
        }

        return string.Join("\r\n", lines);
    }

    private static string Peak(HeadroomReading? reading) =>
        reading is { Bypass: false } value ? FrequencyText.Format(value.PeakHz) : "bypassed";
}
