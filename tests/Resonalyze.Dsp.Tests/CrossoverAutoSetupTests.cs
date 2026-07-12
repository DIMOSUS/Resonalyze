namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverAutoSetupTests
{
    private const double SampleRate = 48_000;

    // A synthetic driver curve on a log grid: flat at `levelDb` inside the band,
    // rolling off at 24 dB/octave beyond both edges — the shape the band and
    // crossover analysis has to read.
    private static List<SignalPoint> BandCurve(
        double lowHz,
        double highHz,
        double levelDb)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = levelDb;
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
        double minHz = 20,
        double maxHz = 20_000,
        bool independentSlopes = false,
        params CrossoverFilterFamily[] families) =>
        new(
            families.Length > 0
                ? families
                : [
                    CrossoverFilterFamily.LinkwitzRiley,
                    CrossoverFilterFamily.Butterworth,
                    CrossoverFilterFamily.Bessel
                ],
            minHz,
            maxHz,
            independentSlopes,
            SampleRate);

    // Peak-to-peak ripple (dB) of the predicted magnitude sum over the system's
    // passband — the quantity the optimizer is trying to shrink.
    private static double SumRippleDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals)
    {
        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            channels.OrderBy(c => c.Type).First().MagnitudeDb);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            channels.OrderBy(c => c.Type).Last().MagnitudeDb);

        // Trim half an octave inside the outer band edges: the outermost drivers'
        // own roll-off skirts are unavoidable and not what the crossover controls.
        double trim = Math.Pow(2.0, 0.5);
        var window = CrossoverAutoSetup
            .SummedResponseDb(channels, proposals, SampleRate)
            .Where(point => point.X >= low.LowHz * trim && point.X <= high.HighHz / trim)
            .Select(point => point.Y)
            .ToList();
        return window.Max() - window.Min();
    }

    [Fact]
    public void Propose_HandlesAFourWaySystem()
    {
        // Sub / woofer / midrange / tweeter — the four-way case the wizard now
        // carries end to end. Three junctions, ordered low to high, with every
        // handover inside both classes' sensible range and a sane summed ripple.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 100, 0), DriverType.Subwoofer),
            new(BandCurve(40, 500, 0), DriverType.Woofer),
            new(BandCurve(250, 4_500, 0), DriverType.Midrange),
            new(BandCurve(2_000, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            sources, Options());

        Assert.Equal(4, proposals.Count);
        double subToWoofer = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double wooferToMid = proposals[1].LowPassEdge!.Value.FrequencyHz;
        double midToTweeter = proposals[2].LowPassEdge!.Value.FrequencyHz;
        Assert.True(subToWoofer < wooferToMid);
        Assert.True(wooferToMid < midToTweeter);
        Assert.InRange(subToWoofer, 40, 80);
        Assert.InRange(wooferToMid, 250, 500);
        // The placement heuristics cross the mid/tweeter as low as the tweeter's
        // sensible floor (1.5 kHz) and its measured band allow, out of the 2–4 kHz
        // ear-sensitivity band; a low tweeter handover must stay steep (>= 24).
        Assert.InRange(midToTweeter, 1_500, 4_000);
        if (midToTweeter < CrossoverAutoSetup.TweeterProtectionHz)
        {
            Assert.True(proposals[3].HighPassEdge!.Value.SlopeDbPerOctave >= 24);
        }
        Assert.Null(proposals[3].LowPassEdge);
        Assert.True(SumRippleDb(sources, proposals) < 6.0);
    }

    [Fact]
    public void Propose_CrossesACapableTweeterLowAndKeepsItSteep()
    {
        // A tweeter that measures clean down to ~1.2 kHz: the placement
        // heuristics (avoid the 2–4 kHz ear band, cross a wide overlap low) pull
        // its handover below the ear band, and the tweeter protection keeps that
        // low handover steep (>= 24 dB/oct) so the tweeter is not overdriven.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(60, 900, 0), DriverType.Midbass),
            new(BandCurve(250, 5_000, 0), DriverType.Midrange),
            new(BandCurve(1_200, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        CrossoverEdge tweeterHighPass = proposals[2].HighPassEdge!.Value;
        Assert.True(
            tweeterHighPass.FrequencyHz < 2_000,
            $"mid/tweeter handover {tweeterHighPass.FrequencyHz:0} Hz was not pulled below the ear band");
        Assert.True(
            tweeterHighPass.FrequencyHz >= 1_500,
            $"handover {tweeterHighPass.FrequencyHz:0} Hz dropped below the tweeter floor");
        Assert.True(
            tweeterHighPass.SlopeDbPerOctave >= 24,
            $"low tweeter handover slope {tweeterHighPass.SlopeDbPerOctave} is not steep");
    }

    [Fact]
    public void Propose_MidbassHandoversStayInItsSensibleRange()
    {
        // A three-way with an explicit Midbass driver exercises the Midbass row of
        // SensibleRange (80-500 Hz), which the other systems never hit. Its lower
        // handover to the sub lands inside that range; the upper handover to the
        // tweeter is pulled higher by the tweeter's own range but must stay ordered.
        var sub = new AutoSetupSource(BandCurve(20, 120, 0), DriverType.Subwoofer);
        var midbass = new AutoSetupSource(BandCurve(100, 800, 0), DriverType.Midbass);
        var tweeter = new AutoSetupSource(BandCurve(2_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [sub, midbass, tweeter], Options());

        Assert.Equal(3, proposals.Count);
        double subToMidbass = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double midbassToTweeter = proposals[1].LowPassEdge!.Value.FrequencyHz;
        Assert.InRange(subToMidbass, 80, 500); // midbass sensible-range low side
        Assert.True(subToMidbass < midbassToTweeter, "Handovers must stay ordered.");
        Assert.InRange(midbassToTweeter, 500, 2_000);
    }

    [Fact]
    public void EstimateBand_ReadsEdgesLevelAndType()
    {
        Assert.Equal(
            DriverType.Subwoofer,
            CrossoverAutoSetup.EstimateBand(BandCurve(22, 78, 0)).SuggestedType);

        DriverBandEstimate woofer = CrossoverAutoSetup.EstimateBand(
            BandCurve(50, 200, -12));
        Assert.Equal(DriverType.Woofer, woofer.SuggestedType);
        Assert.InRange(woofer.LowHz, 20, 60);
        Assert.InRange(woofer.LevelDb, -13, -11);

        Assert.Equal(
            DriverType.Midbass,
            CrossoverAutoSetup.EstimateBand(BandCurve(100, 450, 0)).SuggestedType);

        Assert.Equal(
            DriverType.Midrange,
            CrossoverAutoSetup.EstimateBand(BandCurve(300, 3_500, 0)).SuggestedType);

        DriverBandEstimate tweeter = CrossoverAutoSetup.EstimateBand(
            BandCurve(2_500, 18_000, -3));
        Assert.Equal(DriverType.Tweeter, tweeter.SuggestedType);
        Assert.InRange(tweeter.LowHz, 1_800, 2_600);
    }

    [Fact]
    public void Propose_WooferToMidrange_KeepsTheCrossoverInTheWooferRange()
    {
        // Regression: a woofer whose measured response extends into the midband
        // (its -8 dB point sits near 850 Hz) must still hand over to the midrange
        // down in the woofer's sensible range (~250 Hz), not up at 850 Hz.
        var woofer = new AutoSetupSource(BandCurve(35, 850, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, midrange],
            Options());

        double crossover = proposals[0].LowPassEdge!.Value.FrequencyHz;
        Assert.InRange(crossover, 200, 300);
    }

    [Fact]
    public void Propose_SubwooferToWoofer_CrossesInTheirOverlap()
    {
        // Subwoofer (20-80 Hz) and woofer (40-250 Hz) overlap only at 40-80 Hz;
        // the handover must land there, not up in the woofer's midband skirt.
        var sub = new AutoSetupSource(BandCurve(20, 120, 0), DriverType.Subwoofer);
        var woofer = new AutoSetupSource(BandCurve(50, 600, 0), DriverType.Woofer);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [sub, woofer],
            Options());

        double crossover = proposals[0].LowPassEdge!.Value.FrequencyHz;
        Assert.InRange(crossover, 40, 80);
    }

    [Fact]
    public void Propose_TwoWay_SplitsInsideTheOverlapWithAllowedFilters()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(families: CrossoverFilterFamily.LinkwitzRiley));

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[1].Kind);
        Assert.Null(proposals[0].HighPassEdge);
        Assert.Null(proposals[1].LowPassEdge);

        double lowPassHz = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double highPassHz = proposals[1].HighPassEdge!.Value.FrequencyHz;
        Assert.Equal(lowPassHz, highPassHz);
        Assert.InRange(lowPassHz, 1_000, 2_000);
        Assert.Equal(
            CrossoverFilterFamily.LinkwitzRiley,
            proposals[0].LowPassEdge!.Value.Family);
    }

    [Fact]
    public void Propose_OnlyUsesTheAllowedFamilies()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(families: CrossoverFilterFamily.Bessel));

        Assert.Equal(CrossoverFilterFamily.Bessel, proposals[0].LowPassEdge!.Value.Family);
        Assert.Equal(CrossoverFilterFamily.Bessel, proposals[1].HighPassEdge!.Value.Family);
    }

    [Fact]
    public void Propose_KeepsTheCrossoverInsideTheRequestedRange()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 6_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(700, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(minHz: 2_500, maxHz: 4_000));

        double crossover = proposals[0].LowPassEdge!.Value.FrequencyHz;
        Assert.InRange(crossover, 2_500, 4_000);
    }

    [Fact]
    public void Propose_GainsAreCutOnly_AndReferenceTheLoudestChannel()
    {
        // The tweeter plays 6 dB louder; it gets the cut, the woofer stays put.
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, -6), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options());

        Assert.True(proposals.All(proposal => proposal.GainDb <= 0.0001));
        Assert.Contains(proposals, proposal => Math.Abs(proposal.GainDb) < 0.0001);
        // The tweeter is the loud one, so it must be the channel that gets cut.
        Assert.True(proposals[1].GainDb < proposals[0].GainDb);
    }

    [Fact]
    public void Propose_LevelMatchedFlatDrivers_SumsFlat()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 2_500, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(900, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options());

        Assert.True(
            SumRippleDb([woofer, tweeter], proposals) < 3.0,
            "The optimized two-way sum should be flat within a few dB.");
    }

    [Fact]
    public void Propose_IsAtLeastAsFlatAsAFixedLr24Split()
    {
        // A woofer that already rolls off gently well below where a flat tweeter
        // takes over: a fixed LR24 electrical split overshoots the acoustic slope
        // and dips. The optimizer is free to pick gentler/other filters and must
        // not do worse than the naive LR24-at-the-intersection baseline.
        var woofer = new AutoSetupSource(BandCurve(40, 1_200, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_500, 20_000, 0), DriverType.Tweeter);
        var channels = new[] { woofer, tweeter };

        IReadOnlyList<CrossoverProposal> optimized = CrossoverAutoSetup.Propose(
            channels,
            Options());

        var baseline = new[]
        {
            new CrossoverProposal(
                CrossoverKind.LowPass,
                null,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_350, 24),
                0),
            new CrossoverProposal(
                CrossoverKind.HighPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 1_350, 24),
                null,
                0)
        };

        Assert.True(
            SumRippleDb(channels, optimized) <= SumRippleDb(channels, baseline) + 0.25,
            "The optimizer must not be flatter-losing against a fixed LR24 split.");
    }

    [Fact]
    public void Propose_ThreeWay_GivesTheMiddleChannelABandPass_InInputOrder()
    {
        // Input deliberately out of band order; results come back in input order.
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [tweeter, woofer, midrange],
            Options());

        Assert.Equal(CrossoverKind.HighPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.LowPass, proposals[1].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[2].Kind);

        double lowSplit = proposals[1].LowPassEdge!.Value.FrequencyHz;
        double highSplit = proposals[0].HighPassEdge!.Value.FrequencyHz;
        Assert.Equal(lowSplit, proposals[2].HighPassEdge!.Value.FrequencyHz);
        Assert.Equal(highSplit, proposals[2].LowPassEdge!.Value.FrequencyHz);
        Assert.True(lowSplit < highSplit);
    }

    [Fact]
    public void Propose_IndependentSlopes_MayDifferAcrossAJunction()
    {
        // A woofer with a lot of natural high-end roll-off paired with a tweeter
        // that stays flat: independent slopes let the two sides differ.
        var woofer = new AutoSetupSource(BandCurve(40, 900, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_500, 20_000, 0), DriverType.Tweeter);
        var channels = new[] { woofer, tweeter };

        IReadOnlyList<CrossoverProposal> matched = CrossoverAutoSetup.Propose(
            channels,
            Options(independentSlopes: false));
        IReadOnlyList<CrossoverProposal> independent = CrossoverAutoSetup.Propose(
            channels,
            Options(independentSlopes: true));

        // Matched keeps both sides equal; independent is allowed to differ and
        // must never come out worse.
        Assert.Equal(
            matched[0].LowPassEdge!.Value.SlopeDbPerOctave,
            matched[1].HighPassEdge!.Value.SlopeDbPerOctave);
        Assert.True(
            SumRippleDb(channels, independent) <= SumRippleDb(channels, matched) + 0.25);
    }

    [Fact]
    public void Propose_LowerLimit_AddsASubsonicHighPassToTheWoofer()
    {
        // The woofer reaches well below 75 Hz; a 75 Hz lower limit must band-limit
        // it with a high-pass, turning it into a band-pass.
        var woofer = new AutoSetupSource(BandCurve(28, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> limited = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(minHz: 75));

        Assert.Equal(CrossoverKind.BandPass, limited[0].Kind);
        Assert.NotNull(limited[0].HighPassEdge);
        Assert.Equal(75, limited[0].HighPassEdge!.Value.FrequencyHz, 0);

        // Left at the full range there is nothing to band-limit.
        IReadOnlyList<CrossoverProposal> full = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(minHz: 20));
        Assert.Equal(CrossoverKind.LowPass, full[0].Kind);
        Assert.Null(full[0].HighPassEdge);
    }

    [Fact]
    public void Propose_UpperLimit_AddsABrickwallLowPassToTheTweeter()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> limited = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(maxHz: 15_000));

        Assert.Equal(CrossoverKind.BandPass, limited[1].Kind);
        Assert.NotNull(limited[1].LowPassEdge);
        Assert.Equal(15_000, limited[1].LowPassEdge!.Value.FrequencyHz, 0);
    }

    [Fact]
    public void Propose_LowerLimitAboveTheWooferEdge_AddsNothing()
    {
        // The woofer already rolls off above the 75 Hz limit — nothing to cut.
        var woofer = new AutoSetupSource(BandCurve(120, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(minHz: 75));

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Null(proposals[0].HighPassEdge);
    }

    [Fact]
    public void Propose_NeverUsesImpracticallyShallowSlopes()
    {
        var woofer = new AutoSetupSource(BandCurve(30, 600, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, midrange, tweeter],
            Options());

        IEnumerable<CrossoverEdge> edges = proposals
            .SelectMany(proposal => new[] { proposal.HighPassEdge, proposal.LowPassEdge })
            .Where(edge => edge is not null)
            .Select(edge => edge!.Value);
        Assert.All(edges, edge => Assert.True(edge.SlopeDbPerOctave >= 12));
    }

    [Fact]
    public void Propose_WideOverlap_PrefersSteeperThanTheFloor()
    {
        // Two flat drivers overlapping across three octaves: pure flatness is
        // indifferent to the slope, but the overlap penalty makes the engineer's
        // choice — a steeper filter that narrows the overlap — win.
        var woofer = new AutoSetupSource(BandCurve(40, 5_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(families: CrossoverFilterFamily.LinkwitzRiley));

        Assert.True(
            proposals[0].LowPassEdge!.Value.SlopeDbPerOctave >= 24,
            "The overlap penalty should push past the shallow floor when drivers " +
            "overlap widely.");
    }

    [Fact]
    public void Propose_RejectsDuplicateTypesAndTooFewChannels()
    {
        var a = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
        var b = new AutoSetupSource(BandCurve(50, 2_500, 0), DriverType.Woofer);

        Assert.Throws<ArgumentException>(
            () => CrossoverAutoSetup.Propose([a, b], Options()));
        Assert.Throws<ArgumentException>(
            () => CrossoverAutoSetup.Propose([a], Options()));
    }
}
