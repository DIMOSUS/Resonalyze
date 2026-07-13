using System.Numerics;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The field regressions the user hit on the real cabin measurements with an
/// auto-crossover that split mid/tweeter at 2300 Hz — two doors into the same
/// failure: (a) the wide-window promotion unseated the tweeter's
/// arrival-anchored pick with a comb ALIAS ~1.7 ms (nearly four crossover
/// periods) away for a 0.25 dB summation "gain"; (b) with the promotion capped,
/// the scene-preserving co-move walked the tweeter pair up to a period off its
/// mid through its flat ±1.2 ms window for 0.1-1 dB of mean junction loss. Both
/// are the same physics: on a high junction the summation surface is a comb of
/// near-equal minima and fractions of a dB cannot choose a lobe — the arrival
/// can. With both reach caps, mid and tweeter stay on the SAME summation lobe
/// (band-limited arrivals within one crossover period of each other — the
/// residual is the crossover's own per-driver group delay, not a misalignment).
/// </summary>
public sealed class PromotionReachTests
{
    private readonly ITestOutputHelper output;

    public PromotionReachTests(ITestOutputHelper output) => this.output = output;

    private sealed record Channel(
        string Name,
        int SampleRate,
        Complex[] RawIr,
        DspChannelChain BaseChain) : IAlignmentChannel;

    private static CrossoverEdge Bw(double frequencyHz) =>
        new(CrossoverFilterFamily.Butterworth, frequencyHz, 24);

