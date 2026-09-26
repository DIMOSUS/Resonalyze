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
            SampleRate,
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

    // The low-frequency guard is on the filter group delay itself, not a fixed frequency.
    [Fact]
    public void ProposeRanked_KeepsEveryCrossoverWithinTheGroupDelayBudget()
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
            SampleRate,
            SampleRate);

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, candidateCount: 50);

        Assert.NotEmpty(ranked);
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            foreach (CrossoverProposal proposal in candidate.Proposals)
            {
                if (proposal.LowPassEdge is { } lp)
                {
                    AssertWithinGroupDelayBudget(lp, highPass: false);
                }

                if (proposal.HighPassEdge is { } hp)
                {
                    AssertWithinGroupDelayBudget(hp, highPass: true);
                }
            }
        }
    }

    /// <summary>The budget is read at the corner, and a split moves both edges off it — group delay runs as 1/fc,
    /// so the lower edge carries more of it than the corner does. A guard rather than a reproduction: the band where
    /// this bites is narrow (a corner between about 9.2 and 10 ms), and no fixture here lands in it.</summary>
    [Fact]
    public void ASplitJunction_KeepsBothEdgesWithinTheGroupDelayBudget()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 200), DriverType.Subwoofer),
            new(BandCurve(80, 1_000), DriverType.Woofer)
        };
        var options = new CrossoverAutoSetupOptions(
            [CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth],
            20,
            20_000,
            IndependentSlopes: true,
            SampleRate,
            SampleRate,
            SubElevationDb: null,
            [new JunctionSearchWindow(AllowSplitCorners: true)]);

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, candidateCount: 50);

        Assert.NotEmpty(ranked);
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            foreach (CrossoverProposal proposal in candidate.Proposals)
            {
                if (proposal.LowPassEdge is { } lp)
                {
                    AssertWithinGroupDelayBudget(lp, highPass: false);
                }

                if (proposal.HighPassEdge is { } hp)
                {
                    AssertWithinGroupDelayBudget(hp, highPass: true);
                }
            }
        }
    }

    private static void AssertWithinGroupDelayBudget(CrossoverEdge edge, bool highPass)
    {
        double groupDelay = CrossoverFilter.MaxGroupDelaySeconds(edge, highPass, SampleRate);
        Assert.True(
            groupDelay <= CrossoverAutoSetup.MaxCrossoverGroupDelaySeconds + 1e-9,
            $"{edge.SlopeDbPerOctave} dB/oct at {edge.FrequencyHz:0} Hz = " +
            $"{groupDelay * 1000:0.0} ms group delay");
    }

    // The 24 dB/oct floor is always admitted; below budget every path (seed, descent, conventional) uses exactly the floor.
    [Fact]
    public void ProposeRanked_AtASubBudgetJunction_FallsBackToTheFloorConsistently()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 35), DriverType.Subwoofer),
            new(BandCurve(30, 500), DriverType.Woofer)
        };
        var options = new CrossoverAutoSetupOptions(
            [CrossoverFilterFamily.LinkwitzRiley],
            20,
            20_000,
            IndependentSlopes: true,
            SampleRate,
            SampleRate);

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, candidateCount: 50);

        Assert.NotEmpty(ranked);
        Assert.Contains(ranked, candidate => candidate.IsConventional24);

        bool sawFloorAboveBudget = false;
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            foreach (CrossoverProposal proposal in candidate.Proposals)
            {
                foreach ((CrossoverEdge? edge, bool highPass) in new[]
                    { (proposal.LowPassEdge, false), (proposal.HighPassEdge, true) })
                {
                    if (edge is not { } value)
                    {
                        continue;
                    }

                    double gd = CrossoverFilter.MaxGroupDelaySeconds(value, highPass, SampleRate);
                    if (gd > CrossoverAutoSetup.MaxCrossoverGroupDelaySeconds)
                    {
                        int floor = CrossoverFilter.SupportedSlopes(value.Family)
                            .Where(slope => slope >= 24)
                            .Min();
                        Assert.Equal(floor, value.SlopeDbPerOctave);
                        sawFloorAboveBudget = true;
                    }
                }
            }
        }

        Assert.True(
            sawFloorAboveBudget,
            "Expected a junction low enough that even the floor slope exceeds the budget.");
    }

    // Independent oracle: a flat magnitude has zero minimum phase, so flat drivers leave the filters' own phase as
    // the whole of the sum. That is the model — the old assertion was a plain AMPLITUDE sum, which only matched
    // because 24 dB/oct happens to put the two sides in phase.
    [Fact]
    public void SummedResponseDb_IsTheComplexSumOfTheFilteredChannels()
    {
        var channels = new List<AutoSetupSource>
        {
            new(FlatCurve(), DriverType.Woofer),
            new(FlatCurve(), DriverType.Tweeter)
        };
        var lowPass = new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 18));
        var highPass = new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_000, 18));
        var proposals = new List<CrossoverProposal>
        {
            new(CrossoverKind.LowPass, null, lowPass.LowPassEdge, GainDb: -2),
            new(CrossoverKind.HighPass, highPass.HighPassEdge, null, GainDb: 0)
        };

        IReadOnlyList<SignalPoint> summed = CrossoverAutoSetup.SummedResponseDb(
            channels, proposals, SampleRate, SampleRate);

        foreach (SignalPoint point in summed)
        {
            Complex expected =
                Math.Pow(10, -2 / 20.0)
                    * CrossoverFilter.Response(lowPass, point.X, SampleRate)
                + CrossoverFilter.Response(highPass, point.X, SampleRate);
            Assert.Equal(20 * Math.Log10(expected.Magnitude), point.Y, precision: 9);
        }
    }

    /// <summary>The polarity rule, pinned on the model rather than on a table in the code. For a Butterworth pair of
    /// order N = slope/6 the ratio HP/LP is (jω/ωc)^N at EVERY frequency, so the two sides are exactly in phase when
    /// N ≡ 0 (mod 4), exactly anti-phase when N ≡ 2 (mod 4) — 12 and 36 dB/oct — and in quadrature for odd N, where
    /// polarity cannot move the summed magnitude at all.</summary>
    [Theory]
    [InlineData(12, true)]
    [InlineData(24, false)]
    [InlineData(36, true)]
    [InlineData(48, false)]
    public void TheSummedResponse_NeedsTheInversionTheCrossoverOrderImplies(
        int slopeDbPerOctave,
        bool expectInverted)
    {
        (double upright, double inverted) = PolarityPair(slopeDbPerOctave);
        double wanted = expectInverted ? inverted : upright;
        double other = expectInverted ? upright : inverted;
        Assert.True(
            wanted > other + 20,
            $"{slopeDbPerOctave} dB/oct: upright {upright:0.0} dB against inverted {inverted:0.0} dB " +
            $"at the corner, which does not choose a polarity.");
    }

    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    [InlineData(30)]
    [InlineData(42)]
    public void AnOddOrderPair_IsInQuadrature_SoPolarityCannotMoveTheSum(int slopeDbPerOctave)
    {
        (double upright, double inverted) = PolarityPair(slopeDbPerOctave);
        Assert.Equal(upright, inverted, precision: 6);
    }

    // Level at the corner with the upper channel upright and inverted, on flat drivers.
    private static (double Upright, double Inverted) PolarityPair(int slopeDbPerOctave)
    {
        const double cornerHz = 1_000;
        var channels = new List<AutoSetupSource>
        {
            new(FlatCurve(), DriverType.Woofer),
            new(FlatCurve(), DriverType.Tweeter)
        };
        var lowPass = new CrossoverEdge(
            CrossoverFilterFamily.Butterworth, cornerHz, slopeDbPerOctave);
        var highPass = new CrossoverEdge(
            CrossoverFilterFamily.Butterworth, cornerHz, slopeDbPerOctave);

        double At(bool invert)
        {
            var proposals = new List<CrossoverProposal>
            {
                new(CrossoverKind.LowPass, null, lowPass, GainDb: 0),
                new(CrossoverKind.HighPass, highPass, null, GainDb: 0, InvertPolarity: invert)
            };
            return CrossoverAutoSetup
                .SummedResponseDb(channels, proposals, SampleRate, SampleRate)
                .MinBy(point => Math.Abs(Math.Log(point.X / cornerHz)))!
                .Y;
        }

        return (At(false), At(true));
    }

    [Fact]
    [Trait("Category", "Slow")]
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

    // Independently bounded junctions can jointly break the half-octave separation (ratio 1.375 < √2 seen).
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

    // Only the dedicated conventional run carries the flag, not a pool candidate that happens to be all-24.
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

    // Matched slopes tie each driver's two shoulders, not the whole system.
    [Fact]
    [Trait("Category", "Slow")]
    public void ProposeRanked_MatchedSlopes_TieEachDriversTwoShoulders()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 150), DriverType.Subwoofer),
            new(BandCurve(50, 700), DriverType.Woofer),
            new(BandCurve(250, 5_000), DriverType.Midrange),
            new(BandCurve(1_800, 20_000), DriverType.Tweeter)
        };
        var options = new CrossoverAutoSetupOptions(
            [CrossoverFilterFamily.LinkwitzRiley, CrossoverFilterFamily.Butterworth],
            20,
            20_000,
            IndependentSlopes: false,
            SampleRate,
            SampleRate);

        IReadOnlyList<RankedCrossoverProposal> ranked =
            CrossoverAutoSetup.ProposeRanked(sources, options, candidateCount: 50);

        Assert.NotEmpty(ranked);
        foreach (RankedCrossoverProposal candidate in ranked)
        {
            foreach (CrossoverProposal proposal in candidate.Proposals)
            {
                if (proposal.HighPassEdge is { } hp && proposal.LowPassEdge is { } lp)
                {
                    Assert.Equal(hp.SlopeDbPerOctave, lp.SlopeDbPerOctave);
                }
            }
        }
    }

    [Fact]
    public void Propose_MatchedSlopes_LetDifferentDriversTakeDifferentSlopes()
    {
        // Matched slopes bind a CHANNEL's two shoulders, never the whole chain: the sub junction is held by the
        // group-delay budget where the tweeter junction is not, so the chain must end up with more than one slope.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 70), DriverType.Subwoofer),
            new(PeakedCurve(120, 20_000, 1_600, 12, 0.6), DriverType.Midrange),
            new(BandCurve(2_500, 20_000), DriverType.Tweeter)
        };
        var options = new CrossoverAutoSetupOptions(
            [CrossoverFilterFamily.LinkwitzRiley],
            20,
            20_000,
            IndependentSlopes: false,
            SampleRate,
            SampleRate);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, options);

        foreach (CrossoverProposal proposal in proposals)
        {
            if (proposal.HighPassEdge is { } hp && proposal.LowPassEdge is { } lp)
            {
                Assert.Equal(hp.SlopeDbPerOctave, lp.SlopeDbPerOctave);
            }
        }

        // The chain does not settle on one slope for everybody.
        var slopes = proposals
            .SelectMany(proposal => new[] { proposal.HighPassEdge, proposal.LowPassEdge })
            .Where(edge => edge is not null)
            .Select(edge => edge!.Value.SlopeDbPerOctave)
            .Distinct()
            .ToList();
        Assert.True(slopes.Count > 1, $"Every edge took the same slope: {slopes[0]} dB/oct.");

        // The budget holds the sub; where under it the slope lands is the search's call.
        CrossoverEdge subLowPass = proposals[0].LowPassEdge!.Value;
        Assert.True(
            CrossoverFilter.MaxGroupDelaySeconds(subLowPass, highPass: false, SampleRate)
                <= CrossoverAutoSetup.MaxCrossoverGroupDelaySeconds,
            $"The sub low-pass at {subLowPass.SlopeDbPerOctave} dB/oct is over the group-delay budget.");
    }

    // Fit levels mid and tweeter to each other, keeps the bass raw by default, leaves a sub-target midbass alone (cut-only).
    private static List<AutoSetupSource> TargetCurveSources()
    {
        List<SignalPoint> Shelf(double lowHz, double highHz, double levelDb) =>
            BandCurve(lowHz, highHz).Select(p => new SignalPoint(p.X, p.Y + levelDb)).ToList();

        return
        [
            new AutoSetupSource(Shelf(20, 90, 6), DriverType.Subwoofer),
            new AutoSetupSource(Shelf(70, 400, -6), DriverType.Midbass),
            new AutoSetupSource(Shelf(300, 5_000, -18), DriverType.Midrange),
            new AutoSetupSource(Shelf(2_000, 20_000, -15), DriverType.Tweeter),
        ];
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ApplyTargetCurveGains_LevelsMidTweeterKeepsBassCutsOnlyDownward()
    {
        List<AutoSetupSource> sources = TargetCurveSources();
        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        Assert.All(proposals, p => Assert.True(p.GainDb <= 0.0 + 1e-9, $"{p.GainDb} dB > 0"));
        Assert.Contains(proposals, p => Math.Abs(p.GainDb) < 1e-9);

        Assert.Equal(0.0, proposals[2].GainDb, precision: 6);
        Assert.True(proposals[3].GainDb < -0.5, $"tweeter {proposals[3].GainDb} not attenuated");

        Assert.True(Math.Abs(proposals[0].GainDb) < 1.0, $"sub moved {proposals[0].GainDb} dB");
    }

    [Fact]
    public void ApplyTargetCurveGains_TrimmingTheElevationCutsTheBass()
    {
        List<AutoSetupSource> sources = TargetCurveSources();
        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());
        double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
            sources, proposals, SampleRate);
        Assert.True(measured > 6, $"measured elevation {measured} unexpectedly small");

        IReadOnlyList<CrossoverProposal> flat = CrossoverAutoSetup.ApplyTargetCurveGains(
            sources, proposals, SampleRate, subElevationDb: 0);
        IReadOnlyList<CrossoverProposal> half = CrossoverAutoSetup.ApplyTargetCurveGains(
            sources, proposals, SampleRate, subElevationDb: measured / 2);

        Assert.True(flat[0].GainDb < half[0].GainDb, "flat sub not cut below half");
        Assert.True(half[0].GainDb < proposals[0].GainDb + 1e-9, "half sub not cut below default");
        Assert.Equal(proposals[2].GainDb, flat[2].GainDb, precision: 6);
        IReadOnlyList<CrossoverProposal> over = CrossoverAutoSetup.ApplyTargetCurveGains(
            sources, proposals, SampleRate, subElevationDb: measured + 20);
        Assert.Equal(proposals[0].GainDb, over[0].GainDb, precision: 6);
        Assert.All(over, p => Assert.True(p.GainDb <= 0.0 + 1e-9));
    }

    // No sub: the woofer anchors the bass like a sub instead of being cut to the reference (−24 dB in the field).
    [Fact]
    public void ApplyTargetCurveGains_WithoutASubTheWooferAnchorsTheBass()
    {
        List<SignalPoint> Shelf(double lowHz, double highHz, double levelDb) =>
            BandCurve(lowHz, highHz).Select(p => new SignalPoint(p.X, p.Y + levelDb)).ToList();
        var sources = new List<AutoSetupSource>
        {
            new(Shelf(30, 260, 12), DriverType.Woofer),
            new(Shelf(200, 5_000, 0), DriverType.Midrange),
            new(Shelf(2_000, 20_000, 2), DriverType.Tweeter),
        };
        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
            sources, proposals, SampleRate);
        Assert.True(measured > 8, $"measured elevation {measured} unexpectedly small");
        Assert.True(Math.Abs(proposals[0].GainDb) < 1.0, $"woofer moved {proposals[0].GainDb} dB");
        Assert.Equal(0.0, proposals[1].GainDb, precision: 6);
        Assert.True(proposals[2].GainDb < -0.5, $"tweeter {proposals[2].GainDb} not attenuated");
        Assert.All(proposals, p => Assert.True(p.GainDb <= 1e-9));

        IReadOnlyList<CrossoverProposal> flat = CrossoverAutoSetup.ApplyTargetCurveGains(
            sources, proposals, SampleRate, subElevationDb: 0);
        Assert.True(flat[0].GainDb < -8, $"flat woofer {flat[0].GainDb} not cut");
        Assert.Equal(proposals[1].GainDb, flat[1].GainDb, precision: 6);
    }

    // A woofer quieter than mid/treble has no elevation to preserve: level down to it.
    [Fact]
    public void ApplyTargetCurveGains_AQuietWooferWithoutASubStaysLevelMatched()
    {
        List<SignalPoint> Shelf(double lowHz, double highHz, double levelDb) =>
            BandCurve(lowHz, highHz).Select(p => new SignalPoint(p.X, p.Y + levelDb)).ToList();
        var sources = new List<AutoSetupSource>
        {
            new(Shelf(40, 2_000, -6), DriverType.Woofer),
            new(Shelf(1_000, 20_000, 0), DriverType.Tweeter),
        };
        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        double measured = CrossoverAutoSetup.MeasuredSubElevationDb(
            sources, proposals, SampleRate);
        Assert.Equal(0.0, measured, precision: 6);
        Assert.True(Math.Abs(proposals[0].GainDb) < 0.5, $"woofer moved {proposals[0].GainDb} dB");
        Assert.True(proposals[1].GainDb < proposals[0].GainDb, "tweeter not cut to the woofer");
    }

    // Ideal impulses make matched LR24 losslessly alignable: the conventional candidate wins with ~zero penalty.
    [Fact]
    [Trait("Category", "Slow")]
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
