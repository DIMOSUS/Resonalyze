using System.Numerics;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The field regressions the user hit on the real cabin measurements — three
/// doors into one failure: (a) the wide-window promotion unseated the
/// tweeter's arrival-anchored pick with a comb ALIAS ~1.7 ms (nearly four
/// crossover periods) away for a 0.25 dB summation "gain" (2300 Hz split);
/// (b) with the promotion capped, the scene-preserving co-move walked the
/// tweeter pair up to a period off its mid for 0.1-1 dB of mean junction
/// loss; (c) at a 1500 Hz split the promotion fired legitimately but hopped
/// onto the deepest-summing lobe a full period past the physically correct
/// one, because the band-limited arrival anchor itself carries a ~0.3-0.4 ms
/// envelope-rise bias at these junctions. All three are the same physics: on
/// a high junction the summation surface is a comb of near-equal minima and
/// fractions of a dB cannot choose a lobe — only the drivers' FRONTS can.
/// The onset lock closes every door at once: the search window is pinned to
/// the broadband threshold-onset anchor (bias-free where the front is sharp)
/// and the retry/promotion escape hatches stay shut. The contract here: mid
/// and tweeter fronts land within the lock's reach of each other, at BOTH
/// splits, for BOTH field gain configs, on BOTH sides.
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
    // alias, 0 dB exposed the co-move door once the promotion was capped. The
    // 2300 Hz split is the original failure cabin config; the 1500 Hz split
    // reproduces the later lobe-hop failure one octave down. The alignment
    // must not depend on the tweeter's gain at all — the fronts are where
    // they are.
    [Theory]
    [InlineData(2_300, 200, 2_300, -5.0)]
    [InlineData(2_300, 200, 2_300, 0.0)]
    [InlineData(1_500, 220, 1_900, -5.0)]
    [InlineData(1_500, 220, 1_900, 0.0)]
    public void ComputeStereo_RealCabin_OnsetLockKeepsMidAndTweeterFrontsTogether(
        double midTwrHz,
        double woofMidHz,
        double bridgeBandLowHz,
        double tweeterGainDb)
    {
        StereoRun run = RunRealCabin(woofMidHz, midTwrHz, bridgeBandLowHz, tweeterGainDb);

        output.WriteLine(run.Log);
        output.WriteLine(run.GapReport);

        // The contract: the mid and tweeter FRONTS (broadband threshold
        // onsets, the feature a human validates on the IR plot) sit within
        // the onset lock's reach of each other. The pre-lock failures opened
        // 0.7-1.7 ms cross-lobe front gaps here.
        double onePeriodMs = 1_000.0 / midTwrHz;
        Assert.InRange(Math.Abs(run.LeftFrontGapMs), 0, 0.75 * onePeriodMs);
        Assert.InRange(Math.Abs(run.RightFrontGapMs), 0, 0.75 * onePeriodMs);

        // Independent cross-check with a DIFFERENT detector: the front gaps
        // above are measured by the same onset estimator that anchored the
        // lock, so alone they only prove self-consistency. The octave-band
        // envelope arrival is a separate observable; it carries its own
        // ~0.45-0.8-period rise-time bias at these junctions (see
        // EstimateBroadbandOnset docs), so its gap is asserted loosely — but a
        // whole-cycle slip the onset estimator somehow agreed with itself on
        // (≥ 2 periods) cannot hide from it.
        Assert.InRange(Math.Abs(run.LeftBandArrivalGapMs), 0, 1.5 * onePeriodMs);
        Assert.InRange(Math.Abs(run.RightBandArrivalGapMs), 0, 1.5 * onePeriodMs);

        // The mechanism pin: the mid/tweeter junction must actually have been
        // onset-locked (sharp fronts, spread gate passed) — a silently
        // disengaged lock would hand the junction back to the comb.
        Assert.Contains("ONSET-LOCKED", run.Log);
        Assert.Contains("onset gap after", run.Log);
    }

    private sealed record StereoRun(
        string Log,
        double LeftFrontGapMs,
        double RightFrontGapMs,
        double LeftBandArrivalGapMs,
        double RightBandArrivalGapMs,
        string GapReport);

    // Runs the full stereo auto-alignment on the real cabin IRs with the
    // crossovers placed by the caller, and measures the final mid/tweeter
    // front gaps from the broadband onsets of the aligned IRs.
    private StereoRun RunRealCabin(
        double woofMidHz,
        double midTwrHz,
        double bridgeBandLowHz,
        double tweeterGainDb)
    {
        Channel Load(string file, string name, DspChannelChain chain)
        {
            (int rate, Complex[] ir) = LoadTransferIr(file);
            return new Channel(name, rate, ir, chain);
        }

        Channel sub = Load("sub woof closed window.json", "sub", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.LowPass, Bw(80))));
        Channel leftWoof = Load("l woof.json", "L woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(woofMidHz), Bw(80))));
        Channel leftMid = Load("l mid.json", "L mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(midTwrHz), Bw(woofMidHz))));
        Channel leftTwr = Load("l twr.json", "L twr", new DspChannelChain(
            GainDb: tweeterGainDb,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(midTwrHz))));
        Channel rightWoof = Load("r woof.json", "R woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(woofMidHz), Bw(80))));
        Channel rightMid = Load("r mid.json", "R mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(midTwrHz), Bw(woofMidHz))));
        Channel rightTwr = Load("r twr.json", "R twr", new DspChannelChain(
            GainDb: tweeterGainDb,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(midTwrHz))));

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
        double[] crossovers = [80, woofMidHz, midTwrHz];
        List<AlignmentJunction> leftPairs = crossovers
            .Select((fc, i) => Junction(leftSnapshots[i], leftSnapshots[i + 1], fc))
            .ToList();
        List<AlignmentJunction> rightPairs = crossovers
            .Select((fc, i) => Junction(rightSnapshots[i], rightSnapshots[i + 1], fc))
            .ToList();

        const double SceneOffsetMs = 0.25;
        const double BridgeBandHighHz = 20_000;
        List<StereoPairLink> pairLinks =
        [
            new(leftWoof, rightWoof, 80, woofMidHz),
            new(leftMid, rightMid, woofMidHz, midTwrHz),
            new(leftTwr, rightTwr, bridgeBandLowHz, BridgeBandHighHz)
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
                bridgeBandLowHz,
                BridgeBandHighHz,
                SceneOffsetMs,
                pairLinks),
            Reprocess,
            alignment,
            log);

        IReadOnlyList<AlignmentSnapshot> final = Reprocess(alignment);
        double FrontGap(Channel mid, Channel twr, out double midMs, out double twrMs)
        {
            midMs = VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                final.First(item => ReferenceEquals(item.Channel, mid)).ImpulseResponse,
                mid.SampleRate).OnsetMs;
            twrMs = VirtualCrossoverAnalysis.EstimateBroadbandOnset(
                final.First(item => ReferenceEquals(item.Channel, twr)).ImpulseResponse,
                twr.SampleRate).OnsetMs;
            return midMs - twrMs;
        }

        double BandArrivalGap(Channel mid, Channel twr)
        {
            double midMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                final.First(item => ReferenceEquals(item.Channel, mid)).ImpulseResponse,
                mid.SampleRate, midTwrHz / 2, midTwrHz * 2);
            double twrMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                final.First(item => ReferenceEquals(item.Channel, twr)).ImpulseResponse,
                twr.SampleRate, midTwrHz / 2, midTwrHz * 2);
            return midMs - twrMs;
        }

        double leftGap = FrontGap(leftMid, leftTwr, out double lMid, out double lTwr);
        double rightGap = FrontGap(rightMid, rightTwr, out double rMid, out double rTwr);
        double leftBandGap = BandArrivalGap(leftMid, leftTwr);
        double rightBandGap = BandArrivalGap(rightMid, rightTwr);
        string gapReport =
            $"LEFT  mid front {lMid:0.000} / twr {lTwr:0.000} ms -> gap {leftGap:+0.000;-0.000} ms\n" +
            $"RIGHT mid front {rMid:0.000} / twr {rTwr:0.000} ms -> gap {rightGap:+0.000;-0.000} ms\n" +
            $"band arrival gaps: L {leftBandGap:+0.000;-0.000} / R {rightBandGap:+0.000;-0.000} ms\n" +
            $"delays: L mid {alignment.GetValueOrDefault(leftMid).DelayMs:0.00} " +
            $"L twr {alignment.GetValueOrDefault(leftTwr).DelayMs:0.00} | " +
            $"R mid {alignment.GetValueOrDefault(rightMid).DelayMs:0.00} " +
            $"R twr {alignment.GetValueOrDefault(rightTwr).DelayMs:0.00}";

        return new StereoRun(
            log.ToString(), leftGap, rightGap, leftBandGap, rightBandGap, gapReport);
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
