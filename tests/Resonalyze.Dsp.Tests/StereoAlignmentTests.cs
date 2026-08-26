using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The stereo alignment cascade: the plan's reference (left-role) side
/// first, then the top-pair arrival bridge with the scene offset (positive
/// = the plan's right-role side, the far one, leads), then the far-side
/// descent that must not touch mono channels. A right-hand-drive caller
/// hands the plan mirrored — the mirrored-plan test pins that shape. The
/// synthetic systems place impulses at known positions, so every stage's
/// contribution is verifiable arithmetic.
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
        public int ProcessorSampleRate => SampleRate;
        public Complex[] Ir { get; } = ir;
    }

    private static Complex[] ImpulseAtMs(double offsetMs, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        int position = BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate);
        ir[position] = amplitude;
        return ir;
    }

    // A first arrival plus a competing lobe, to smear a junction's whitened
    // correlation into a low-dominance comb — the seed then falls back to the
    // arrival envelope (the "untrusted" case the wide window exists for).
    private static Complex[] ImpulseWithEcho(
        double offsetMs, double amplitude, double echoMs, double echoAmplitude)
    {
        Complex[] ir = ImpulseAtMs(offsetMs, amplitude);
        int echo = BasePosition + (int)Math.Round((offsetMs + echoMs) / 1000.0 * SampleRate);
        ir[echo] += echoAmplitude;
        return ir;
    }

    private static AlignmentSnapshot Snapshot(
        TestChannel channel, AlignmentOverride over)
    {
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            channel.Ir,
            new DspChannelChain(
                DelayMs: over.DelayMs, InvertPolarity: over.InvertPolarity),
            SampleRate,
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
            double rightMidAmplitude = 1.0,
            int[]? reprocessCount = null,
            double globalLateMs = 0,
            bool mirrorPlan = false)
    {
        var sub = new TestChannel(
            "sub", ImpulseAtMs(2.0 + leftLateMs + globalLateMs));
        var leftWoof = new TestChannel(
            "L woof", ImpulseAtMs(1.0 + leftLateMs + globalLateMs));
        var leftMid = new TestChannel(
            "L mid", ImpulseAtMs(0.4 + leftLateMs + globalLateMs));
        var leftTwr = new TestChannel(
            "L twr", ImpulseAtMs(0.0 + leftLateMs + globalLateMs, leftTopAmplitude));
        var rightWoof = new TestChannel(
            "R woof", ImpulseAtMs(1.0 + rightLateMs + globalLateMs));
        Complex[] rightMidIr = ImpulseAtMs(
            0.4 + rightLateMs + globalLateMs,
            rightMidEchoMs > 0 ? 0.6 : rightMidAmplitude);
        if (rightMidEchoMs > 0)
        {
            int echoPosition = BasePosition + (int)Math.Round(
                (0.4 + rightLateMs + globalLateMs + rightMidEchoMs)
                    / 1000.0 * SampleRate);
            rightMidIr[echoPosition] += Complex.One;
        }
        var rightMid = new TestChannel("R mid", rightMidIr);
        var rightTwr = new TestChannel(
            "R twr", ImpulseAtMs(0.0 + rightLateMs + globalLateMs, rightTopAmplitude));

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
                    // The link's first member is the settled reference-side
                    // channel, so a mirrored plan swaps the pair too.
                    pairLinks.Add(mirrorPlan
                        ? new StereoPairLink(
                            linkChannels[i].Right, linkChannels[i].Left,
                            band.LowHz, band.HighHz)
                        : new StereoPairLink(
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
                mirrorPlan ? rightSnapshots : leftSnapshots,
                mirrorPlan ? rightPairs : leftPairs,
                mirrorPlan ? leftSnapshots : rightSnapshots,
                mirrorPlan ? leftPairs : rightPairs,
                new HashSet<IAlignmentChannel> { sub },
                mirrorPlan ? rightTwr : leftTwr,
                mirrorPlan ? leftTwr : rightTwr,
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
    public void ComputeStereo_MirroredPlanMakesTheLeftSideLead()
    {
        // The right-hand-drive shape: the caller mirrors the plan (the right
        // side is the reference the cascade settles first, the left one is
        // fitted to it), and the same POSITIVE offset now makes the left
        // side lead — the right top's final arrival is 0.25 ms LATER.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25, mirrorPlan: true);

        double leftTop = FinalArrivalMs(left[2], 0.0, alignment);
        double rightTop = FinalArrivalMs(right[2], 1.5, alignment);
        Assert.InRange(rightTop - leftTop, 0.20, 0.30);

        // Both sides must still cohere internally: the reference walk now
        // runs on the right side (tuning the shared sub along the way) and
        // the descent on the left, whose woofer sits between TWO settled
        // references (the mono sub and its mid) — hence the wider tolerance
        // mirroring the LHD test's fitted side.
        double[] naturals = [1.0, 0.4, 0.0];
        for (int i = 0; i < 2; i++)
        {
            Assert.InRange(
                Math.Abs(
                    FinalArrivalMs(right[i], naturals[i] + 1.5, alignment) -
                    FinalArrivalMs(right[i + 1], naturals[i + 1] + 1.5, alignment)),
                0, 0.1);
            Assert.InRange(
                Math.Abs(
                    FinalArrivalMs(left[i], naturals[i], alignment) -
                    FinalArrivalMs(left[i + 1], naturals[i + 1], alignment)),
                0, 0.2);
        }

        double minimum = new[] { sub, left[0], left[1], left[2], right[0], right[1], right[2] }
            .Min(channel => alignment.GetValueOrDefault(channel).DelayMs);
        Assert.InRange(minimum, 0, 0.011);
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

        // The far pass reports the pinned junction instead of tuning it
        // (role-based wording: on a mirrored RHD plan the reference side is
        // physically the right one).
        Assert.Contains("mono, timed by the reference side", log.ToString());
    }

    [Fact]
    public void ComputeStereo_RightTopInheritsTheLeftTopsPolarityNeverAsymmetric()
    {
        // The right tweeter is wired backwards (negative impulse). Automatic delay
        // must NEVER invert one side of a pair alone: polarity is a property of the
        // driver, decided on the left and mirrored to the right. So the right top
        // inherits the left top's sign (both normal here) and is NOT flipped — a
        // genuinely reverse-wired driver is left for a MANUAL flip, not an asymmetric
        // automatic correction.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                rightTopAmplitude: -1.0,
                linkBands: UserLinkBands);

        Assert.False(alignment.GetValueOrDefault(left[2]).InvertPolarity);
        Assert.False(alignment.GetValueOrDefault(right[2]).InvertPolarity);
        Assert.Equal(
            alignment.GetValueOrDefault(left[2]).InvertPolarity,
            alignment.GetValueOrDefault(right[2]).InvertPolarity);
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
    public void ComputeStereo_RightDriverInheritsItsLeftCounterpartsPolarity()
    {
        // Polarity is a property of the DRIVER, not the side. The RIGHT mid is wired
        // backwards: aligned on its own against the top it would flip (its mid/tweeter
        // junction is high enough that a flip is unambiguous). The LEFT mid is wired
        // normally and does not flip. The symmetry rule makes the right mid inherit
        // its left counterpart's sign and search only the delay, so the two sides end
        // with the SAME polarity — the asymmetric inversion (one side's mid flipped,
        // the other not) Butterworth used to trigger is now structurally impossible.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: UserLinkBands,
                rightMidAmplitude: -1.0);

        Assert.False(alignment.GetValueOrDefault(left[1]).InvertPolarity);
        Assert.Equal(
            alignment.GetValueOrDefault(left[1]).InvertPolarity,
            alignment.GetValueOrDefault(right[1]).InvertPolarity);
    }

    [Fact]
    public void ComputeStereo_AutoDelayNeverInvertsAPairAsymmetrically()
    {
        // The user's absolute rule for automatic delay: whatever the measurements,
        // a driver's polarity flag is identical on both sides. Even with the right
        // mid AND the right top wired backwards — each of which, aligned on its own,
        // would flip — every pair stays symmetric.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                rightTopAmplitude: -1.0,
                linkBands: UserLinkBands,
                rightMidAmplitude: -1.0);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(
                alignment.GetValueOrDefault(left[i]).InvertPolarity,
                alignment.GetValueOrDefault(right[i]).InvertPolarity);
        }
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
    public void ComputeStereo_PureLowBandPairIsLockedToItsArrivalLobe()
    {
        // The woofer link's shared band (80-175 Hz) never reaches the
        // localization region, so the tight scene pin does not apply — but an
        // identical L/R driver pair's delay split is still physical, and the
        // junction comb (lobes a dB apart at a low junction) must not choose
        // it: the field failure parked one under-seat midbass at 0 and the
        // other at 10.85 ms. The pair is locked to the cross-side arrival's
        // LOBE (half the tightest adjacent junction period), inside which the
        // junction sum keeps full authority. The mid link (400-2500 Hz) keeps
        // the tight scene lock.
        (TestChannel _, TestChannel[] _, TestChannel[] _,
            Dictionary<IAlignmentChannel, AlignmentOverride> _,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: UserLinkBands);

        string[] lines = log.ToString().Split('\n');
        string woofLine = Array.Find(lines,
            line => line.StartsWith("Channel R woof:"))!;
        Assert.NotNull(woofLine);
        Assert.Contains("(cross-side)", woofLine);
        Assert.Contains("SCENE-LOCKED", woofLine);
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
    public void ComputeStereo_ReferenceSideProposal_IsIndependentOfTheFarSide()
    {
        // The reference side is settled first and only the pair co-move can
        // move it afterwards — and that co-move is judged on the REFERENCE
        // side's junctions alone, so the near side's relations cannot depend
        // on the far side's acoustics at all. Derange the far mid (a strong
        // second lobe 0.7 ms behind its arrival) and every relation on the
        // reference side must come out identical. The far side still BOUNDS
        // the shared delta — it can veto one that would leave it a lobe out —
        // it just cannot ask for one. (This pins the invariant, not the
        // criterion: the two criteria happen to agree on this fixture. The
        // criterion's evidence is the archived-cabin battery, where judging
        // by the mean cost the reference side up to 0.03 dB of junction loss
        // it had already won.)
        (_, TestChannel[] left, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> plain, _) = RunStereo(
                sceneOffsetMs: 0.25, linkBands: UserLinkBands);
        (_, TestChannel[] derangedLeft, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> deranged,
            StringBuilder derangedLog) = RunStereo(
                sceneOffsetMs: 0.25, linkBands: UserLinkBands, rightMidEchoMs: 0.7);

        // The premise: a co-move actually ran on the deranged pair, so the
        // criterion had something to decide.
        Assert.Contains("Co-move L mid+R mid: ", derangedLog.ToString());

        // Compared RELATIVE to the reference side's own bottom channel: the
        // mono co-move and the final normalization both shift every channel
        // uniformly, which changes no relation and is not what this pins.
        for (int i = 1; i < left.Length; i++)
        {
            Assert.Equal(
                plain.GetValueOrDefault(left[i]).DelayMs -
                    plain.GetValueOrDefault(left[0]).DelayMs,
                deranged.GetValueOrDefault(derangedLeft[i]).DelayMs -
                    deranged.GetValueOrDefault(derangedLeft[0]).DelayMs,
                2);
            Assert.Equal(
                plain.GetValueOrDefault(left[i]).InvertPolarity,
                deranged.GetValueOrDefault(derangedLeft[i]).InvertPolarity);
        }
    }

    [Fact]
    public void ComputeStereo_NearTheDelayCeilingTheSceneSurvives()
    {
        // The left side is 46 ms late, parking the whole right side just
        // under the 50 ms delay ceiling (the ceiling is the transferable
        // range a device could accept, not an operating region — real cabin
        // spans run well under 10 ms). Every pass that adds delay from here — the
        // co-move above all — must bound its window by the remaining span UP
        // FRONT: clamping one side after the fact would move the two sides
        // unequally and silently bend the scene. Both linked pairs must still
        // read the scene offset at the end and nothing may exceed the limit.
        // (At this realistic lateness the impulses sit well inside the
        // detector's linear range, so the scene is measured directly on the
        // final proposal — the normalization is uniform and scene-invariant.)
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment,
            StringBuilder log) = RunStereo(
                sceneOffsetMs: 0.25,
                rightLateMs: 0,
                linkBands: UserLinkBands,
                leftLateMs: 46.0);

        Assert.Contains("Co-move", log.ToString());
        Assert.All(
            new[] { sub, left[0], left[1], left[2], right[0], right[1], right[2] },
            channel => Assert.True(
                alignment.GetValueOrDefault(channel).DelayMs is >= 0 and <= 50));

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
        // channel; BOTH co-moves spend ONE per pass — their delta scans are
        // spectrum rotations through per-channel windowed cuts, never
        // re-renders. (The mono co-move once re-rendered every one of its
        // ~30-60 probes, on the belief that rotation through a fixed shared
        // gate misgrades multi-millisecond deltas — true of that gate, and
        // repealed with it: the windows travel with the channels now, so the
        // rotation IS the honest read.) The ceiling breaks loudly if
        // per-delta re-rendering creeps back into any of them.
        int[] count = [0];
        RunStereo(
            sceneOffsetMs: 0.25,
            linkBands: UserLinkBands,
            reprocessCount: count);

        Assert.InRange(count[0], 1, 40);
    }

    [Fact]
    public void Compute_UntrustedSeedWindow_IsKeyedToTheJunctionNotTheChannel()
    {
        // sub/woof (80 Hz) is made untrusted by a sub echo that smears its whitened
        // correlation into a low-dominance comb; woof/mid (120 Hz) stays trusted. The
        // mid arrives latest, so the walk DESCENDS: woof is searched against mid (its
        // TRUSTED junction) and sub against woof (its UNTRUSTED junction, as the LOWER
        // channel). The wide-seed window must key on the JUNCTION, so:
        //  - sub's untrusted junction widens even though sub is its LOWER channel — the
        //    channel-keyed version recorded only the UPPER (woof) and missed this on the
        //    downward walk;
        //  - woof's trusted junction stays narrow even though woof is the untrusted
        //    UPPER of sub/woof — the channel-keyed version would have leaked the wide
        //    window onto this unrelated, trusted junction.
        // The sub's echo sits a full 80 Hz period out and all but matches the
        // direct copy, so its whitened correlation carries two same-polarity
        // lobes a period apart that the RIVAL gate cannot separate — the
        // whole-period ambiguity the fine window cannot undo, and the one
        // remaining reason to fall back to the arrival. Its junction band is
        // widened like the sibling rival tests', so the kernel's own trough
        // stays shallow and the rival rule is what refuses the seed.
        var sub = new TestChannel("sub", ImpulseWithEcho(1.0, 0.995, 12.5, 1.0));
        var woof = new TestChannel("woof", ImpulseAtMs(3.0));
        var mid = new TestChannel("mid", ImpulseAtMs(6.0));
        TestChannel[] all = [sub, woof, mid];
        List<AlignmentSnapshot> snapshots = all.Select(c => Snapshot(c, default)).ToList();
        var pairs = new List<AlignmentJunction>
        {
            new(snapshots[0], snapshots[1], 80, 30, 340),
            Junction(snapshots[1], snapshots[2], 120)
        };
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(c => Snapshot(c, overrides.GetValueOrDefault(c))).ToList();
        AutoAlignmentEngine.Compute(snapshots, pairs, Reprocess, alignment, log);

        string text = log.ToString();
        // The premise: exactly the low junction fell back to the arrival seed.
        Assert.Contains("seed arrival", TestLog.Line(text, "Pair sub/woof"));
        Assert.Contains("seed phat", TestLog.Line(text, "Pair woof/mid"));

        // Issue 1: the untrusted junction widens on the descending search of its lower
        // channel. Issue 2: the trusted junction is not polluted by its shared channel.
        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel sub:"));
        Assert.DoesNotContain("WIDE SEED", TestLog.Line(text, "Channel woof:"));
    }

    [Fact]
    public void ComoveMonoChannels_IsInvariantToTheFieldsAbsoluteOffset()
    {
        // The mono co-move works in RELATIVE coordinates: the mono channel's
        // own delay hitting zero is not a wall, because the same relative
        // placement is reachable by lifting every other channel together
        // (scene- and junction-preserving). Two alignments that differ by
        // nothing but a global offset must therefore produce the SAME
        // relative answer — here the sub sits at its floor and both woofers
        // want it ~1.2 ms earlier, so the un-offset run must rebase the rest
        // of the field instead of settling for a clipped move.
        var sub = new TestChannel("sub", ImpulseAtMs(10.0));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(8.0));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(7.5));
        TestChannel[] all = [sub, leftWoof, rightWoof];

        (double SubRelativeMs, bool SubInverted, string Log) Run(double offsetMs)
        {
            IReadOnlyList<AlignmentSnapshot> Reprocess(
                IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
                all.Select(channel =>
                    Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();

            List<AlignmentSnapshot> snapshots = all
                .Select(channel => Snapshot(channel, default))
                .ToList();
            AlignmentJunction leftPair = Junction(snapshots[0], snapshots[1], 80);
            AlignmentJunction rightPair = Junction(snapshots[0], snapshots[2], 80);
            var plan = new StereoAlignmentPlan(
                [snapshots[0], snapshots[1]],
                [leftPair],
                [snapshots[0], snapshots[2]],
                [rightPair],
                new HashSet<IAlignmentChannel> { sub },
                leftWoof,
                rightWoof,
                40,
                160,
                SceneOffsetMs: 0);
            var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
            {
                [sub] = new(0 + offsetMs, false),
                [leftWoof] = new(1.0 + offsetMs, false),
                [rightWoof] = new(1.0 + offsetMs, false)
            };
            var log = new StringBuilder();
            AutoAlignmentEngine.ComoveMonoChannels(
                plan, Reprocess, alignment, log, snapshots);
            return (
                alignment[sub].DelayMs - alignment[leftWoof].DelayMs,
                alignment[sub].InvertPolarity,
                log.ToString());
        }

        (double atFloor, bool floorInverted, string floorLog) = Run(offsetMs: 0);
        (double free, bool freeInverted, string freeLog) = Run(offsetMs: 3);

        // Both runs moved (the woofers' junctions clearly want the sub
        // earlier), the floor run by rebasing the rest of the field.
        Assert.Contains("Co-move sub:", floorLog);
        Assert.Contains("Co-move sub:", freeLog);
        Assert.True(atFloor < -1.5, $"the sub did not move earlier ({atFloor:0.00} ms)");
        Assert.InRange(Math.Abs(atFloor - free), 0, 0.06);
        Assert.Equal(freeInverted, floorInverted);
    }

    [Fact]
    public void ComoveMonoChannels_SubBandInconsistentHop_IsVetoed()
    {
        // The field failure this pins (an 80 Hz sub junction): both woofers
        // carry a strong inverted narrowband build-up in the junction band's
        // UPPER half, half a period behind their direct front. Through the
        // full-band mean the sub then "gains" by moving onto that build-up
        // (a comb impostor the margin gate cannot refuse), while the clean
        // LOWER half — direct sound only — plainly loses. The sub-band
        // consistency veto is the cross-check narrow-band ranging disciplines
        // converge on: a true lobe holds every half of the band, an impostor
        // wins one half and loses the other.
        //
        // The build-up has to be genuinely strong (-6 dB) to fool the mean.
        // At -10 dB it once did, but only through a window that moved with
        // the probe: the grid's anchor was the earliest peak of the whole
        // field, which the probed sub itself set, so a cell that carried the
        // sub earlier also carried the window earlier and won ~0.5 dB of that
        // move rather than of the summation. With the window held for the
        // whole grid (see JunctionGateAnchor) the same impostor gains 0.02 dB
        // and is refused by the hop margin long before the veto — the guard
        // this test exists for needs a build-up that wins on merit.
        Complex[] WooferIr()
        {
            Complex[] ir = ImpulseAtMs(8.0);
            Complex[] mode = VirtualCrossoverAnalysis.ApplyChain(
                ImpulseAtMs(8.0 + 6.25, -6.0),
                new DspChannelChain(Crossover: new CrossoverSpec(
                    CrossoverKind.BandPass,
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 150, 36),
                    new CrossoverEdge(CrossoverFilterFamily.Butterworth, 100, 36))),
                SampleRate,
                SampleRate);
            for (int i = 0; i < ir.Length; i++)
            {
                ir[i] += mode[i];
            }
            return ir;
        }
        var sub = new TestChannel("sub", ImpulseAtMs(8.0));
        var leftWoof = new TestChannel("L woof", WooferIr());
        var rightWoof = new TestChannel("R woof", WooferIr());
        TestChannel[] all = [sub, leftWoof, rightWoof];
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();
        List<AlignmentSnapshot> snapshots = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        var plan = new StereoAlignmentPlan(
            [snapshots[0], snapshots[1]],
            [Junction(snapshots[0], snapshots[1], 80)],
            [snapshots[0], snapshots[2]],
            [Junction(snapshots[0], snapshots[2], 80)],
            new HashSet<IAlignmentChannel> { sub },
            leftWoof,
            rightWoof,
            40,
            160,
            SceneOffsetMs: 0);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [sub] = new(0, false),
            [leftWoof] = new(0, false),
            [rightWoof] = new(0, false)
        };
        var log = new StringBuilder();

        AutoAlignmentEngine.ComoveMonoChannels(
            plan, Reprocess, alignment, log, snapshots);

        Assert.Contains("mono lobe hop vetoed for sub", log.ToString());
        // The sub stays on the direct-sound lobe: no flip, at most an in-lobe
        // polish away from the aligned position.
        Assert.False(alignment[sub].InvertPolarity);
        Assert.InRange(
            alignment[sub].DelayMs - alignment[leftWoof].DelayMs, -2.5, 2.5);
    }

    [Fact]
    public void ComoveMonoChannels_UnmeasurableRightJunction_AbstainsEntirely()
    {
        // The right sub junction never faced the walk's structure gate on
        // its own (the descent certifies a COMBINED band), so the co-move
        // certifies every junction itself — and with the right woofer only a
        // -60 dB residue, the whole co-move must abstain rather than move
        // the sub judged by the healthy left junction alone: that would
        // merely re-optimize what the walk already settled.
        var sub = new TestChannel("sub", ImpulseAtMs(10.0));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(8.0));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(7.5, 0.001));
        TestChannel[] all = [sub, leftWoof, rightWoof];
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();
        List<AlignmentSnapshot> snapshots = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        var plan = new StereoAlignmentPlan(
            [snapshots[0], snapshots[1]],
            [Junction(snapshots[0], snapshots[1], 80)],
            [snapshots[0], snapshots[2]],
            [Junction(snapshots[0], snapshots[2], 80)],
            new HashSet<IAlignmentChannel> { sub },
            leftWoof,
            rightWoof,
            40,
            160,
            SceneOffsetMs: 0);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [sub] = new(0, false),
            [leftWoof] = new(1.0, false),
            [rightWoof] = new(1.0, false)
        };
        var log = new StringBuilder();

        AutoAlignmentEngine.ComoveMonoChannels(
            plan, Reprocess, alignment, log, snapshots);

        Assert.Contains("mono co-move skipped for sub", log.ToString());
        Assert.DoesNotContain("Co-move sub:", log.ToString());
        Assert.Equal(0, alignment[sub].DelayMs);
        Assert.False(alignment[sub].InvertPolarity);
    }

    [Fact]
    public void ComoveMonoChannels_RefreshesTheStaleDecision()
    {
        // The same system as the invariance test above: both woofers want the
        // sub earlier than the left walk left it. The walk recorded the sub
        // as the untouched reference; once the co-move moves it, the report
        // must not keep calling it that — the decision is replaced with the
        // co-move's own kind and confidence, keeping the history in the
        // detail.
        var sub = new TestChannel("sub", ImpulseAtMs(10.0));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(8.0));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(7.5));
        TestChannel[] all = [sub, leftWoof, rightWoof];
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();
        List<AlignmentSnapshot> snapshots = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        var plan = new StereoAlignmentPlan(
            [snapshots[0], snapshots[1]],
            [Junction(snapshots[0], snapshots[1], 80)],
            [snapshots[0], snapshots[2]],
            [Junction(snapshots[0], snapshots[2], 80)],
            new HashSet<IAlignmentChannel> { sub },
            leftWoof,
            rightWoof,
            40,
            160,
            SceneOffsetMs: 0);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [sub] = new(0, false),
            [leftWoof] = new(1.0, false),
            [rightWoof] = new(1.0, false)
        };
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>
        {
            [sub] = new(
                AlignmentDecisionKind.Reference, Confidence: null,
                "reference (others align to it)")
        };
        var log = new StringBuilder();

        AutoAlignmentEngine.ComoveMonoChannels(
            plan, Reprocess, alignment, log, snapshots, decisions);

        Assert.Contains("Co-move sub:", log.ToString());
        AlignmentDecision decision = decisions[sub];
        Assert.Equal(AlignmentDecisionKind.Search, decision.Kind);
        Assert.NotNull(decision.Confidence);
        Assert.Contains("mono co-move", decision.Detail);
        Assert.Contains("reference", decision.Detail);
    }

    [Fact]
    public void ComputeStereo_IsInvariantToAGlobalTimeOffset()
    {
        // Two systems differing by nothing but a uniform acoustic offset are
        // the SAME system: every relative quantity — the walk's bases, the
        // scene, and above all the co-move windows (which once bounded by
        // absolute [0, ceiling] positions and so depended on the transient
        // global offset) — must produce the identical normalized proposal.
        (TestChannel _, TestChannel[] leftA, TestChannel[] rightA,
            Dictionary<IAlignmentChannel, AlignmentOverride> baseline, _) =
            RunStereo(sceneOffsetMs: 0.25, linkBands: UserLinkBands);
        (TestChannel _, TestChannel[] leftB, TestChannel[] rightB,
            Dictionary<IAlignmentChannel, AlignmentOverride> offset, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                linkBands: UserLinkBands,
                globalLateMs: 15.0);

        TestChannel[] channelsA = [leftA[0], leftA[1], leftA[2], rightA[0], rightA[1], rightA[2]];
        TestChannel[] channelsB = [leftB[0], leftB[1], leftB[2], rightB[0], rightB[1], rightB[2]];
        for (int i = 0; i < channelsA.Length; i++)
        {
            Assert.Equal(
                baseline.GetValueOrDefault(channelsA[i]).DelayMs,
                offset.GetValueOrDefault(channelsB[i]).DelayMs,
                2);
            Assert.Equal(
                baseline.GetValueOrDefault(channelsA[i]).InvertPolarity,
                offset.GetValueOrDefault(channelsB[i]).InvertPolarity);
        }
    }

    // ---- the pure latched-fallback donor resolver (unit-tested directly, since
    //      driving the modal-latch detectors synthetically is threshold-brittle) ----

    [Fact]
    public void ResolveLatchedPathSplit_NoDonors_YieldsNoTarget()
    {
        var (split, tier, corroborating, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([], 0.6);

        // No geometry reference at all: the caller must NOT fabricate one.
        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
        Assert.Equal(0.0, split);
        Assert.Equal(0, corroborating);
    }

    [Fact]
    public void ResolveLatchedPathSplit_LoneDonor_IsATrustedButLooseEstimate()
    {
        var (split, tier, corroborating, low, high) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([1.37], 0.6);

        // One donor is a usable estimate but carries its own DSP asymmetry, so
        // it only earns the loose lock — never the tight one.
        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.Loose, tier);
        Assert.Equal(1.37, split, 6);
        Assert.Equal(1, corroborating);
        Assert.Equal(1.37, low, 6);
        Assert.Equal(1.37, high, 6);
    }

    [Fact]
    public void ResolveLatchedPathSplit_AgreeingDonors_CorroborateForTheTightLock()
    {
        // The v3 case: mids +1.37 and tweeters +1.41 agree, so the cabin's L/R
        // offset is corroborated and pinned tightly at their median.
        var (split, tier, corroborating, low, high) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([1.37, 1.41], 0.6);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.Tight, tier);
        Assert.Equal(1.39, split, 6);
        Assert.Equal(2, corroborating);
        Assert.Equal(1.37, low, 6);
        Assert.Equal(1.41, high, 6);
    }

    [Fact]
    public void ResolveLatchedPathSplit_TwoDisagreeingDonors_YieldNoTarget()
    {
        // Two donors, no corroboration: the geometry is ambiguous, so the pair
        // must not be pinned rather than gamble on one.
        var (_, tier, _, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([0.4, 1.5], 0.6);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
    }

    [Fact]
    public void ResolveLatchedPathSplit_MajorityCluster_RejectsTheOutlier()
    {
        // The reviewer's anomaly case: a nearest donor of +0.40 must NOT win
        // over two corroborating +1.4-ish donors. The majority cluster's median
        // is taken, the outlier dropped, and the reported [low, high] span
        // excludes it so the log names only the true cluster members.
        var (split, tier, corroborating, low, high) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([0.40, 1.50, 1.40], 0.6);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.Tight, tier);
        Assert.Equal(1.45, split, 6);
        Assert.Equal(2, corroborating);
        Assert.Equal(1.40, low, 6);
        Assert.Equal(1.50, high, 6);
    }

    [Fact]
    public void ResolveLatchedPathSplit_ClusterSpanExcludesADonorNearTheMedian()
    {
        // The winning cluster is [1.00, 1.05, 1.45, 1.50] (median 1.25); the
        // 0.70 donor sits 0.55 from that median — inside the tolerance — yet is
        // NOT in the cluster (1.50 − 0.70 = 0.80 > 0.60). The returned [low,
        // high] span, not a distance-to-median test, is what keeps it out of
        // the log.
        var (split, tier, corroborating, low, high) =
            AutoAlignmentEngine.ResolveLatchedPathSplit(
                [0.70, 1.00, 1.05, 1.45, 1.50], 0.60);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.Tight, tier);
        Assert.Equal(4, corroborating);
        Assert.Equal(1.00, low, 6);
        Assert.Equal(1.50, high, 6);
        Assert.True(0.70 < low, "the 0.70 donor must fall outside the cluster span");
        Assert.Equal(1.25, split, 6);
    }

    [Fact]
    public void ResolveLatchedPathSplit_BridgedDonorsDoNotFormOneTightCluster()
    {
        // A chain where consecutive pairs agree but the extremes do not: each of
        // 0.45/1.00/1.55 is within 0.6 of the middle, yet the extremes span 1.10.
        // Anchoring on the middle would fake one 3-way cluster; the mutual-span
        // rule instead sees two equally-large disagreeing 2-clusters and refuses
        // to pin — no confidently-wrong tight lock from bridged measurements.
        var (_, tier, _, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([0.45, 1.00, 1.55], 0.60);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
    }

    // ------------------------------------------------- far-side junction polish

    // A bare polish bench: two far channels 1.0 ms of assigned delay each, whose
    // junction (fc 2500, band 1250-5000) is left SKEWED by the scene-assigned
    // positions — the shape the scene locks and the arithmetic bridge leave
    // behind on a real far side. Returns the two delays after the polish.
    private static (double MidDelayMs, double TwrDelayMs, string Log)
        RunFarSidePolish(
            double twrLateMs,
            bool midIsMono = false,
            double baseDelayMs = 1.0,
            bool withFieldFloor = false)
    {
        var farMid = new TestChannel("R mid", ImpulseAtMs(5.0));
        var farTwr = new TestChannel("R twr", ImpulseAtMs(5.0 + twrLateMs));
        // A stand-in for the rest of the field: it carries no right junction, so
        // the polish walks past it, but it is what the realizable span is
        // measured FROM — the earliest channel of the whole system.
        var fieldFloor = new TestChannel("L ref", ImpulseAtMs(5.0));
        TestChannel[] all = withFieldFloor
            ? [farMid, farTwr, fieldFloor]
            : [farMid, farTwr];

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            all.Select(channel =>
                Snapshot(channel, overrides.GetValueOrDefault(channel))).ToList();

        List<AlignmentSnapshot> snapshots = all
            .Select(channel => Snapshot(channel, default))
            .ToList();
        AlignmentJunction pair = Junction(snapshots[0], snapshots[1], 2_500);
        // The left-role fields are structural dummies: the polish reads only the
        // right pairs and the mono set.
        var plan = new StereoAlignmentPlan(
            snapshots, [pair], snapshots, [pair],
            midIsMono
                ? new HashSet<IAlignmentChannel> { farMid }
                : new HashSet<IAlignmentChannel>(),
            farMid, farTwr, 1_250, 5_000, SceneOffsetMs: 0);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [farMid] = new(baseDelayMs, false),
            [farTwr] = new(baseDelayMs, false)
        };
        if (withFieldFloor)
        {
            alignment[fieldFloor] = new(0.0, false);
        }

        var log = new StringBuilder();
        AutoAlignmentEngine.PolishFarSideJunctions(
            plan, snapshots, snapshots, Reprocess, alignment, log,
            decisions: null);
        return (alignment[farMid].DelayMs, alignment[farTwr].DelayMs, log.ToString());
    }

    [Fact]
    public void PolishFarSideJunctions_ClawsBackASmallSceneSkewExactly()
    {
        // The scene positions leave the far junction 0.02 ms out of alignment —
        // inside the polish budget, so the pass closes it completely and the
        // pair's arrivals meet.
        (double midDelay, double twrDelay, string log) = RunFarSidePolish(0.02);

        double residualMs = Math.Abs((5.0 + midDelay) - (5.02 + twrDelay));
        Assert.True(
            residualMs <= 0.005,
            $"the junction skew survived the polish ({residualMs:0.000} ms)");
        Assert.Contains("Far-side polish", log);
        Assert.Contains("off the scene position", log);
    }

    [Fact]
    public void PolishFarSideJunctions_NeverSpendsMoreThanTheBudgetPerChannel()
    {
        // A 0.10 ms skew: each channel may close at most its own 0.03 ms of it.
        // The point is the ceiling, not the residual — a polish that chased the
        // whole skew would be a second alignment pass wearing a polish's name.
        (double midDelay, double twrDelay, string _) = RunFarSidePolish(0.10);

        Assert.InRange(Math.Abs(midDelay - 1.0), 0, 0.03 + 1e-9);
        Assert.InRange(Math.Abs(twrDelay - 1.0), 0, 0.03 + 1e-9);
        // And both spent their budgets TOWARD each other: mid later, twr earlier.
        Assert.True(midDelay > 1.0, $"mid did not move later ({midDelay:0.000})");
        Assert.True(twrDelay < 1.0, $"twr did not move earlier ({twrDelay:0.000})");
    }

    [Fact]
    public void PolishFarSideJunctions_StaysInsideTheRealizableDelaySpan()
    {
        // The field already spans the DSP's whole range with the earliest
        // channel at zero. The polish is the only pass that moves a channel
        // LATER after the cascade settles, so without a ceiling it would spend
        // a hundredth of a decibel to make the proposal unrealizable — and the
        // feasibility check would then refuse the whole run.
        (double midDelay, double twrDelay, string _) = RunFarSidePolish(
            0.02, baseDelayMs: 50.0, withFieldFloor: true);

        Assert.InRange(midDelay, 0, 50.0);
        Assert.InRange(twrDelay, 0, 50.0);
    }
    [Fact]
    public void PolishFarSideJunctions_NeverMovesAMonoChannel()
    {
        // The mono channel is shared with the reference side — moving it would
        // re-time the left walk's junctions behind their backs. The polish must
        // spend the tweeter's budget instead and leave the mono exactly put.
        (double midDelay, double twrDelay, string _) =
            RunFarSidePolish(0.02, midIsMono: true);

        Assert.Equal(1.0, midDelay, 3);
        Assert.InRange(twrDelay, 0.97, 0.99);
    }
}
