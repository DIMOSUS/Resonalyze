using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The stereo cascade on the real measurements in <c>assets/test_data</c>:
/// the user's left 4-way (shared mono sub) bridged to the right 3-way at the
/// tweeter pair with the 0.25 ms dash-center scene offset. Pins the contract
/// the synthetic tests cannot: on real cabin acoustics the bridge lands the
/// right tweeter's band-limited arrival exactly the offset behind the left
/// one, the mono sub keeps its left-pass timing, and no delay is negative.
/// </summary>
public sealed class StereoAlignmentRealDataTests
{
    private sealed record Channel(
        string Name,
        int SampleRate,
        Complex[] RawIr,
        DspChannelChain BaseChain) : IAlignmentChannel;

    private static CrossoverEdge Bw(double frequencyHz) =>
        new(CrossoverFilterFamily.Butterworth, frequencyHz, 24);

    [Fact]
    public void ComputeStereo_RealCabin_BridgesTheTweetersAndKeepsTheMonoSub()
    {
        Channel Load(string file, string name, DspChannelChain chain)
        {
            (int rate, Complex[] ir) = LoadTransferIr(file);
            return new Channel(name, rate, ir, chain);
        }

        Channel sub = Load("sub woof closed window.json", "sub", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.LowPass, Bw(80))));
        Channel leftWoof = Load("l woof.json", "L woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(175), Bw(80))));
        Channel leftMid = Load("l mid.json", "L mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(1_300), Bw(175))));
        Channel leftTwr = Load("l twr.json", "L twr", new DspChannelChain(
            GainDb: -5,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(1_800))));
        Channel rightWoof = Load("r woof.json", "R woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(175), Bw(80))));
        Channel rightMid = Load("r mid.json", "R mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(1_300), Bw(175))));
        Channel rightTwr = Load("r twr.json", "R twr", new DspChannelChain(
            GainDb: -5,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(1_800))));

        Channel[] all = [sub, leftWoof, leftMid, leftTwr, rightWoof, rightMid, rightTwr];
        Channel[] leftByBand = [sub, leftWoof, leftMid, leftTwr];
        Channel[] rightByBand = [sub, rightWoof, rightMid, rightTwr];

        // The engine re-evaluates the same override maps many times across the
        // junction walks; caching processed IRs keeps the real-data run fast.
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
        double[] crossovers = [80, 175, 1_300];
        List<AlignmentJunction> leftPairs = crossovers
            .Select((fc, i) => Junction(leftSnapshots[i], leftSnapshots[i + 1], fc))
            .ToList();
        List<AlignmentJunction> rightPairs = crossovers
            .Select((fc, i) => Junction(rightSnapshots[i], rightSnapshots[i + 1], fc))
            .ToList();

        const double SceneOffsetMs = 0.25;
        const double BridgeBandLowHz = 1_800;
        const double BridgeBandHighHz = 12_000;
        List<StereoPairLink> pairLinks =
        [
            new(leftWoof, rightWoof, 80, 175),
            new(leftMid, rightMid, 175, 1_300),
            new(leftTwr, rightTwr, 1_800, 12_000)
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

        // Every proposal is a physically realizable non-negative delay and the
        // field is normalized: something sits at zero.
        Assert.All(all, channel =>
            Assert.True(alignment.GetValueOrDefault(channel).DelayMs >= 0));
        Assert.InRange(
            all.Min(channel => alignment.GetValueOrDefault(channel).DelayMs),
            0, 0.011);

        // The bridge contract on real acoustics: with the final delays applied,
        // the right tweeter's band-limited arrival trails the left one by the
        // scene offset (right LEADS by 0.25 ms). Tolerance covers the 0.01 ms
        // delay rounding and the arrival detector's repeatability (~0.06 ms on
        // these measurements).
        IReadOnlyList<AlignmentSnapshot> final = Reprocess(alignment);
        double leftArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == leftTwr).ImpulseResponse,
            leftTwr.SampleRate, BridgeBandLowHz, BridgeBandHighHz);
        double rightArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == rightTwr).ImpulseResponse,
            rightTwr.SampleRate, BridgeBandLowHz, BridgeBandHighHz);
        Assert.InRange(leftArrival - rightArrival, 0.15, 0.35);

        // The mono sub is timed by the left side alone: its delay relative to
        // the left woofer must match a left-only engine run bit for bit (both
        // runs may differ by uniform shifts only).
        var leftOnly = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            leftSnapshots, leftPairs, Reprocess, leftOnly, new StringBuilder());
        double stereoRelative = alignment.GetValueOrDefault(sub).DelayMs
            - alignment.GetValueOrDefault(leftWoof).DelayMs;
        double leftOnlyRelative = leftOnly.GetValueOrDefault(sub).DelayMs
            - leftOnly.GetValueOrDefault(leftWoof).DelayMs;
        Assert.InRange(Math.Abs(stereoRelative - leftOnlyRelative), 0, 0.011);

        // The pinned right sub junction is reported, not tuned.
        Assert.Contains("mono, timed by the left side", log.ToString());

        // The field regression this cabin exposed: the right woofer used to
        // optimize ONLY its junction toward the bridge and parked a whole
        // 175 Hz period off the shared sub (junction avg −4.9 dB, and 5.4 ms
        // away from the left woofer). With the joint two-neighbor search it
        // must sit within a cabin-width of the left woofer and keep the sub
        // handover healthy on BOTH sides.
        double leftWoofDelay = alignment.GetValueOrDefault(leftWoof).DelayMs;
        double rightWoofDelay = alignment.GetValueOrDefault(rightWoof).DelayMs;
        Assert.InRange(Math.Abs(leftWoofDelay - rightWoofDelay), 0, 1.5);

        (double LossDb, double DipDb)? rightSubJunction =
            VirtualCrossoverAnalysis.MeasureSumLoss(
                final.First(item => item.Channel == rightWoof).ImpulseResponse,
                new List<Complex[]>
                {
                    final.First(item => item.Channel == sub).ImpulseResponse
                },
                rightWoof.SampleRate, 40, 160);
        Assert.NotNull(rightSubJunction);
        Assert.InRange(rightSubJunction.Value.LossDb, -1.5, 0);
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
