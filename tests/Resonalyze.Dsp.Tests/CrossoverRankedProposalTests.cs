using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverRankedProposalTests
{
    private const double SampleRate = 48_000;

    private static List<SignalPoint> FlatCurve(double levelDb = 0)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 256))
        {
            points.Add(new SignalPoint(frequency, levelDb));
        }

        return points;
    }

    private static List<SignalPoint> BandCurve(double lowHz, double highHz)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = 0;
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    private static CrossoverAutoSetupOptions Options(
        params CrossoverFilterFamily[] families) =>
        new(
            families.Length > 0
                ? families
                : [
                    CrossoverFilterFamily.LinkwitzRiley,
                    CrossoverFilterFamily.Butterworth,
                    CrossoverFilterFamily.Bessel
                ],
            20,
            20_000,
            IndependentSlopes: false,
            SampleRate);

    [Theory]
    [InlineData(83, 85)]
    [InlineData(97, 95)]
    [InlineData(100, 100)]
    [InlineData(104, 100)]
    [InlineData(996, 1_000)]
    [InlineData(1_024, 1_000)]
    [InlineData(1_730, 1_750)]
    [InlineData(12, 20)]
    [InlineData(6_420, 6_400)]
    public void RoundToLattice_SnapsToTheHumanFriendlySteps(
        double frequency,
        double expected)
    {
        Assert.Equal(expected, CrossoverAutoSetup.RoundToLattice(frequency));
    }

    [Fact]
    public void Propose_JunctionFrequenciesLandOnTheLattice()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 100), DriverType.Subwoofer),
            new(BandCurve(40, 500), DriverType.Woofer),
            new(BandCurve(250, 4_500), DriverType.Midrange),
            new(BandCurve(2_000, 20_000), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            sources, Options());

        foreach (CrossoverProposal proposal in proposals)
        {
            foreach (CrossoverEdge? edge in new[] { proposal.LowPassEdge, proposal.HighPassEdge })
            {
                if (edge is { } value)
                {
                    Assert.Equal(
                        CrossoverAutoSetup.RoundToLattice(value.FrequencyHz),
                        value.FrequencyHz,
                        precision: 6);
                }
            }
        }
    }

    // Below 300 Hz slopes steeper than 24 dB/oct are excluded (group delay);
    // no candidate in the whole ranked pool may carry one, even with steep
    // families available and independent slopes on.
    [Fact]
    public void ProposeRanked_NoSteepSlopesBelow300Hz()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 100), DriverType.Subwoofer),
            new(BandCurve(40, 500), DriverType.Woofer)
        };
        var options = new CrossoverAutoSetupOptions(
            [CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth],
            20,
            20_000,
            IndependentSlopes: true,
            SampleRate);

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, candidateCount: 50);

        Assert.NotEmpty(ranked);
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            foreach (CrossoverProposal proposal in candidate.Proposals)
            {
                foreach (CrossoverEdge? edge in new[]
                    { proposal.LowPassEdge, proposal.HighPassEdge })
                {
                    if (edge is { } value &&
                        value.FrequencyHz < CrossoverAutoSetup.SteepSlopeMinimumJunctionHz)
                    {
                        Assert.True(
                            value.SlopeDbPerOctave <=
                                CrossoverAutoSetup.LowJunctionMaxSlopeDbPerOctave,
                            $"{value.SlopeDbPerOctave} dB/oct at {value.FrequencyHz} Hz");
                    }
                }
            }
        }
    }

    // Independent oracle for the summation rule: flat unit drivers make the
    // expected sum a pure amplitude sum of the exact digital filter
    // magnitudes, computed here WITHOUT SummedResponseDb's internals. A
    // power-sum regression (the old Butterworth branch) fails this by ~3 dB
    // at the corner.
    [Fact]
    public void SummedResponseDb_IsThePlainAmplitudeSumOfTheFilteredChannels()
    {
        var channels = new List<AutoSetupSource>
        {
            new(FlatCurve(), DriverType.Woofer),
            new(FlatCurve(), DriverType.Tweeter)
        };
        var lowPass = new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 24));
        var highPass = new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 24));
        var proposals = new List<CrossoverProposal>
        {
            new(CrossoverKind.LowPass, null, lowPass.LowPassEdge, GainDb: -2),
            new(CrossoverKind.HighPass, highPass.HighPassEdge, null, GainDb: 0)
        };

        IReadOnlyList<SignalPoint> summed = CrossoverAutoSetup.SummedResponseDb(
            channels, proposals, SampleRate);

        foreach (SignalPoint point in summed)
        {
            double expected =
                Math.Pow(10, -2 / 20.0)
                    * CrossoverFilter.Response(lowPass, point.X, SampleRate).Magnitude
                + CrossoverFilter.Response(highPass, point.X, SampleRate).Magnitude;
            Assert.Equal(20 * Math.Log10(expected), point.Y, precision: 9);
        }
    }

    [Fact]
    public void ProposeRanked_AlwaysContainsTheConventional24Candidate()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 120), DriverType.Subwoofer),
            new(BandCurve(60, 900), DriverType.Midbass),
            new(BandCurve(2_000, 20_000), DriverType.Tweeter)
        };

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, Options(), candidateCount: 50);

        Assert.InRange(ranked.Count, 2, 50);
        Assert.Contains(ranked, candidate => candidate.IsConventional24);
        // Without IRs the penalty column stays empty and the ranking is the
        // magnitude score alone.
        Assert.All(ranked, candidate => Assert.Null(candidate.AchievabilityPenaltyDb));
        // Proposals come back in input order: the tweeter is the last channel.
        Assert.All(ranked, candidate => Assert.Null(candidate.Proposals[2].LowPassEdge));
    }

    private static List<SignalPoint> PeakedCurve(
        double lowHz,
        double highHz,
        double peakHz,
        double peakDb,
        double widthOctaves)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = peakDb * Math.Exp(
                -Math.Pow(Math.Log2(frequency / peakHz) / widthOctaves, 2));
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    // Per-junction pool options are each bounded against the descent optimum's
    // neighbours; combined independently, two junctions moved toward each
    // other can jointly break the half-octave minimum separation. A peaked
    // middle driver pulls both of its junctions inward — without the joint
    // check this configuration put an fc pair at ratio 1.375 (< sqrt 2) into
    // the ranked list.
    [Fact]
    public void ProposeRanked_KeepsTheMinimumJunctionSeparationInEveryCandidate()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 250), DriverType.Woofer),
            new(PeakedCurve(60, 1_500, 220, 10, 0.5), DriverType.Midbass),
            new(BandCurve(220, 20_000), DriverType.Midrange)
        };

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, Options(), candidateCount: 50);

        double separation = Math.Pow(2.0, 0.5);
        Assert.NotEmpty(ranked);
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            var crossovers = candidate.Proposals
                .Take(candidate.Proposals.Count - 1)
                .Select(proposal => proposal.LowPassEdge!.Value.FrequencyHz)
                .ToList();
            for (int j = 1; j < crossovers.Count; j++)
            {
                Assert.True(
                    crossovers[j] >= crossovers[j - 1] * separation * (1 - 1e-9),
                    $"fc {string.Join('/', crossovers)} breaks the separation.");
            }
        }
    }

    // The tie preference protects the ONE candidate the dedicated conventional
    // run built (all slopes 24 dB/oct, Linkwitz-Riley when allowed) — a pool
    // candidate that merely landed on all-24 slopes, or a Butterworth/Bessel
    // 24 mix, must not carry the flag. Before the signature match this
    // configuration flagged five candidates, one of them pure Butterworth.
    [Fact]
    public void ProposeRanked_FlagsOnlyTheDedicatedConventionalCandidate()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 250), DriverType.Woofer),
            new(PeakedCurve(60, 1_500, 260, 10, 0.3), DriverType.Midbass),
            new(BandCurve(220, 20_000), DriverType.Midrange)
        };

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, Options(), candidateCount: 50);

        RankedCrossoverProposal conventional =
            Assert.Single(ranked, candidate => candidate.IsConventional24);
        foreach (CrossoverProposal proposal in conventional.Proposals)
        {
            foreach (CrossoverEdge? edge in new[]
                { proposal.LowPassEdge, proposal.HighPassEdge })
            {
                if (edge is { } value)
                {
                    Assert.Equal(CrossoverFilterFamily.LinkwitzRiley, value.Family);
                    Assert.Equal(24, value.SlopeDbPerOctave);
                }
            }
        }
    }

    // With ideal impulse drivers a matched LR24 handover is losslessly
    // alignable, so the post-check must hand the win to the conventional
    // candidate with a near-zero penalty.
    [Fact]
    public void ProposeRanked_WithImpulseResponsesPrefersAnAchievableHandover()
    {
        var sources = new List<AutoSetupSource>
        {
            new(FlatCurve(), DriverType.Woofer),
            new(FlatCurve(), DriverType.Tweeter)
        };
        Complex[] MakeDelta()
        {
            var ir = new Complex[16_384];
            ir[2_000] = Complex.One;
            return ir;
        }

        IReadOnlyList<RankedCrossoverProposal> ranked = CrossoverAutoSetup.ProposeRanked(
            sources,
            Options(),
            [MakeDelta(), MakeDelta()],
            candidateCount: 20);

        Assert.All(ranked, candidate => Assert.NotNull(candidate.AchievabilityPenaltyDb));
        Assert.True(ranked[0].IsConventional24, "The conventional candidate should win on ideal drivers.");
        Assert.True(
            ranked[0].AchievabilityPenaltyDb < 1.0,
            $"Achievability penalty {ranked[0].AchievabilityPenaltyDb:0.00} dB should be near zero.");
    }
}
