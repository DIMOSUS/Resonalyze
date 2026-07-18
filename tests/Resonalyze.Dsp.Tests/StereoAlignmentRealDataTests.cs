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

    private static CrossoverEdge Bw(double frequencyHz, int slopeDbPerOctave = 24) =>
        new(CrossoverFilterFamily.Butterworth, frequencyHz, slopeDbPerOctave);

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
                    channel.SampleRate,
                    out int validSampleCount);
                hit = new AlignmentSnapshot(
                    channel, processed,
                    VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    validSampleCount);
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

        // The polarity regression the user hit: two IDENTICAL tweeters measured at
        // 90° off-axis. Up in the bridge band (1.8-12 kHz) the two spatially-separated
        // tops comb-filter, so normal and inverted sum within a fraction of a dB (the
        // captured run was a 0.15 dB near-tie), and the fragile first-lobe sign read
        // disagreed (L Negative / R Positive) — which used to invert the RIGHT tweeter
        // alone. A near-tie must keep the tops matched: the tweeters stay
        // non-inverted, and every driver pair keeps ONE polarity. Lower down
        // the cascade may legitimately flip whole pairs (this cabin's
        // sub/woofer junction is genuinely inverted — whitened trough
        // r -0.97, position stable across crossover configs — so the woofers
        // flip relative to the sub), but never one side alone.
        Assert.False(alignment.GetValueOrDefault(leftTwr).InvertPolarity);
        Assert.False(alignment.GetValueOrDefault(rightTwr).InvertPolarity);
        Assert.Equal(
            alignment.GetValueOrDefault(leftWoof).InvertPolarity,
            alignment.GetValueOrDefault(rightWoof).InvertPolarity);
        Assert.Equal(
            alignment.GetValueOrDefault(leftMid).InvertPolarity,
            alignment.GetValueOrDefault(rightMid).InvertPolarity);
        Assert.True(
            alignment.GetValueOrDefault(leftWoof).InvertPolarity !=
            alignment.GetValueOrDefault(sub).InvertPolarity,
            "The sub/woofer junction lost its inverted near-arrival lobe.");

        // The attack contract at the shared sub junction: with the final
        // delays applied, the sub's band-limited arrival stays within a
        // cabin-width of the left woofer's — the lobe families either side
        // sit 3.5-5 ms out and audibly detach the bass from the kick.
        {
            IReadOnlyList<AlignmentSnapshot> settledFinal = Reprocess(alignment);
            double subAttackMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                settledFinal.First(item => item.Channel == sub).ImpulseResponse,
                sub.SampleRate, 40, 160);
            double woofAttackMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
                settledFinal.First(item => item.Channel == leftWoof).ImpulseResponse,
                leftWoof.SampleRate, 40, 160);
            Assert.InRange(subAttackMs - woofAttackMs, -2.5, 2.5);
        }

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

        // The scene mandate: the mids live squarely in the localization
        // region, so their pair is PINNED to the scene offset (±0.05 ms plus
        // the arrival detector's ~0.06 ms repeatability) — the field failure
        // had them drifting 0.43 ms left of the target because nothing
        // constrained the descent below the bridge.
        double leftMidArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == leftMid).ImpulseResponse,
            leftMid.SampleRate, 175, 1_300);
        double rightMidArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == rightMid).ImpulseResponse,
            rightMid.SampleRate, 175, 1_300);
        Assert.InRange(leftMidArrival - rightMidArrival, 0.10, 0.40);

        // The scene-preserving co-move may shift a pair, but never its L−R
        // timing: the tweeter pair still reads the scene offset at the end.
        double leftTwrArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == leftTwr).ImpulseResponse,
            leftTwr.SampleRate, BridgeBandLowHz, BridgeBandHighHz);
        double rightTwrArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final.First(item => item.Channel == rightTwr).ImpulseResponse,
            rightTwr.SampleRate, BridgeBandLowHz, BridgeBandHighHz);
        Assert.InRange(leftTwrArrival - rightTwrArrival, 0.15, 0.35);
    }

    [Fact]
    public void Compute_RealCabin_SubJunctionKeepsTheInvertedLobeNearTheArrival()
    {
        // The field failure of 2026-07-17 verbatim: the user's 4-way left side
        // (sub LP 80 Bw24, woof 80 Bw24 / 220 Bw36, mid 200 Bw24 / 1500 Bw36
        // at -3.5 dB, twr HP 1900 Bw36 at -7.5 dB). At the 80 Hz sub/woofer
        // junction the whitened correlation is decisively inverted near the
        // arrival (trough r -0.97 at -0.28 ms). The engine that shipped the
        // failure sent the junction to the arrival fallback plus a
        // period-wide window, where a non-inverted lobe 4.98 ms out came
        // within 0.03 dB of the true one and the invert preference swapped
        // onto it — parking the sub 5 ms behind the woofer on the impulse
        // view. The symmetric seed gate now trusts the dominant trough's
        // POSITION outright: the stage-2 window stays narrow around the
        // measured lobe and the far alternatives never become candidates.
        Channel Load(string file, string name, DspChannelChain chain)
        {
            (int rate, Complex[] ir) = LoadTransferIr(file);
            return new Channel(name, rate, ir, chain);
        }

        Channel sub = Load("sub woof closed window.json", "sub", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.LowPass, Bw(80))));
        Channel woof = Load("l woof.json", "L woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(220, 36), Bw(80))));
        Channel mid = Load("l mid.json", "L mid", new DspChannelChain(
            GainDb: -3.5,
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(1_500, 36), Bw(200))));
        Channel twr = Load("l twr.json", "L twr", new DspChannelChain(
            GainDb: -7.5,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(1_900, 36))));

        Channel[] byBand = [sub, woof, mid, twr];
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
                    channel.SampleRate,
                    out int validSampleCount);
                hit = new AlignmentSnapshot(
                    channel, processed,
                    VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    validSampleCount);
                cache[key] = hit;
            }

            return hit;
        }

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            byBand.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();

        List<AlignmentSnapshot> snapshots = byBand
            .Select(channel => Snapshot(channel, default))
            .ToList();
        double[] crossovers = [80, 220, 1_500];
        List<AlignmentJunction> junctions = crossovers
            .Select((fc, i) => new AlignmentJunction(
                snapshots[i], snapshots[i + 1], fc,
                Math.Max(20, fc / 2), Math.Min(20_000, fc * 2)))
            .ToList();

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        AutoAlignmentEngine.Compute(snapshots, junctions, Reprocess, alignment, log);

        // The dominant trough is trusted as the seed — no arrival fallback, no
        // widened window at this junction.
        string pairLine = TestLog.Line(log.ToString(), "Pair sub/");
        Assert.Contains("phat trough", pairLine);
        Assert.Contains("-> seed phat", pairLine);

        // The sub junction flips (relative inversion between sub and woofer)
        // — and the flip stays contained there: the chain above the woofer
        // keeps the woofer's polarity instead of rippling the inversion
        // upward.
        Assert.True(
            alignment.GetValueOrDefault(woof).InvertPolarity !=
            alignment.GetValueOrDefault(sub).InvertPolarity,
            "The sub/woofer junction lost its inverted near-arrival lobe.");
        Assert.Equal(
            alignment.GetValueOrDefault(mid).InvertPolarity,
            alignment.GetValueOrDefault(woof).InvertPolarity);
        Assert.Equal(
            alignment.GetValueOrDefault(twr).InvertPolarity,
            alignment.GetValueOrDefault(woof).InvertPolarity);

        // THE regression: with the final delays applied, the sub's band-limited
        // arrival stays within a cabin-width of the woofer's (the fix lands at
        // +0.79 ms; the failure sat at +4.98, the mirrored lobe at -5.75).
        IReadOnlyList<AlignmentSnapshot> final = Reprocess(alignment);
        double subArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final[0].ImpulseResponse, sub.SampleRate, 40, 160);
        double woofArrival = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            final[1].ImpulseResponse, woof.SampleRate, 40, 160);
        Assert.InRange(subArrival - woofArrival, -2.5, 2.5);

        // Still a physically realizable, normalized field.
        Assert.All(byBand, channel =>
            Assert.True(alignment.GetValueOrDefault(channel).DelayMs >= 0));
    }

    [Fact]
    public void ComputeStereo_RealCabin_LowJunctionKeepsTheSubAttackCoherent()
    {
        // The hardest sub/woofer configuration of this cabin (woofer/mid split
        // at 220 Hz, all Bw24): the woofer's band-limited arrival in 40-160 Hz
        // is BISTABLE — 11.5 ms here (the early front) vs 21.1 ms under the
        // Bw36 field config (the modal build-up), a 9.6 ms swing on the same
        // driver — so the pair's arrival diff is garbage and the dominant
        // whitened trough (r -0.97, position stable across every config of
        // this cabin) sits beyond the seed's arrival reach: the junction
        // honestly falls back to the arrival plus a period-wide window. In
        // that window the true inverted lobe 0.54 ms from the prior and a
        // non-inverted lobe 3.50 ms out score within 0.04 dB — fractions of a
        // dB cannot choose a lobe, so the polarity-agnostic envelope
        // tie-break must take the near one, keeping the sub's attack glued to
        // the woofers. (An earlier revision of this test pinned NON-inverted
        // woofers here; that sign was the coin toss coming up lucky-looking —
        // the 3.50 ms lobe — while the genuinely inverted junction lost 3.5 ms
        // of bass attack. Polarity is the loss search's business; the attack
        // is the contract.) Every driver pair still keeps ONE polarity.
        Channel Load(string file, string name, DspChannelChain chain)
        {
            (int rate, Complex[] ir) = LoadTransferIr(file);
            return new Channel(name, rate, ir, chain);
        }

        const double WoofMidHz = 220;
        const double MidTwrHz = 1_850;
        Channel sub = Load("sub woof closed window.json", "sub", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.LowPass, Bw(80))));
        Channel leftWoof = Load("l woof.json", "L woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(WoofMidHz), Bw(80))));
        Channel leftMid = Load("l mid.json", "L mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(MidTwrHz), Bw(WoofMidHz))));
        Channel leftTwr = Load("l twr.json", "L twr", new DspChannelChain(
            GainDb: -5,
            Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: Bw(MidTwrHz))));
        Channel rightWoof = Load("r woof.json", "R woof", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(WoofMidHz), Bw(80))));
        Channel rightMid = Load("r mid.json", "R mid", new DspChannelChain(
            Crossover: new CrossoverSpec(CrossoverKind.BandPass, Bw(MidTwrHz), Bw(WoofMidHz))));
        Channel rightTwr = Load("r twr.json", "R twr", new DspChannelChain(
            GainDb: -5,
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
                    channel.SampleRate,
                    out int validSampleCount);
                hit = new AlignmentSnapshot(
                    channel, processed,
                    VirtualCrossoverAnalysis.FindPeakIndex(processed),
                    validSampleCount);
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
        const double BridgeBandLowHz = 1_850;
        const double BridgeBandHighHz = 12_000;
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

        // The direct contract: the sub's attack stays within a cabin-width of
        // the left woofer's — the competing lobe families sit 3.5-5 ms out.
        IReadOnlyList<AlignmentSnapshot> settledFinal = Reprocess(alignment);
        double subAttackMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            settledFinal.First(item => item.Channel == sub).ImpulseResponse,
            sub.SampleRate, 40, 160);
        double woofAttackMs = VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            settledFinal.First(item => item.Channel == leftWoof).ImpulseResponse,
            leftWoof.SampleRate, 40, 160);
        Assert.InRange(subAttackMs - woofAttackMs, -2.5, 2.5);

        // The absolute contract regardless of the crossover: automatic delay
        // never inverts one side of a driver pair alone.
        (Channel Left, Channel Right)[] driverPairs =
        [
            (leftWoof, rightWoof),
            (leftMid, rightMid),
            (leftTwr, rightTwr)
        ];
        Assert.All(driverPairs, pair => Assert.Equal(
            alignment.GetValueOrDefault(pair.Left).InvertPolarity,
            alignment.GetValueOrDefault(pair.Right).InvertPolarity));

        // Still a physically realizable, normalized field.
        Assert.All(all, channel =>
            Assert.True(alignment.GetValueOrDefault(channel).DelayMs >= 0));
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