    // Both tweeter gains are real field configs: -5 dB exposed the promotion
    // alias, 0 dB exposed the co-move door once the promotion was capped.
    [Theory]
    [InlineData(-5.0)]
    [InlineData(0.0)]
    public void ComputeStereo_RealCabin_PromotionKeepsMidAndTweeterOnOneLobe(
        double tweeterGainDb)
    {
        Channel Load(string file, string name, DspChannelChain chain)
        {
            (int rate, Complex[] ir) = LoadTransferIr(file);
            return new Channel(name, rate, ir, chain);
        }

        const double WoofMidHz = 200;
        const double MidTwrHz = 2_300;
        Channel sub = Load("sub woof closed window.json", "sub", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.LowPass, Bw(80))));
        Channel leftWoof = Load("l woof.json", "L woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(WoofMidHz), Bw(80))));
        Channel leftMid = Load("l mid.json", "L mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(MidTwrHz), Bw(WoofMidHz))));
        Channel leftTwr = Load("l twr.json", "L twr", new DspChannelChain(
            GainDb: tweeterGainDb,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(MidTwrHz))));
        Channel rightWoof = Load("r woof.json", "R woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(WoofMidHz), Bw(80))));
        Channel rightMid = Load("r mid.json", "R mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(MidTwrHz), Bw(WoofMidHz))));
        Channel rightTwr = Load("r twr.json", "R twr", new DspChannelChain(
            GainDb: tweeterGainDb,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(MidTwrHz))));

        Channel[] all = [sub, leftWoof, leftMid, leftTwr, rightWoof, rightMid, rightTwr];
        Channel[] leftByBand = [sub, leftWoof, leftMid, leftTwr];
        Channel[] rightByBand = [sub, rightWoof, rightMid, rightTwr];

        var cache = new Dictionary<(Channel, double, bool), AlignmentSnapshot>();
        AlignmentSnapshot Snapshot(Channel channel, AlignmentOverride over)
        {
            (Channel, double, bool) key = (channel, over.DelayMs, over.InvertPolarity);
            if (!cache.TryGetValue(key, out AlignmentSnapshot? hit))
            {
                Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
                    channel.RawIr,
                    channel.BaseChain with
                    {
                        DelayMs = over.DelayMs,
                        InvertPolarity = over.InvertPolarity
                    },
                    channel.SampleRate);
                hit = new AlignmentSnapshot(
                    channel, processed, VirtualCrossoverAnalysis.FindPeakIndex(processed));
                cache[key] = hit;
            }

            return hit;
        }

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();

        List<AlignmentSnapshot> leftSnapshots = leftByBand
            .Select(channel => Snapshot(channel, default))
            .ToList();
        List<AlignmentSnapshot> rightSnapshots = rightByBand
            .Select(channel => Snapshot(channel, default))
            .ToList();
        AlignmentJunction Junction(
            AlignmentSnapshot lower, AlignmentSnapshot upper, double fc) =>
            new(lower, upper, fc, Math.Max(20, fc / 2), Math.Min(20_000, fc * 2));
        double[] crossovers = [80, WoofMidHz, MidTwrHz];
        List<AlignmentJunction> leftPairs = crossovers
            .Select((fc, i) => Junction(leftSnapshots[i], leftSnapshots[i + 1], fc))
            .ToList();
        List<AlignmentJunction> rightPairs = crossovers
            .Select((fc, i) => Junction(rightSnapshots[i], rightSnapshots[i + 1], fc))
            .ToList();

        const double SceneOffsetMs = 0.25;
        const double BridgeBandLowHz = 2_300;
        const double BridgeBandHighHz = 20_000;
        List<StereoPairLink> pairLinks =
        [
            new(leftWoof, rightWoof, 80, WoofMidHz),
            new(leftMid, rightMid, WoofMidHz, MidTwrHz),
            new(leftTwr, rightTwr, BridgeBandLowHz, BridgeBandHighHz)
        ];
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        AutoAlignmentEngine.ComputeStereo(
            new StereoAlignmentPlan(
                leftSnapshots,
                leftPairs,
                rightSnapshots,
                rightPairs,
                new HashSet<IAlignmentChannel> { sub },
                leftTwr,
                rightTwr,
                BridgeBandLowHz,
                BridgeBandHighHz,
                SceneOffsetMs,
                pairLinks),
            Reprocess,
            alignment,
            log);

        output.WriteLine(log.ToString());

        IReadOnlyList<AlignmentSnapshot> final = Reprocess(alignment);
        double MidTwrArrival(Channel mid, Channel twr, out double midMs, out double twrMs)
        {
            midMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                final.First(item => item.Channel == mid).ImpulseResponse,
                mid.SampleRate, 1_150, 4_600);
            twrMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                final.First(item => item.Channel == twr).ImpulseResponse,
                twr.SampleRate, 1_150, 4_600);
            return midMs - twrMs;
        }

        double leftGap = MidTwrArrival(leftMid, leftTwr, out double lMid, out double lTwr);
        double rightGap = MidTwrArrival(rightMid, rightTwr, out double rMid, out double rTwr);
        output.WriteLine(
            $"LEFT  mid {lMid:0.000} / twr {lTwr:0.000} ms -> C/D gap {leftGap:+0.000;-0.000} ms");
        output.WriteLine(
            $"RIGHT mid {rMid:0.000} / twr {rTwr:0.000} ms -> C/D gap {rightGap:+0.000;-0.000} ms");
        output.WriteLine(
            $"delays: L mid {alignment.GetValueOrDefault(leftMid).DelayMs:0.00} " +
            $"L twr {alignment.GetValueOrDefault(leftTwr).DelayMs:0.00} | " +
            $"R mid {alignment.GetValueOrDefault(rightMid).DelayMs:0.00} " +
            $"R twr {alignment.GetValueOrDefault(rightTwr).DelayMs:0.00}");

        // The contract: mid and tweeter land on the SAME summation lobe, i.e.
        // their band-limited arrivals sit within one crossover period of each
        // other (1000/2300 ≈ 0.43 ms). The bug promoted the tweeter a couple of
        // periods off, opening a >0.7 ms cross-lobe gap. The residual inside one
        // period is the crossover's inherent per-driver group delay, not a
        // misalignment.
        const double OnePeriodMs = 1_000.0 / MidTwrHz;
        Assert.InRange(Math.Abs(leftGap), 0, OnePeriodMs);
        Assert.InRange(Math.Abs(rightGap), 0, OnePeriodMs);

        // The promotion must not have walked the tweeter off the envelope: the
        // declined-alias diagnostic fires only when the reach cap rejects a
        // multi-period comb alias, which is exactly this cabin's failure mode.
        Assert.Contains("promotion declined", log.ToString());
    }

    private static (int SampleRate, Complex[] Ir) LoadTransferIr(string fileName)
    {
        string path = Path.Combine(FindTestDataDirectory(), fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{fileName} is missing - initialize the measurement-data " +
                "submodule with 'git submodule update --init assets/test_data'.",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        int sampleRate = root.GetProperty("sampleRate").GetInt32();
        JsonElement samples = root.GetProperty("transferRealSamples");
        var impulseResponse = new Complex[samples.GetArrayLength()];
        int index = 0;
        foreach (JsonElement sample in samples.EnumerateArray())
        {
            impulseResponse[index++] = new Complex(sample.GetDouble(), 0.0);
        }

        return (sampleRate, impulseResponse);
    }

    private static string FindTestDataDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "assets", "test_data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "assets/test_data was not found above the test binary.");
    }
}
