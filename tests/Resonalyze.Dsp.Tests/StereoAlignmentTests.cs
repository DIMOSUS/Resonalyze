using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Resonalyze.Dsp.Tests;

/// <summary>Stereo cascade: reference side, then the top-pair bridge with the scene offset (positive = far side leads), then the far-side descent.</summary>
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

    // Competing lobe smears the whitened correlation so the seed falls back to the envelope.
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

    /// <summary>Mono sub plus woof/mid/twr per side; the right side arrives 1.5 ms later. <paramref name="rightMidEchoMs"/> adds a stronger
    /// later lobe (correlation chases it, the scene follows the first); <paramref name="reprocessCount"/>[0] counts reprocesses.</summary>
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
                    // The link's first member is the settled reference-side channel, so a mirrored plan swaps the pair.
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

    private static double FinalArrivalMs(
        TestChannel channel,
        double naturalMs,
        Dictionary<IAlignmentChannel, AlignmentOverride> alignment) =>
        naturalMs + alignment.GetValueOrDefault(channel).DelayMs;

    [Fact]
    public void ComputeStereo_BridgeHonorsTheSceneOffsetSign()
    {
        // Positive offset: the right top's final arrival is 0.25 ms EARLIER than the left top's.
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
        // Mirrored (RHD) plan: the same positive offset makes the left side lead.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25, mirrorPlan: true);

        double leftTop = FinalArrivalMs(left[2], 0.0, alignment);
        double rightTop = FinalArrivalMs(right[2], 1.5, alignment);
        Assert.InRange(rightTop - leftTop, 0.20, 0.30);

        // The left woofer sits between two settled references, hence the wider tolerance.
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
        // Making the far side lead is only expressible by shifting the left field up; the minimum delay lands on zero.
        (TestChannel sub, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(sceneOffsetMs: 0.25);

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
        // The right pass may only measure the mono sub's junction, never move it.
        (TestChannel sub, TestChannel[] left, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> stereo,
            StringBuilder log) = RunStereo(sceneOffsetMs: 0.25);

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

        Assert.Contains("mono, timed by the reference side", log.ToString());
    }

    [Fact]
    public void ComputeStereo_RightTopInheritsTheLeftTopsPolarityNeverAsymmetric()
    {
        // Auto delay never inverts one side of a pair alone: the right top inherits the left top's sign.
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
        // Both tops backwards: effective signs (raw sign XOR invert) must agree.
        (TestChannel _, TestChannel[] left, TestChannel[] right,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, _) =
            RunStereo(
                sceneOffsetMs: 0.25,
                leftTopAmplitude: -1.0,
                rightTopAmplitude: -1.0);

        bool leftInvert = alignment.GetValueOrDefault(left[2]).InvertPolarity;
        bool rightInvert = alignment.GetValueOrDefault(right[2]).InvertPolarity;
        Assert.True(leftInvert);
        Assert.Equal(leftInvert, rightInvert);
    }

    [Fact]
    public void ComputeStereo_RightDriverInheritsItsLeftCounterpartsPolarity()
    {
        // Right mid backwards would flip on its own; it inherits the left mid's sign and searches only delay.
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
        // A silent top would time the whole side by garbage: refuse, with no applicable proposals.
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
        // A stronger lobe 0.7 ms behind the right mid's arrival: the lock must pin first arrivals to the scene.
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
        // The woofer link's band never reaches the localization region: locked to the cross-side lobe, not the tight scene pin.
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
        // A link band too narrow for arrival analysis yields no target and so no lock.
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
        // The co-move is judged on the reference side's junctions alone: deranging the far mid changes no reference relation.
        (_, TestChannel[] left, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> plain, _) = RunStereo(
                sceneOffsetMs: 0.25, linkBands: UserLinkBands);
        (_, TestChannel[] derangedLeft, _,
            Dictionary<IAlignmentChannel, AlignmentOverride> deranged,
            StringBuilder derangedLog) = RunStereo(
                sceneOffsetMs: 0.25, linkBands: UserLinkBands, rightMidEchoMs: 0.7);

        Assert.Contains("Co-move L mid+R mid: ", derangedLog.ToString());

        // Relative to the bottom channel: uniform shifts change no relation. The far side bounds the co-move's
        // window (never scores it), and the scan's step follows the window, so one 0.02 ms grid step is allowed.
        for (int i = 1; i < left.Length; i++)
        {
            double plainMs = plain.GetValueOrDefault(left[i]).DelayMs -
                plain.GetValueOrDefault(left[0]).DelayMs;
            double derangedMs = deranged.GetValueOrDefault(derangedLeft[i]).DelayMs -
                deranged.GetValueOrDefault(derangedLeft[0]).DelayMs;
            Assert.InRange(derangedMs, plainMs - 0.021, plainMs + 0.021);
            Assert.Equal(
                plain.GetValueOrDefault(left[i]).InvertPolarity,
                deranged.GetValueOrDefault(derangedLeft[i]).InvertPolarity);
        }
    }

    [Fact]
    public void ComputeStereo_NearTheDelayCeilingTheSceneSurvives()
    {
        // Near the 50 ms ceiling every delay-adding pass must bound its window up front; clamping one side later bends the scene.
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
        // Cost unit = one reprocess; co-moves spend one per pass (delta scans are spectrum rotations). Breaks if re-rendering creeps back.
        int[] count = [0];
        RunStereo(
            sceneOffsetMs: 0.25,
            linkBands: UserLinkBands,
            reprocessCount: count);

        Assert.InRange(count[0], 1, 40);
    }

    [Fact]
    public void NormalizePolarityPresentation_CountsDriverPositionsAndLeavesTheSubNormal()
    {
        // Mono sub plus three pairs: sub and midbasses inverted is 3 of 7 channels but 2 of 4 positions — a tie,
        // and the tie is broken so the sub reads normal; the whole field flips, both sides alike.
        var sub = new TestChannel("sub", ImpulseAtMs(0));
        TestChannel[] left = [new("L woof", ImpulseAtMs(0)), new("L mid", ImpulseAtMs(0)), new("L twr", ImpulseAtMs(0))];
        TestChannel[] right = [new("R woof", ImpulseAtMs(0)), new("R mid", ImpulseAtMs(0)), new("R twr", ImpulseAtMs(0))];
        List<AlignmentSnapshot> positions = [Snapshot(sub, default), .. left.Select(c => Snapshot(c, default))];
        List<AlignmentSnapshot> scope = [.. positions, .. right.Select(c => Snapshot(c, default))];
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [sub] = new(0, true),
            [left[0]] = new(1, true),
            [right[0]] = new(1, true)
        };

        AutoAlignmentEngine.NormalizePolarityPresentation(
            scope, alignment, new StringBuilder(), positions);

        Assert.False(alignment[sub].InvertPolarity);
        Assert.False(alignment[left[0]].InvertPolarity);
        Assert.False(alignment[right[0]].InvertPolarity);
        foreach (TestChannel stack in new[] { left[1], left[2], right[1], right[2] })
        {
            Assert.True(alignment[stack].InvertPolarity, stack.Name);
        }
    }

    /// <summary>The reference mid/twr junction ties between its lobes (an inverted twin 12 samples out, about half a
    /// period at 2500 Hz); the far tweeter is wired inverted and sits those 12 samples early, on the alias. The whole
    /// stack starts at <paramref name="baseDelayMs"/>; <paramref name="withFieldFloor"/> adds a channel at 0 ms that
    /// carries no junction but bounds the realizable span.</summary>
    private static (TestChannel LeftMid, TestChannel LeftTwr, TestChannel RightMid, TestChannel RightTwr,
        Dictionary<IAlignmentChannel, AlignmentOverride> Alignment, string Log)
        RunJunctionBranch(double baseDelayMs = 0, bool withFieldFloor = false, int twinSamples = 12) =>
        RunJunctionBranch(out _, baseDelayMs, withFieldFloor, twinSamples);

    /// <param name="renderedTwrDelays">Every left-tweeter delay a re-render was asked for, in order.</param>
    private static (TestChannel LeftMid, TestChannel LeftTwr, TestChannel RightMid, TestChannel RightTwr,
        Dictionary<IAlignmentChannel, AlignmentOverride> Alignment, string Log)
        RunJunctionBranch(
            out List<double> renderedTwrDelays,
            double baseDelayMs = 0,
            bool withFieldFloor = false,
            int twinSamples = 12)
    {
        List<double> rendered = [];
        renderedTwrDelays = rendered;
        Complex[] leftMidIr = ImpulseAtMs(0.0);
        leftMidIr[BasePosition + twinSamples] -= Complex.One;
        var leftMid = new TestChannel("L mid", leftMidIr);
        var leftTwr = new TestChannel("L twr", ImpulseAtMs(0.0));
        var rightMid = new TestChannel("R mid", ImpulseAtMs(0.0));
        Complex[] rightTwrIr = new Complex[IrLength];
        rightTwrIr[BasePosition - twinSamples] = -1.0;
        var rightTwr = new TestChannel("R twr", rightTwrIr);
        var floor = new TestChannel("sub", ImpulseAtMs(0.0));
        TestChannel[] all = withFieldFloor
            ? [leftMid, leftTwr, rightMid, rightTwr, floor]
            : [leftMid, leftTwr, rightMid, rightTwr];
        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides)
        {
            rendered.Add(overrides.GetValueOrDefault(leftTwr).DelayMs);
            return all.Select(channel => Snapshot(channel, overrides.GetValueOrDefault(channel)))
                .ToList();
        }

        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        foreach (TestChannel channel in new[] { leftMid, leftTwr, rightMid, rightTwr })
        {
            alignment[channel] = new AlignmentOverride(baseDelayMs, false);
        }
        IReadOnlyList<AlignmentSnapshot> initial = Reprocess(alignment);
        AlignmentSnapshot Of(TestChannel channel) =>
            initial.First(item => item.Channel == channel);
        List<AlignmentSnapshot> left = [Of(leftMid), Of(leftTwr)];
        List<AlignmentSnapshot> right = [Of(rightMid), Of(rightTwr)];
        var plan = new StereoAlignmentPlan(
            left, [Junction(left[0], left[1], 2_500)],
            right, [Junction(right[0], right[1], 2_500)],
            new HashSet<IAlignmentChannel>(), leftTwr, rightTwr,
            BridgeBandLowHz: 2_500, BridgeBandHighHz: 12_000, SceneOffsetMs: 0,
            [
                new StereoPairLink(leftMid, rightMid, 400, 2_500),
                new StereoPairLink(leftTwr, rightTwr, 2_500, 12_000)
            ]);
        var log = new StringBuilder();

        AutoAlignmentEngine.RebalanceJunctionBranches(
            plan, left, right, initial, Reprocess, alignment, log);

        return (leftMid, leftTwr, rightMid, rightTwr, alignment, log.ToString());
    }

    [Fact]
    public void RebalanceJunctionBranches_AdoptedMove_DelaysAndFlipsTheStackAbove()
    {
        // The move is half a period AND a flip of the tweeters on both sides; the delay alone would be the worst of
        // both branches.
        (TestChannel leftMid, TestChannel leftTwr, TestChannel rightMid, TestChannel rightTwr,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, string log) =
            RunJunctionBranch();

        Assert.Contains("stereo branch moved at L mid/L twr", log);
        foreach (TestChannel tweeter in new[] { leftTwr, rightTwr })
        {
            AlignmentOverride over = alignment.GetValueOrDefault(tweeter);
            Assert.True(over.InvertPolarity, tweeter.Name);
            Assert.InRange(over.DelayMs, 0.14, 0.26);
        }
        Assert.False(alignment.GetValueOrDefault(leftMid).InvertPolarity);
        Assert.False(alignment.GetValueOrDefault(rightMid).InvertPolarity);
        Assert.Equal(0.0, alignment.GetValueOrDefault(leftMid).DelayMs);
        Assert.Equal(0.0, alignment.GetValueOrDefault(rightMid).DelayMs);
    }

    [Fact]
    public void RebalanceJunctionBranches_RendersAndWritesTheMoveOnTheDspGrid()
    {
        // Eleven samples at 48 kHz is 0.229 ms: the scan's own grid lands on 0.225, which no processor can play.
        // The move goes onto the grid BEFORE the re-render judges it — every delay a re-render is asked for is a
        // DSP tick — and what is written is what was judged: 0.23, the tick nearer the twin.
        (_, TestChannel leftTwr, _, TestChannel rightTwr,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, string log) =
            RunJunctionBranch(out List<double> renderedTwrDelays, twinSamples: 11);

        Assert.Contains("stereo branch moved at L mid/L twr", log);
        Assert.Contains("+0.23 ms flipped", log);
        Assert.All(renderedTwrDelays, delayMs => Assert.Equal(Math.Round(delayMs, 2), delayMs, 9));
        Assert.Contains(0.23, renderedTwrDelays);
        foreach (TestChannel tweeter in new[] { leftTwr, rightTwr })
        {
            Assert.Equal(0.23, alignment.GetValueOrDefault(tweeter).DelayMs);
        }
    }

    [Fact]
    public void RebalanceJunctionBranches_DeclinesAMoveThatWouldPassTheDelayCeiling()
    {
        // The stack already sits 49.9 ms above the field's floor: the same move would span past the 50 ms ceiling
        // and turn a valid proposal into a refusal, so the branch is kept.
        (_, TestChannel leftTwr, _, TestChannel rightTwr,
            Dictionary<IAlignmentChannel, AlignmentOverride> alignment, string log) =
            RunJunctionBranch(baseDelayMs: 49.9, withFieldFloor: true);

        Assert.DoesNotContain("stereo branch moved", log);
        Assert.Contains("past the 50 ms ceiling", log);
        Assert.Equal(49.9, alignment[leftTwr].DelayMs);
        Assert.Equal(49.9, alignment[rightTwr].DelayMs);
        Assert.False(alignment[leftTwr].InvertPolarity);
    }

    [Fact]
    public void Compute_UntrustedSeedWindow_IsKeyedToTheJunctionNotTheChannel()
    {
        // sub/woof untrusted (echo a period out), woof/mid trusted; the walk descends.
        // The wide window keys on the junction: it widens for sub as the LOWER channel and does not leak to woof's trusted junction.
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
        Assert.Contains("seed arrival", TestLog.Line(text, "Pair sub/woof"));
        Assert.Contains("seed phat", TestLog.Line(text, "Pair woof/mid"));

        Assert.Contains("WIDE SEED", TestLog.Line(text, "Channel sub:"));
        Assert.DoesNotContain("WIDE SEED", TestLog.Line(text, "Channel woof:"));
    }

    [Fact]
    public void ComoveMonoChannels_IsInvariantToTheFieldsAbsoluteOffset()
    {
        // The mono co-move is relative: a sub at its zero floor lifts the rest of the field instead of clipping.
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

        Assert.Contains("Co-move sub:", floorLog);
        Assert.Contains("Co-move sub:", freeLog);
        Assert.True(atFloor < -1.5, $"the sub did not move earlier ({atFloor:0.00} ms)");
        Assert.InRange(Math.Abs(atFloor - free), 0, 0.06);
        Assert.Equal(freeInverted, floorInverted);
    }

    [Fact]
    public void ComoveMonoChannels_SubBandInconsistentHop_IsVetoed()
    {
        // Inverted build-up in the band's upper half fools the full-band mean; the sub-band veto must refuse it.
        // It must be -6 dB to win on merit once the window is held for the grid (see JunctionGateAnchor).
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
        Assert.False(alignment[sub].InvertPolarity);
        Assert.InRange(
            alignment[sub].DelayMs - alignment[leftWoof].DelayMs, -2.5, 2.5);
    }

    [Fact]
    public void ComoveMonoChannels_UnmeasurableRightJunction_AbstainsEntirely()
    {
        // With the right woofer a -60 dB residue the co-move must abstain, not re-optimize on the left junction alone.
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
    public void ComoveMonoChannels_AfterThePolish_ATrimAmendsTheDecisionAndKeepsItsConfidence()
    {
        // The pass that follows the far-side polish trims the sub inside its lobe: that amends the decision the walk
        // and the first co-move made. Re-deciding it from a trim's small gain would report a confident sub as Low.
        var sub = new TestChannel("sub", ImpulseAtMs(9.0));
        var leftWoof = new TestChannel("L woof", ImpulseAtMs(8.0));
        var rightWoof = new TestChannel("R woof", ImpulseAtMs(8.0));
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
            [sub] = new(2.0, false),
            [leftWoof] = new(2.0, false),
            [rightWoof] = new(2.0, false)
        };
        var decisions = new Dictionary<IAlignmentChannel, AlignmentDecision>
        {
            [sub] = new(AlignmentDecisionKind.Search, AlignmentConfidence.High, "mono co-move -6.00 ms + invert")
        };
        var log = new StringBuilder();

        bool moved = AutoAlignmentEngine.ComoveMonoChannels(
            plan, Reprocess, alignment, log, snapshots, decisions: decisions, afterPolish: true);

        Assert.True(moved, log.ToString());
        Assert.InRange(alignment[sub].DelayMs - alignment[leftWoof].DelayMs, -1.2, -0.8);
        Assert.Equal(AlignmentConfidence.High, decisions[sub].Confidence);
        Assert.StartsWith("mono co-move -6.00 ms + invert; ", decisions[sub].Detail);
        Assert.Contains("after the far-side polish", decisions[sub].Detail);
    }

    [Fact]
    public void ComoveMonoChannels_RefreshesTheStaleDecision()
    {
        // Once the co-move moves the sub it is no longer reported as the reference.
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
            plan, Reprocess, alignment, log, snapshots, decisions: decisions);

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
        // A uniform acoustic offset must give the identical normalized proposal.
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

    // Latched-fallback donor resolver, unit-tested directly: synthetic modal latches are threshold-brittle.

    [Fact]
    public void ResolveLatchedPathSplit_NoDonors_YieldsNoTarget()
    {
        var (split, tier, corroborating, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([], 0.6);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
        Assert.Equal(0.0, split);
        Assert.Equal(0, corroborating);
    }

    [Fact]
    public void ResolveLatchedPathSplit_LoneDonor_IsATrustedButLooseEstimate()
    {
        var (split, tier, corroborating, low, high) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([1.37], 0.6);

        // A single donor carries its own DSP asymmetry: loose lock only.
        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.Loose, tier);
        Assert.Equal(1.37, split, 6);
        Assert.Equal(1, corroborating);
        Assert.Equal(1.37, low, 6);
        Assert.Equal(1.37, high, 6);
    }

    [Fact]
    public void ResolveLatchedPathSplit_AgreeingDonors_CorroborateForTheTightLock()
    {
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
        var (_, tier, _, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([0.4, 1.5], 0.6);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
    }

    [Fact]
    public void ResolveLatchedPathSplit_MajorityCluster_RejectsTheOutlier()
    {
        // A nearest +0.40 donor must not beat two corroborating ~+1.4 donors.
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
        // 0.70 is within tolerance of the median yet outside the cluster span (1.50 − 0.70 > 0.60).
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
        // Consecutive pairs agree but extremes span 1.10: two disagreeing 2-clusters, so no pin.
        var (_, tier, _, _, _) =
            AutoAlignmentEngine.ResolveLatchedPathSplit([0.45, 1.00, 1.55], 0.60);

        Assert.Equal(AutoAlignmentEngine.CrossSideLockTier.None, tier);
    }

    // Two far channels with 1.0 ms each and a junction (fc 2500) left skewed by the scene positions.
    private static (double MidDelayMs, double TwrDelayMs, string Log)
        RunFarSidePolish(
            double twrLateMs,
            bool midIsMono = false,
            double baseDelayMs = 1.0,
            bool withFieldFloor = false,
            double fieldChannelMs = 0.0,
            double junctionHz = 2_500,
            int rounds = 1,
            double midOffsetMs = 0.0)
    {
        var farMid = new TestChannel("R mid", ImpulseAtMs(5.0));
        var farTwr = new TestChannel("R twr", ImpulseAtMs(5.0 + twrLateMs));
        // Carries no right junction; it is the earliest channel the realizable span is measured from.
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
        AlignmentJunction pair = Junction(snapshots[0], snapshots[1], junctionHz);
        var plan = new StereoAlignmentPlan(
            snapshots, [pair], snapshots, [pair],
            midIsMono
                ? new HashSet<IAlignmentChannel> { farMid }
                : new HashSet<IAlignmentChannel>(),
            farMid, farTwr, junctionHz / 2, junctionHz * 2, SceneOffsetMs: 0);
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>
        {
            [farMid] = new(baseDelayMs + midOffsetMs, false),
            [farTwr] = new(baseDelayMs, false)
        };
        if (withFieldFloor)
        {
            alignment[fieldFloor] = new(fieldChannelMs, false);
        }

        var log = new StringBuilder();
        var spentMs = new Dictionary<IAlignmentChannel, double>();
        for (int round = 0; round < rounds; round++)
        {
            AutoAlignmentEngine.PolishFarSideJunctions(
                plan, snapshots, snapshots, Reprocess, alignment, log,
                AutoAlignmentEngine.DefaultMaxDelayMs, decisions: null, spentMs);
        }
        return (alignment[farMid].DelayMs, alignment[farTwr].DelayMs, log.ToString());
    }

    [Fact]
    public void PolishFarSideJunctions_ScoresTheExactMoveToADspTickFromAnOffGridDelay()
    {
        // The descent rebases the field by unrounded amounts, so the mid can stand at 1.006 ms. The tick 1.01 is a
        // +0.004 ms move that aligns it with the tweeter; rounding that move to the grid read it as the incumbent.
        (double midDelay, double twrDelay, string log) = RunFarSidePolish(
            0.0, baseDelayMs: 1.01, junctionHz: 10_000, midOffsetMs: -0.004);

        Assert.Equal(1.01, midDelay, 9);
        Assert.Equal(1.01, twrDelay, 9);
        Assert.Contains("off the scene position", log);
    }

    [Fact]
    public void PolishFarSideJunctions_ReachIsATotalBudgetFromTheScenePosition()
    {
        // The polish alternates with the mono co-move: a second round must not walk the mid another eighth of a period.
        (double once, _, _) = RunFarSidePolish(0.50);
        (double twice, _, string log) = RunFarSidePolish(0.50, rounds: 2);

        Assert.Equal(once, twice, 9);
        Assert.InRange(Math.Abs(twice - 1.0), 0, 0.05 + 1e-9);
        Assert.Contains("Far-side polish R mid: kept", log);
    }

    [Fact]
    public void PolishFarSideJunctions_ClawsBackASmallSceneSkewExactly()
    {
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
        // The mid may close at most an eighth of its 2500 Hz period (0.05 ms): a polish, not a second alignment
        // pass. The tweeter is the bridge and holds the scene delta exactly.
        (double midDelay, double twrDelay, string log) = RunFarSidePolish(0.50);

        Assert.InRange(Math.Abs(midDelay - 1.0), 0, 0.05 + 1e-9);
        Assert.True(midDelay > 1.0, $"mid did not move later ({midDelay:0.000})");
        Assert.Equal(1.0, twrDelay);
        Assert.Contains("Far-side polish R twr: none, the bridge holds the scene delta", log);
    }

    [Fact]
    public void PolishFarSideJunctions_ReachFollowsTheJunctionPeriod()
    {
        // The same 0.50 ms skew under a 200 Hz split is inside the mid's reach (an eighth of 5 ms): it closes exactly.
        (double midDelay, double twrDelay, string _) = RunFarSidePolish(0.50, junctionHz: 200);

        Assert.Equal(1.0, twrDelay);
        Assert.InRange(midDelay, 1.495, 1.505);
    }

    [Fact]
    public void PolishFarSideJunctions_StaysInsideTheRealizableDelaySpan()
    {
        // The field already spans the whole range: the polish must not make it unrealizable.
        (double midDelay, double twrDelay, string _) = RunFarSidePolish(
            0.02, baseDelayMs: 50.0, withFieldFloor: true);

        Assert.InRange(midDelay, 0, 50.0);
        Assert.InRange(twrDelay, 0, 50.0);
    }
    [Fact]
    public void PolishFarSideJunctions_StaysInsideTheSpanFromTheEarlyEndToo()
    {
        // Mirror case: moving the earliest channels earlier also widens the span, so the guard reads the span.
        (double midDelay, double twrDelay, string _) = RunFarSidePolish(
            0.02, baseDelayMs: 1.0, withFieldFloor: true, fieldChannelMs: 51.0);

        Assert.InRange(51.0 - midDelay, 0, 50.0);
        Assert.InRange(51.0 - twrDelay, 0, 50.0);
    }
    [Fact]
    public void PolishFarSideJunctions_NeverMovesAMonoChannelOrTheBridge()
    {
        // The mono is shared with the reference side and the tweeter is the bridge: the skew stands.
        (double midDelay, double twrDelay, string _) =
            RunFarSidePolish(0.02, midIsMono: true);

        Assert.Equal(1.0, midDelay, 3);
        Assert.Equal(1.0, twrDelay, 3);
    }
}
