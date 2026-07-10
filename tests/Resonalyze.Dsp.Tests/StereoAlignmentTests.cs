using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The stereo alignment cascade: left side first, then the top-pair arrival
/// bridge with the scene offset (positive = the right side leads), then the
/// right-side descent that must not touch mono channels. The synthetic
/// systems place impulses at known positions, so every stage's contribution
/// is verifiable arithmetic.
/// </summary>
public sealed class StereoAlignmentTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 480; // 10 ms at 48 kHz.

    private sealed class TestChannel(string name, Complex[] ir) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => StereoAlignmentTests.SampleRate;
        public Complex[] Ir { get; } = ir;
    }

    private static Complex[] ImpulseAtMs(double offsetMs, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        int position = BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate);
        ir[position] = amplitude;
        return ir;
    }

    private static AlignmentSnapshot Snapshot(
        TestChannel channel, AlignmentOverride over)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            channel.Ir,
            new DspChannelChain(
                DelayMs: over.DelayMs, InvertPolarity: over.InvertPolarity),
            SampleRate);
        return new AlignmentSnapshot(
            channel, processed, VirtualCrossoverAnalysis.FindPeakIndex(processed));
    }

    private static AlignmentJunction Junction(
        AlignmentSnapshot lower, AlignmentSnapshot upper, double fc) =>
        new(lower, upper, fc, Math.Max(20, fc / 2), Math.Min(20_000, fc * 2));

    /// <summary>
    /// The user's shape of system: a shared mono sub, then woof/mid/twr per
    /// side. The left side sits at its base positions; the right side arrives
    /// 1.5 ms later across the board (the far side of the cabin).
    /// <paramref name="linkBands"/> (woof/mid/twr order, null entries skipped)
    /// turns on the L/R pair links; <paramref name="rightMidEchoMs"/> gives
    /// the right mid a second, stronger lobe that far behind its arrival (the
    /// scene-lock antagonist: correlation-driven searches chase the strong
    /// lobe, the scene follows the first one); <paramref name="leftLateMs"/>
    /// pushes the whole left side late (near-delay-ceiling scenarios);
    /// <paramref name="reprocessCount"/>[0] counts reprocess invocations.
    /// </summary>
    private static (TestChannel Sub,
        TestChannel[] Left, TestChannel[] Right,
        Dictionary<IAlignmentChannel, AlignmentOverride> Alignment,
        StringBuilder Log)
        RunStereo(
            double sceneOffsetMs,
            double rightLateMs = 1.5,
            double leftTopAmplitude = 1.0,
            double rightTopAmplitude = 1.0,
            (double LowHz, double HighHz)?[]? linkBands = null,
            double rightMidEchoMs = 0,
            double leftLateMs = 0,
            int[]? reprocessCount = null)
    {
        var sub = new TestChannel("sub", ImpulseAtMs(2.0 + leftLateMs));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(1.0 + leftLateMs));
        var leftMid = new TestChannel("L mid", ImpulseAtMs(0.4 + leftLateMs));
        var leftTwr = new TestChannel(
            "L twr", ImpulseAtMs(0.0 + leftLateMs, leftTopAmplitude));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(1.0 + rightLateMs));
        Complex[] rightMidIr = ImpulseAtMs(
            0.4 + rightLateMs, rightMidEchoMs > 0 ? 0.6 : 1.0);
        if (rightMidEchoMs > 0)
        {
            int echoPosition = BasePosition + (int)Math.Round(
                (0.4 + rightLateMs + rightMidEchoMs) / 1000.0 * SampleRate);
            rightMidIr[echoPosition] += Complex.One;
        }
        var rightMid = new TestChannel("R mid", rightMidIr);
        var rightTwr = new TestChannel(
            "R twr", ImpulseAtMs(0.0 + rightLateMs, rightTopAmplitude));

        TestChannel[] leftByBand = [sub, leftWoof, leftMid, leftTwr];
        TestChannel[] rightByBand = [sub, rightWoof, rightMid, rightTwr];
        TestChannel[] all = [sub, leftWoof, leftMid, leftTwr, rightWoof, rightMid, rightTwr];

        List<StereoPairLink>? pairLinks = null;
        if (linkBands != null)
        {
            (TestChannel Left, TestChannel Right)[] linkChannels =
                [(leftWoof, rightWoof), (leftMid, rightMid), (leftTwr, rightTwr)];
            pairLinks = new List<StereoPairLink>();
            for (int i = 0; i < linkBands.Length; i++)
            {
                if (linkBands[i] is { } band)
                {
                    pairLinks.Add(new StereoPairLink(
                        linkChannels[i].Left, linkChannels[i].Right,
                        band.LowHz, band.HighHz));
                }
            }
        }

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            if (reprocessCount != null)
            {
                reprocessCount[0]++;
            }

            return all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();
        }

        List<AlignmentSnapshot> initial = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        AlignmentSnapshot Of(TestChannel channel) =>
            initial.First(item => item.Channel == channel);

        List<AlignmentSnapshot> leftSnapshots =
            leftByBand.Select(Of).ToList();
        List<AlignmentSnapshot> rightSnapshots =
            rightByBand.Select(Of).ToList();
        double[] crossovers = [80, 400, 2_500];
        List<AlignmentJunction> leftPairs = crossovers
            .Select((fc, i) => Junction(leftSnapshots[i], leftSnapshots[i + 1], fc))
            .ToList();
        List<AlignmentJunction> rightPairs = crossovers
            .Select((fc, i) => Junction(rightSnapshots[i], rightSnapshots[i + 1], fc))
            .ToList();

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
                BridgeBandLowHz: 2_500,
                BridgeBandHighHz: 12_000,
                SceneOffsetMs: sceneOffsetMs,
                pairLinks),
            Reprocess,
            alignment,
            log);

        return (sub,
            [leftWoof, leftMid, leftTwr],
            [rightWoof, rightMid, rightTwr],
            alignment, log);
    }

    // The final arrival of a channel = its impulse position + proposed delay.
    private static double FinalArrivalMs(
        TestChannel channel,
        double naturalMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment) =>
        naturalMs + alignment.GetValueOrDefault(channel).DelayMs;

    [Fact]
    public void ComputeStereo_BridgeHonorsTheSceneOffsetSign()
    {
        // Positive offset: the right side must LEAD by 0.25 ms — its top
        // channel's final arrival is 0.25 ms EARLIER than the left top's.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25);

        double leftTop = FinalArrivalMs(left[2], 0.0, alignment);
        double rightTop = FinalArrivalMs(right[2], 1.5, alignment);
        Assert.InRange(leftTop - rightTop, 0.20, 0.30);
    }

    [Fact]
    public void ComputeStereo_NegativeOffsetLeadsTheLeftSide()
    {
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: -0.25);

        double leftTop = FinalArrivalMs(left[2], 0.0, alignment);
        double rightTop = FinalArrivalMs(right[2], 1.5, alignment);
        Assert.InRange(leftTop - rightTop, -0.30, -0.20);
    }

    [Fact]
    public void ComputeStereo_AlignsBothSidesInternallyAndKeepsDelaysNonNegative()
    {
        // Within each side every junction is a pure delay offset, so the
        // cascade must equalize the in-band arrivals side by side. The right
        // side is the far one: making it lead requires advancing it, which is
        // only expressible by shifting the left field up — the engine's
        // uniform-shift branch — and the minimum delay must land on zero.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25);

        // The right woofer sits between TWO settled references (the mono sub
        // below, its mid above) and optimizes both junctions jointly, so it
        // may split a small residual between them instead of hugging the
        // upper neighbor exactly — hence the slightly wider right-side
        // tolerance.
        double[] naturals = [1.0, 0.4, 0.0];
        for (int i = 0; i < 2; i++)
        {
            Assert.InRange(
                Math.Abs(
                    FinalArrivalMs(left[i], naturals[i], alignment) -
                    FinalArrivalMs(left[i + 1], naturals[i + 1], alignment)),
                0, 0.1);
            Assert.InRange(
                Math.Abs(
                    FinalArrivalMs(right[i], naturals[i] + 1.5, alignment) -
                    FinalArrivalMs(right[i + 1], naturals[i + 1] + 1.5, alignment)),
                0, 0.2);
        }

        double minimum = new[] { sub, left[0], left[1], left[2], right[0], right[1], right[2] }
            .Min(channel => alignment.GetValueOrDefault(channel).DelayMs);
        Assert.InRange(minimum, 0, 0.011);
        Assert.All(
            alignment.Values,
            over => Assert.True(over.DelayMs >= 0));
    }

    [Fact]
    public void ComputeStereo_MonoSubIsTimedByTheLeftPassOnly()
    {
        // The sub's delay must equal what a LEFT-ONLY run gives it, up to the
        // uniform shifts the stereo stages add on top of everyone: relative to
        // its left woofer neighbor the sub's timing must be identical in both
        // runs. The right pass may only measure its junction, never move it.
        (TestChannel sub, TestChannel[] left, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> stereo,
            StringBuilder log) = RunStereo(sceneOffsetMs: 0.25);

        // Left-only reference run on identical geometry.
        var subOnly = new TestChannel("sub", ImpulseAtMs(2.0));
        var woof = new TestChannel("L woof", ImpulseAtMs(1.0));
        var mid = new TestChannel("L mid", ImpulseAtMs(0.4));
        var twr = new TestChannel("L twr", ImpulseAtMs(0.0));
        TestChannel[] channels = [subOnly, woof, mid, twr];
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            channels.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();
        List<AlignmentSnapshot> snapshots = channels
            .Select(channel => Snapshot(channel, default))
            .ToList();
        double[] crossovers = [80, 400, 2_500];
        var monoAlignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        AutoAlignmentEngine.Compute(
            snapshots,
            crossovers.Select((fc, i) =>
                Junction(snapshots[i], snapshots[i + 1], fc)).ToList(),
            Reprocess,
            monoAlignment,
            new StringBuilder());

        double stereoRelative = stereo.GetValueOrDefault(sub).DelayMs
            - stereo.GetValueOrDefault(left[0]).DelayMs;
        double monoRelative = monoAlignment.GetValueOrDefault(subOnly).DelayMs
            - monoAlignment.GetValueOrDefault(woof).DelayMs;
        Assert.InRange(Math.Abs(stereoRelative - monoRelative), 0, 0.011);

        // The right pass reports the pinned junction instead of tuning it.
        Assert.Contains("mono, timed by the left side", log.ToString());
    }

    [Fact]
    public void ComputeStereo_BridgeMatchesTheRightTopPolarityToTheLeft()
    {
        // The right tweeter is wired backwards (negative impulse). The
        // arrival-based bridge is polarity-blind, so without the explicit sign
        // match the whole right side would align to a flipped reference and
        // end up in opposite global polarity — the bridge must invert it.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25, rightTopAmplitude: -1.0);

        Assert.False(alignment.GetValueOrDefault(left[2]).InvertPolarity);
        Assert.True(alignment.GetValueOrDefault(right[2]).InvertPolarity);
        // The descent then aligns the (positive) right mid against the
        // corrected top: no compensating flip below.
        Assert.False(alignment.GetValueOrDefault(right[1]).InvertPolarity);
    }

    [Fact]
    public void ComputeStereo_BridgeFollowsAnInvertedLeftTop()
    {
        // BOTH tops are wired backwards. The left walk inverts the left top at
        // its own junction (it sums better flipped against the positive mid);
        // the bridge must then flip the right top too, so the EFFECTIVE
        // acoustic signs of the two tops agree: raw sign XOR invert must be
        // equal on both sides.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                leftTopAmplitude: -1.0,
                rightTopAmplitude: -1.0);

        bool leftInvert = alignment.GetValueOrDefault(left[2]).InvertPolarity;
        bool rightInvert = alignment.GetValueOrDefault(right[2]).InvertPolarity;
        // Both raw signs are negative, so equal effective signs mean equal
        // invert flags — and the left walk is expected to have flipped its top.
        Assert.True(leftInvert);
        Assert.Equal(leftInvert, rightInvert);
    }

    [Fact]
    public void ComputeStereo_RefusesAnUnmeasurableBridgeWithoutTouchingTheRightSide()
    {
        // The right top is silent: its band-limited arrival is invalid, and a
        // best-effort bridge would time the whole right side by garbage
        // (0 − 0 − offset). The cascade must refuse with an explanation, and
        // the right side must carry no proposals the caller could apply.
        var sub = new TestChannel("sub", ImpulseAtMs(2.0));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(1.0));
        var leftTwr = new TestChannel("L twr", ImpulseAtMs(0.0));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(2.5));
        var rightTwr = new TestChannel("R twr", new Complex[IrLength]);
        TestChannel[] all = [sub, leftWoof, leftTwr, rightWoof, rightTwr];

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();

        List<AlignmentSnapshot> initial = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        AlignmentSnapshot Of(TestChannel channel) =>
            initial.First(item => item.Channel == channel);
        List<AlignmentSnapshot> leftByBand = [Of(sub), Of(leftWoof), Of(leftTwr)];
        List<AlignmentSnapshot> rightByBand = [Of(sub), Of(rightWoof), Of(rightTwr)];
        List<AlignmentJunction> leftPairs =
        [
            Junction(leftByBand[0], leftByBand[1], 80),
            Junction(leftByBand[1], leftByBand[2], 2_500)
        ];
        List<AlignmentJunction> rightPairs =
        [
            Junction(rightByBand[0], rightByBand[1], 80),
            Junction(rightByBand[1], rightByBand[2], 2_500)
        ];

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => AutoAlignmentEngine.ComputeStereo(
                new StereoAlignmentPlan(
                    leftByBand,
                    leftPairs,
                    rightByBand,
                    rightPairs,
                    new HashSet<IAlignmentChannel> { sub },
                    leftTwr,
                    rightTwr,
                    BridgeBandLowHz: 2_500,
                    BridgeBandHighHz: 12_000,
                    SceneOffsetMs: 0.25),
                Reprocess,
                alignment,
                new StringBuilder()));

        Assert.Contains("bridge", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(alignment.ContainsKey(rightTwr));
        Assert.False(alignment.ContainsKey(rightWoof));
    }

    [Fact]
    public void ComputeStereo_RejectsAMonoBridge()
    {
        var mono = new TestChannel("mono", ImpulseAtMs(0));
        var left = new TestChannel("L", ImpulseAtMs(0));
        AlignmentSnapshot monoSnapshot = Snapshot(mono, default);
        AlignmentSnapshot leftSnapshot = Snapshot(left, default);
        var plan = new StereoAlignmentPlan(
            [leftSnapshot, monoSnapshot],
            [Junction(leftSnapshot, monoSnapshot, 1_000)],
            [monoSnapshot],
            [],
            new HashSet<IAlignmentChannel> { mono },
            left,
            mono,
            1_000,
            4_000,
            0);

        Assert.Throws<ArgumentException>(() => AutoAlignmentEngine.ComputeStereo(
            plan,
            overrides => [monoSnapshot, leftSnapshot],
            new Dictionary<IAlignmentChannel, AlignmentOverride>(),
            new StringBuilder()));
    }

    private static readonly (double LowHz, double HighHz)?[] UserLinkBands =
        [(80, 175), (400, 2_500), (2_500, 12_000)];

    // The channel's final band-limited envelope arrival with its proposed
    // delay applied — the quantity the Δ L−R read-out (and the scene) follows.
    private static double FinalBandArrivalMs(
        TestChannel channel,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
        double lowHz,
        double highHz)
    {
        AlignmentSnapshot snapshot = Snapshot(
            channel, alignment.GetValueOrDefault(channel));
        return VirtualCrossoverAnalysis.FindBandLimitedArrivalMs(
            snapshot.ImpulseResponse, SampleRate, lowHz, highHz);
    }

    [Fact]
    public void ComputeStereo_SceneLockPinsTheMidPairToTheOffset()
    {
        // The right mid's response opens with its true arrival and carries a
        // STRONGER lobe 0.7 ms behind it. Correlation-driven machinery (the
        // PHAT timeline seed, the junction-sum optimum) chases the strong
        // lobe, which would park the pair's first arrivals — what the stereo
        // image follows — 0.7 ms off the scene. The lock must pin the mid to
        // the cross-side target: first arrivals 0.25 ms apart, right leading.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: UserLinkBands,
                rightMidEchoMs: 0.7);

        Assert.Contains("SCENE-LOCKED", log.ToString());
        double delta =
            FinalBandArrivalMs(left[1], alignment, 400, 2_500) -
            FinalBandArrivalMs(right[1], alignment, 400, 2_500);
        Assert.InRange(delta, 0.15, 0.35);
    }

    [Fact]
    public void ComputeStereo_PureLowBandPairKeepsTheFreeSearch()
    {
        // The woofer link's shared band (80-175 Hz) never reaches the
        // localization region, so the scene mandate does not apply: the pair
        // keeps the free joint-junction search and the cross-side target
        // stays a gentle prior only. The mid link (400-2500 Hz) is locked.
        (TestChannel _, TestChannel[] _, TestChannel[] _,
            Dictionary<IAlignmentChannel, AlignmentOverride> _,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: UserLinkBands);

        string[] lines = log.ToString().Split('\n');
        string woofLine = Array.Find(lines,
            line => line.StartsWith("Channel R woof:"))!;
        Assert.NotNull(woofLine);
        Assert.DoesNotContain("SCENE-LOCKED", woofLine);
        Assert.Contains("(cross-side)", woofLine);
        string midLine = Array.Find(lines,
            line => line.StartsWith("Channel R mid:"))!;
        Assert.NotNull(midLine);
        Assert.Contains("SCENE-LOCKED", midLine);
    }

    [Fact]
    public void ComputeStereo_NarrowSharedBandGetsNoLockAndNoPrior()
    {
        // A link whose shared band is narrower than the arrival analysis
        // admits (1000-1100 Hz is a seventh of an octave) must not produce a
        // cross-side target at all — the band is no longer silently widened
        // into a measurable one — and without a target there is no lock: the
        // channel falls back to its own-side anchor and the run completes.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: [null, (1_000, 1_100), null]);

        Assert.DoesNotContain("cross-side prior R mid", log.ToString());
        Assert.DoesNotContain("SCENE-LOCKED", log.ToString());
        Assert.All(
            new[] { sub, left[0], left[1], left[2], right[0], right[1], right[2] },
            channel => Assert.True(
                alignment.GetValueOrDefault(channel).DelayMs is >= 0 and <= 100));
    }

    [Fact]
    public void ComputeStereo_NearTheDelayCeilingTheSceneSurvives()
    {
        // The left side is 99.2 ms late, parking the whole right side just
        // under the 100 ms delay ceiling. Every pass that adds delay from
        // here — the co-move above all — must bound its window by the ceiling
        // UP FRONT: clamping one side after the fact would move the two sides
        // unequally and silently bend the scene. Both linked pairs must still
        // read the scene offset at the end and nothing may exceed the limit.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                rightLateMs: 0,
                linkBands: UserLinkBands,
                leftLateMs: 99.2);

        Assert.Contains("Co-move", log.ToString());
        Assert.All(
            new[] { sub, left[0], left[1], left[2], right[0], right[1], right[2] },
            channel => Assert.True(
                alignment.GetValueOrDefault(channel).DelayMs is >= 0 and <= 100));

        double twrDelta =
            FinalBandArrivalMs(left[2], alignment, 2_500, 12_000) -
            FinalBandArrivalMs(right[2], alignment, 2_500, 12_000);
        Assert.InRange(twrDelta, 0.15, 0.35);
        double midDelta =
            FinalBandArrivalMs(left[1], alignment, 400, 2_500) -
            FinalBandArrivalMs(right[1], alignment, 400, 2_500);
        Assert.InRange(midDelta, 0.15, 0.35);
    }

    [Fact]
    public void ComputeStereo_ReprocessCallCountStaysBounded()
    {
        // The engine's cost unit is one reprocess: every channel's full DSP
        // chain re-run. The junction walks legitimately spend a couple per
        // channel; the co-move pass must spend ONE per linked pair (its delta
        // scan is an analytic spectrum rotation, not a re-render). The old
        // implementation burned ~40 reprocesses per pair inside the co-move
        // alone, so this ceiling breaks loudly if per-delta re-rendering ever
        // creeps back in.
        int[] count = [0];
        RunStereo(
            sceneOffsetMs: 0.25,
            linkBands: UserLinkBands,
            reprocessCount: count);

        Assert.InRange(count[0], 1, 40);
    }
}
