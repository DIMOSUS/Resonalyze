namespace Resonalyze.Dsp.Tests;

public sealed class CrossoverAutoSetupTests
{
    private const double SampleRate = 48_000;

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
            SampleRate,
            SampleRate);

    private static double SumRippleDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals)
    {
        DriverBandEstimate low = CrossoverAutoSetup.EstimateBand(
            channels.OrderBy(c => c.Type).First().MagnitudeDb);
        DriverBandEstimate high = CrossoverAutoSetup.EstimateBand(
            channels.OrderBy(c => c.Type).Last().MagnitudeDb);

        // Trim half an octave inside the outer edges: the outermost drivers' skirts are not the crossover's doing.
        double trim = Math.Pow(2.0, 0.5);
        var window = CrossoverAutoSetup
            .SummedResponseDb(channels, proposals, SampleRate, SampleRate)
            .Where(point => point.X >= low.LowHz * trim && point.X <= high.HighHz / trim)
            .Select(point => point.Y)
            .ToList();
        return window.Max() - window.Min();
    }

    [Fact]
    public void Propose_HandlesAFourWaySystem()
    {
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
        // Placement crosses woofer/mid as low as the midrange floor (200 Hz), below cone breakup.
        Assert.InRange(wooferToMid, 200, 500);
        // Mid/tweeter crosses as low as the tweeter's resonance bound allows, out of the 2-4 kHz ear band.
        Assert.InRange(midToTweeter, 1_500, 4_000);
        CrossoverEdge tweeterHp = proposals[3].HighPassEdge!.Value;
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(BandCurve(2_000, 20_000, 0)).LowHz);
        Assert.True(
            tweeterHp.FrequencyHz >= CrossoverAutoSetup.TweeterMinCrossoverHz(
                resonance, tweeterHp.SlopeDbPerOctave) - 1,
            $"tweeter at {tweeterHp.FrequencyHz:0} Hz / {tweeterHp.SlopeDbPerOctave} dB-oct is below its resonance floor.");
        Assert.Null(proposals[3].LowPassEdge);
        Assert.True(SumRippleDb(sources, proposals) < 6.0);
    }

    [Fact]
    public void Propose_LocalizationBias_LowersTheMidrangeHandoverButNotTheTweeters()
    {
        // The localization bias lowers only a handover INTO the midrange; mid->tweeter stays on the resonance floor.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 100, 0), DriverType.Subwoofer),
            new(BandCurve(40, 1_200, 0), DriverType.Midbass),
            new(BandCurve(120, 5_000, 0), DriverType.Midrange),
            new(BandCurve(2_000, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        double midbassToMid = proposals[1].LowPassEdge!.Value.FrequencyHz;
        double midToTweeter = proposals[2].LowPassEdge!.Value.FrequencyHz;
        Assert.InRange(midbassToMid, 150, 320);
        Assert.True(
            midToTweeter > 1_000,
            $"the midrange->tweeter handover must not be pulled low, was {midToTweeter:0} Hz.");
    }

    [Fact]
    public void Propose_LocalizationBias_SelfLimitsWhenTheMidrangeCannotPlayLow()
    {
        // The bias is a nudge, not a clamp: a midrange playing only down to ~450 Hz holds the junction up.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 100, 0), DriverType.Subwoofer),
            new(BandCurve(40, 1_200, 0), DriverType.Midbass),
            new(BandCurve(450, 5_000, 0), DriverType.Midrange),
            new(BandCurve(2_000, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(sources, Options());

        double midbassToMid = proposals[1].LowPassEdge!.Value.FrequencyHz;
        Assert.True(
            midbassToMid > 350,
            $"a midrange that cannot play low must keep the handover up, was {midbassToMid:0} Hz.");
    }

    [Fact]
    public void Propose_PrefersTheStandardSlopeOverDraggingTheTweeterLow()
    {
        // A clean tweeter does not justify leaving the 24 dB/oct standard.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(60, 900, 0), DriverType.Midbass),
            new(BandCurve(250, 5_000, 0), DriverType.Midrange),
            new(BandCurve(1_200, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            sources, Options(independentSlopes: true));

        CrossoverEdge tweeterHighPass = proposals[2].HighPassEdge!.Value;
        Assert.Equal(24, tweeterHighPass.SlopeDbPerOctave);
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(BandCurve(1_200, 20_000, 0)).LowHz);
        Assert.True(
            tweeterHighPass.FrequencyHz >= CrossoverAutoSetup.TweeterMinCrossoverHz(
                resonance, 24) - 1,
            $"tweeter at {tweeterHighPass.FrequencyHz:0} Hz / 24 dB-oct is below its resonance floor");
    }

    [Fact]
    public void Propose_StillSteepensWhenTheTweeterIsForcedLow()
    {
        // The 24 dB/oct preference is a penalty: a low max-crossover forces the steep protective slope.
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(40, 3_000, 0), DriverType.Woofer),
            new(BandCurve(1_000, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            sources, Options(maxHz: 1_800, families: CrossoverFilterFamily.LinkwitzRiley));

        CrossoverEdge tweeterHighPass = proposals[1].HighPassEdge!.Value;
        Assert.True(
            tweeterHighPass.SlopeDbPerOctave >= 36,
            $"a tweeter forced below its 24 dB/oct floor must steepen, was {tweeterHighPass.SlopeDbPerOctave}");
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(BandCurve(1_000, 20_000, 0)).LowHz);
        Assert.True(
            tweeterHighPass.FrequencyHz >= CrossoverAutoSetup.TweeterMinCrossoverHz(
                resonance, tweeterHighPass.SlopeDbPerOctave) - 1,
            "a forced-low tweeter must still respect its resonance floor");
    }

    [Fact]
    public void Propose_MidbassHandoversStayInItsSensibleRange()
    {
        // Explicit Midbass exercises SensibleRange's Midbass row (80-500 Hz).
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
        CrossoverEdge tweeterHp = proposals[2].HighPassEdge!.Value;
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(BandCurve(2_000, 20_000, 0)).LowHz);
        Assert.True(
            midbassToTweeter >= CrossoverAutoSetup.TweeterMinCrossoverHz(
                resonance, tweeterHp.SlopeDbPerOctave) - 1,
            "the midbass/tweeter handover must respect the tweeter's resonance floor");
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
    public void EstimateBand_IgnoresAnIsolatedResonancePastADeadGap()
    {
        // A lone breakup island past a dead gap must not stretch HighHz.
        var points = new List<SignalPoint>();
        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y;
            if (f < 40)
            {
                y = -24.0 * Math.Log2(40 / f);
            }
            else if (f <= 400)
            {
                y = 0.0;
            }
            else
            {
                y = -24.0 * Math.Log2(f / 400); // deep roll-off above the band
            }

            if (f >= 2_700 && f <= 3_300)
            {
                y = 0.0; // an isolated resonance island past a dead gap
            }

            points.Add(new SignalPoint(f, y));
        }

        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(points);
        Assert.InRange(band.HighHz, 400, 1_000); // the woofer edge, not the island
        Assert.Equal(DriverType.Woofer, band.SuggestedType);
    }

    [Fact]
    public void EstimateBand_BridgesANarrowInBandNull()
    {
        // A narrow deep null inside the band is bridged, not split.
        var points = new List<SignalPoint>();
        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y;
            if (f < 100)
            {
                y = -24.0 * Math.Log2(100 / f);
            }
            else if (f <= 2_000)
            {
                y = 0.0;
            }
            else
            {
                y = -24.0 * Math.Log2(f / 2_000);
            }

            if (f >= 560 && f <= 640)
            {
                y = -20.0; // a narrow deep null well within the passband
            }

            points.Add(new SignalPoint(f, y));
        }

        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(points);
        Assert.InRange(band.LowHz, 70, 110);
        Assert.InRange(band.HighHz, 2_000, 2_600);
    }

    [Fact]
    public void EstimateBand_UsesCoherenceToRejectAnIncoherentRegion()
    {
        // A loud incoherent region (γ² 0.2) wins by magnitude area; coherence gates it out.
        var mag = new List<SignalPoint>();
        var coh = new List<double>();
        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y;
            double g;
            if (f is >= 100 and <= 300)
            {
                (y, g) = (0.0, 0.9);
            }
            else if (f is >= 2_000 and <= 8_000)
            {
                (y, g) = (3.0, 0.2);
            }
            else
            {
                (y, g) = (-40.0, 0.2);
            }

            mag.Add(new SignalPoint(f, y));
            coh.Add(g);
        }

        DriverBandEstimate noCoh = CrossoverAutoSetup.EstimateBand(mag);
        Assert.InRange(noCoh.LowHz, 1_900, 2_100); // the loud incoherent region wins

        DriverBandEstimate withCoh = CrossoverAutoSetup.EstimateBand(mag, coh);
        Assert.InRange(withCoh.LowHz, 90, 110);
        Assert.InRange(withCoh.HighHz, 250, 350); // the real coherent band

        DriverBandEstimate mismatched = CrossoverAutoSetup.EstimateBand(mag, new[] { 0.9 });
        Assert.Equal(noCoh.HighHz, mismatched.HighHz);
    }

    [Fact]
    public void EstimateBand_CoherenceDoesNotChopACoherentBand()
    {
        var mag = BandCurve(100, 2_000, 0);
        var coh = Enumerable.Repeat(0.95, mag.Count).ToList();

        DriverBandEstimate noCoh = CrossoverAutoSetup.EstimateBand(mag);
        DriverBandEstimate withCoh = CrossoverAutoSetup.EstimateBand(mag, coh);
        Assert.Equal(noCoh.LowHz, withCoh.LowHz);
        Assert.Equal(noCoh.HighHz, withCoh.HighHz);
    }

    [Fact]
    public void Propose_WooferToMidrange_KeepsTheCrossoverInTheWooferRange()
    {
        // A woofer measuring up to ~850 Hz must still hand over near 250 Hz.
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
        // A tweeter measuring down to 1 kHz is held above ~2.3 kHz at 24 dB/oct.
        var woofer = new AutoSetupSource(BandCurve(40, 4_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(families: CrossoverFilterFamily.LinkwitzRiley));

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[1].Kind);
        Assert.Null(proposals[0].HighPassEdge);
        Assert.Null(proposals[1].LowPassEdge);

        double lowPassHz = proposals[0].LowPassEdge!.Value.FrequencyHz;
        CrossoverEdge tweeterHp = proposals[1].HighPassEdge!.Value;
        Assert.Equal(lowPassHz, tweeterHp.FrequencyHz);
        Assert.InRange(lowPassHz, 1_500, 4_000);
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(BandCurve(1_000, 20_000, 0)).LowHz);
        Assert.True(
            tweeterHp.FrequencyHz >= CrossoverAutoSetup.TweeterMinCrossoverHz(
                resonance, tweeterHp.SlopeDbPerOctave) - 1,
            "the split must respect the tweeter's resonance floor");
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
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, -6), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options());

        Assert.True(proposals.All(proposal => proposal.GainDb <= 0.0001));
        Assert.Contains(proposals, proposal => Math.Abs(proposal.GainDb) < 0.0001);
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
        // The crossover (~2.3 kHz) sits above the tweeter's protection floor, so the LR24 baseline is reachable.
        var woofer = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);
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
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_300, 24),
                0),
            new CrossoverProposal(
                CrossoverKind.HighPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_300, 24),
                null,
                0)
        };

        Assert.True(
            SumRippleDb(channels, optimized) <= SumRippleDb(channels, baseline) + 0.25,
            "The optimizer must not be flatter-losing against a fixed LR24 split.");
    }

    [Fact]
    public void Propose_ThreeWay_GivesTheMiddleChannelABandPass()
    {
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, midrange, tweeter],
            Options());

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.BandPass, proposals[1].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[2].Kind);

        double lowSplit = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double highSplit = proposals[2].HighPassEdge!.Value.FrequencyHz;
        Assert.Equal(lowSplit, proposals[1].HighPassEdge!.Value.FrequencyHz);
        Assert.Equal(highSplit, proposals[1].LowPassEdge!.Value.FrequencyHz);
        Assert.True(lowSplit < highSplit);
    }

    [Fact]
    public void TwoClassesThatOnlyTouch_StillLeaveTheJunctionSomethingToSearch()
    {
        // A subwoofer is sensible to 80 Hz and a midbass from 80 Hz, so their intersection is the single point
        // where the two ranges abut. Applied literally that pins the junction with nothing to decide, which is
        // what a five-way with two subwoofers showed in the field: "Pinned to 80 Hz" over a 20-157 Hz overlap.
        var subwoofer = new AutoSetupSource(BandCurve(20, 150, 0), DriverType.Subwoofer);
        var midbass = new AutoSetupSource(BandCurve(20, 1_500, 0), DriverType.Midbass);

        JunctionWindowResolution window = CrossoverAutoSetup.ResolveJunctionWindow(
            [subwoofer, midbass], 0, Options());

        Assert.True(
            window.HighHz > window.LowHz,
            $"The junction is pinned to {window.LowHz:0} Hz, where the two classes happen to meet.");
        Assert.InRange(80, window.LowHz, window.HighHz);
        Assert.DoesNotContain(
            window.Notes, note => note.Summary.StartsWith("Pinned", StringComparison.Ordinal));

        // Deliberate, and the reason this is pinned rather than left implicit: the two classes touch at 80 Hz, so
        // a window with room on both sides of it is a window OUTSIDE both of their ranges. A class bound is a
        // preference about where a handover belongs, and a preference that admits exactly one frequency is not
        // one — the measured band and the safety bounds are what actually hold the window in.
        Assert.True(
            window.LowHz < 80 && window.HighHz > 80,
            $"The window {window.LowHz:0}-{window.HighHz:0} Hz stayed inside a class range: at a junction " +
            "where the classes only touch there is no inside to stay in.");
    }

    [Fact]
    public void Propose_WithoutASplitWindow_HandsEveryJunctionOverAtOneFrequency()
    {
        // Split corners are opt-in junction by junction: a window that does not ask for one must not produce one.
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);
        var midrange = new AutoSetupSource(BandCurve(200, 5_000, 0), DriverType.Midrange);
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, midrange, tweeter],
            Options() with
            {
                JunctionWindows = [new JunctionSearchWindow(), new JunctionSearchWindow()]
            });

        Assert.Equal(
            proposals[0].LowPassEdge!.Value.FrequencyHz,
            proposals[1].HighPassEdge!.Value.FrequencyHz);
        Assert.Equal(
            proposals[1].LowPassEdge!.Value.FrequencyHz,
            proposals[2].HighPassEdge!.Value.FrequencyHz);
    }

    [Fact]
    public void Propose_ASplitJunction_PartsItsCornersAndIsFlatterForIt()
    {
        var woofer = new AutoSetupSource(BandCurve(40, 4_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(600, 20_000, 0), DriverType.Tweeter);
        AutoSetupSource[] channels = [woofer, tweeter];

        IReadOnlyList<CrossoverProposal> matched = CrossoverAutoSetup.Propose(
            channels, Options());
        IReadOnlyList<CrossoverProposal> split = CrossoverAutoSetup.Propose(
            channels,
            Options() with
            {
                JunctionWindows = [new JunctionSearchWindow(AllowSplitCorners: true)]
            });

        double lowPass = split[0].LowPassEdge!.Value.FrequencyHz;
        double highPass = split[1].HighPassEdge!.Value.FrequencyHz;
        double octaves = Math.Log2(highPass / lowPass);
        Assert.True(
            Math.Abs(octaves) > 0.01,
            $"The junction stayed matched at {lowPass} Hz, so nothing about a split is tested here.");
        // Searched as an offset from the corner, so it can never exceed the widest offset on the list.
        Assert.True(
            Math.Abs(octaves) <= 0.3,
            $"{lowPass}-{highPass} Hz is {octaves:0.000} octaves apart, wider than any offered offset.");
        Assert.True(
            JunctionSpanDb(channels, split) <= JunctionSpanDb(channels, matched),
            $"The split junction spans {JunctionSpanDb(channels, split):0.000} dB against " +
            $"{JunctionSpanDb(channels, matched):0.000} dB matched, so it bought nothing.");
    }

    [Fact]
    public void ASplitOffset_MustBuyMoreThanTheOverlapItSaves()
    {
        // Parting the corners shrinks the overlap integral whatever it does to the response, so an offset that
        // changes nothing else would still score better and the option would stop being a search. Charged back at
        // the overlap term's own rate, this junction stays matched; with the charge at zero it parts by a quarter
        // octave for nothing.
        var woofer = new AutoSetupSource(BandCurve(40, 4_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(600, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(independentSlopes: true) with
            {
                JunctionWindows = [new JunctionSearchWindow(AllowSplitCorners: true)]
            });

        Assert.Equal(
            proposals[0].LowPassEdge!.Value.FrequencyHz,
            proposals[1].HighPassEdge!.Value.FrequencyHz);
    }

    /// <summary>The Fs floor is read at the corner the search is standing on, but a negative offset puts the
    /// high-pass BELOW that corner — an eighth of an octave at the widest, which is 3 dB of the floor's protection
    /// at 24 dB/oct and 6 dB at 48. The ranked pool crosses junction options and never re-runs the descent, so this
    /// covers both paths.</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 24)]
    [InlineData(true, 24)]
    public void ABandLimitUnderTheFloor_SteepensTheTweeterHighPass_PastTheSlopeWindowAndTheBaseline(
        bool ranked, int? maxSlope)
    {
        // The band limit holds the corner under the 24 dB/oct floor, so only a steeper slope protects Fs; neither
        // the junction's slope window nor the conventional all-24 candidate may keep 24 there.
        AutoSetupSource[] channels =
        [
            new(BandCurve(200, 8_000, 0), DriverType.Midrange),
            new(BandCurve(1_500, 20_000, 0), DriverType.Tweeter)
        ];
        CrossoverAutoSetupOptions options = Options(maxHz: 2_000) with
        {
            JunctionWindows = [new JunctionSearchWindow(MaxSlopeDbPerOctave: maxSlope)]
        };

        CrossoverEdge highPass = (ranked
            ? CrossoverAutoSetup.ProposeRanked(channels, options)[0].Proposals
            : CrossoverAutoSetup.Propose(channels, options))[1].HighPassEdge!.Value;

        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(channels[1].MagnitudeDb).LowHz);
        Assert.True(
            highPass.FrequencyHz >= CrossoverAutoSetup.TweeterMinCrossoverHz(resonance, highPass.SlopeDbPerOctave) - 1,
            $"The tweeter crosses at {highPass.FrequencyHz:0} Hz / {highPass.SlopeDbPerOctave} dB-oct, under the floor " +
            $"its {resonance:0} Hz resonance needs.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASplitCorner_NeverPutsATweeterHighPassUnderItsResonanceFloor(bool ranked)
    {
        // Pinned just above the floor and Linkwitz-Riley only, with a midrange that rolls off right above the window:
        // there the search does reach for a negative offset. Without it this reads a junction that never splits and
        // asserts nothing (a mid reaching 5 kHz only overlapped while the polarity scoring was biased).
        List<SignalPoint> tweeterCurve = BandCurve(1_100, 20_000, 0);
        var channels = new AutoSetupSource[]
        {
            new(BandCurve(30, 500, 0), DriverType.Woofer),
            new(BandCurve(200, 1_800, 0), DriverType.Midrange),
            new(tweeterCurve, DriverType.Tweeter)
        };
        CrossoverAutoSetupOptions options =
            Options(families: CrossoverFilterFamily.LinkwitzRiley) with
            {
                JunctionWindows =
                [
                    new JunctionSearchWindow(AllowSplitCorners: true),
                    new JunctionSearchWindow(1_700, 1_750, AllowSplitCorners: true)
                ]
            };

        IReadOnlyList<CrossoverProposal> proposals = ranked
            ? CrossoverAutoSetup.ProposeRanked(channels, options, candidateCount: 50)[0].Proposals
            : CrossoverAutoSetup.Propose(channels, options);

        CrossoverEdge highPass = proposals[2].HighPassEdge!.Value;
        if (!ranked)
        {
            // The descent does reach for a negative offset here, which is what puts the floor at risk at all.
            // The pool refuses it — the floors it enumerates against are the same ones — so only assert it where
            // the overlap is the point rather than pinning what the ranking happens to prefer.
            Assert.True(
                highPass.FrequencyHz < proposals[1].LowPassEdge!.Value.FrequencyHz,
                "This fixture is meant to overlap its corners; without that the floor is never at risk.");
        }
        double resonance = CrossoverAutoSetup.TweeterResonanceHz(
            CrossoverAutoSetup.EstimateBand(tweeterCurve).LowHz);
        double floor = CrossoverAutoSetup.TweeterMinCrossoverHz(
            resonance, highPass.SlopeDbPerOctave);
        Assert.True(
            highPass.FrequencyHz >= floor - 1,
            $"The tweeter high-pass landed at {highPass.FrequencyHz:0} Hz with " +
            $"{highPass.SlopeDbPerOctave} dB/oct, under the {floor:0} Hz its estimated " +
            $"{resonance:0} Hz resonance needs.");
    }

    // An octave either side of the corner: the band JunctionPenalty scores, read off the ideal complex sum.
    private static double JunctionSpanDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals)
    {
        double corner = Math.Sqrt(
            proposals[0].LowPassEdge!.Value.FrequencyHz *
            proposals[1].HighPassEdge!.Value.FrequencyHz);
        var band = CrossoverAutoSetup
            .SummedResponseDb(channels, proposals, SampleRate, SampleRate)
            .Where(point => point.X >= corner / 2 && point.X <= corner * 2)
            .Select(point => point.Y)
            .ToList();
        return band.Max() - band.Min();
    }

    [Fact]
    public void Propose_WalksTheCallersOrder_NotTheDriverTypes()
    {
        // Shuffled input is walked in the given order: with two drivers of one class only the caller knows which plays lower.
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);

        IReadOnlyList<CrossoverProposal> shuffled = CrossoverAutoSetup.Propose(
            [tweeter, woofer], Options());

        // Position 0 is the chain's bottom, so it takes the low-pass even though it is the tweeter.
        Assert.Equal(CrossoverKind.LowPass, shuffled[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, shuffled[1].Kind);
    }

    [Fact]
    public void Propose_IndependentSlopes_MayDifferAcrossAJunction()
    {
        // Three ways, because the rule binds a CHANNEL's two shoulders and a two-way has no channel with two of them:
        // there the woofer owns only a low-pass and the tweeter only a high-pass, so they were always free to differ
        // and the old assertion only held by luck.
        var channels = new[]
        {
            new AutoSetupSource(BandCurve(40, 900, 0), DriverType.Woofer),
            new AutoSetupSource(BandCurve(200, 6_000, 0), DriverType.Midrange),
            new AutoSetupSource(BandCurve(1_500, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> matched = CrossoverAutoSetup.Propose(
            channels,
            Options(independentSlopes: false));
        IReadOnlyList<CrossoverProposal> independent = CrossoverAutoSetup.Propose(
            channels,
            Options(independentSlopes: true));

        Assert.Equal(
            matched[1].HighPassEdge!.Value.SlopeDbPerOctave,
            matched[1].LowPassEdge!.Value.SlopeDbPerOctave);
        Assert.True(
            SumRippleDb(channels, independent) <= SumRippleDb(channels, matched) + 0.25,
            $"Freeing the slopes should not cost flatness: " +
            $"{SumRippleDb(channels, independent):0.00} dB against " +
            $"{SumRippleDb(channels, matched):0.00} dB.");
    }

    [Fact]
    public void Propose_LowerLimit_AddsASubsonicHighPassToTheWoofer()
    {
        var woofer = new AutoSetupSource(BandCurve(28, 2_000, 0), DriverType.Woofer);
        var tweeter = new AutoSetupSource(BandCurve(1_000, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> limited = CrossoverAutoSetup.Propose(
            [woofer, tweeter],
            Options(minHz: 75));

        Assert.Equal(CrossoverKind.BandPass, limited[0].Kind);
        Assert.NotNull(limited[0].HighPassEdge);
        Assert.Equal(75, limited[0].HighPassEdge!.Value.FrequencyHz, 0);

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
        // Flatness is indifferent to slope here; the overlap penalty makes the steeper filter win.
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
    public void Propose_RejectsTooFewChannels()
    {
        var a = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);

        Assert.Throws<ArgumentException>(
            () => CrossoverAutoSetup.Propose([a], Options()));
    }

    [Fact]
    public void Propose_SplitsTwoSubwoofersBetweenThemselves()
    {
        var lower = new AutoSetupSource(BandCurve(20, 60, 0), DriverType.Subwoofer);
        var upper = new AutoSetupSource(BandCurve(35, 120, 0), DriverType.Subwoofer);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [lower, upper], Options());

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[1].Kind);
        double split = proposals[0].LowPassEdge!.Value.FrequencyHz;
        Assert.Equal(split, proposals[1].HighPassEdge!.Value.FrequencyHz);
        // Inside the shared band, not pushed up by the hand-over-up bias.
        Assert.InRange(split, 35, 60);
    }

    [Fact]
    public void Propose_TwoDriversOfOneClass_DivideTheBandTheyShare()
    {
        // Two subwoofers in series are one class band split between them, and the split belongs in the middle of
        // what both can produce. Left to flatness alone it lands wherever the cabin happens to be smoothest, which
        // squeezes one of the two into a sliver of its own range: on the field five-way the lower sub came out
        // working 20-35 Hz, and on this shape the split ran up to 70 Hz instead.
        List<SignalPoint> lowerCurve = BandCurve(20, 113, 0);
        List<SignalPoint> upperCurve = BandCurve(20, 157, 0);
        var lower = new AutoSetupSource(lowerCurve, DriverType.Subwoofer);
        var upper = new AutoSetupSource(upperCurve, DriverType.Subwoofer);

        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose([lower, upper], Options());

        double split = proposals[0].LowPassEdge!.Value.FrequencyHz;
        double middle = Math.Sqrt(
            CrossoverAutoSetup.EstimateBand(upperCurve).LowHz *
            CrossoverAutoSetup.EstimateBand(lowerCurve).HighHz);
        Assert.True(
            Math.Abs(Math.Log2(split / middle)) <= 0.25,
            $"The two split at {split:0} Hz, {Math.Log2(split / middle):0.00} octaves off the " +
            $"{middle:0} Hz middle of the band they share.");
    }

    [Fact]
    public void Propose_SameClassJunction_CarriesNoClassPlacementBias()
    {
        // The localization bias answers which CLASS owns a region; between two subs it must not apply.
        // The runs differ only in the upper driver's class and share one search window, so any difference is the bias.
        // The window has to leave room UNDER the biased answer: a 40 Hz floor put both runs on the window edge, where
        // no bias can show itself. It also has to leave room between that answer and the middle of the shared band,
        // which is where a same-class junction is pulled instead — with a 20-100 Hz lower driver the two landed a
        // lattice step apart and neither force could be read.
        var lower = new AutoSetupSource(BandCurve(20, 70, 0), DriverType.Subwoofer);
        var curve = BandCurve(25, 400, 0);
        CrossoverAutoSetupOptions options = Options();

        double Split(DriverType upperType) => CrossoverAutoSetup
            .Propose([lower, new AutoSetupSource(curve, upperType)], options)[0]
            .LowPassEdge!.Value.FrequencyHz;

        double sameClass = Split(DriverType.Subwoofer);
        double acrossClasses = Split(DriverType.Woofer);

        // In octaves, not hertz: 20 Hz is most of an octave at the bottom of a sub band and nothing at the top.
        Assert.True(
            Math.Log2(acrossClasses / sameClass) > 0.25,
            $"A sub-to-sub split was biased up like a sub-to-woofer one: " +
            $"{sameClass:0} Hz against {acrossClasses:0} Hz.");
    }

    [Fact]
    public void JunctionWindow_KeepsTheTweeterFloor_WhereTheClassesDoNotOverlap()
    {
        // Field case: a midbass measuring to 736 Hz under a tweeter measuring from 712 Hz. Midbass tops out at
        // 500 Hz by class and a tweeter starts at 1.7 kHz, so the CLASS bounds cross and are dropped — and the
        // tweeter's resonance floor used to be dropped with them, leaving a 712-736 Hz window inside the dome's
        // own resonance. The class bound is a preference; Fs is not.
        var midbass = new AutoSetupSource(BandCurve(70, 736, 0), DriverType.Midbass);
        var tweeter = new AutoSetupSource(BandCurve(712, 9_980, 0), DriverType.Tweeter);
        AutoSetupSource[] channels = [midbass, tweeter];

        JunctionWindowResolution window =
            CrossoverAutoSetup.ResolveJunctionWindow(channels, 0, Options());

        double floor = CrossoverAutoSetup.TweeterMinCrossoverHz(
            CrossoverAutoSetup.TweeterResonanceHz(
                CrossoverAutoSetup.EstimateBand(tweeter.MagnitudeDb).LowHz),
            48);
        Assert.True(
            window.LowHz >= floor - 1e-6,
            $"The window opens at {window.LowHz:0} Hz, under the {floor:0} Hz resonance floor.");
        Assert.True(
            window.HighHz > window.LowHz,
            $"The window collapsed to {window.LowHz:0} Hz instead of opening upwards.");

        // Opening upward is not opening to the top of the band: nobody searches a mid-to-tweeter handover up to
        // 20 kHz. Two octaves over the floor covers every slope the resonance rule can ask for and stops there.
        Assert.True(
            window.HighHz <= window.LowHz * 4,
            $"The window runs to {window.HighHz:0} Hz, far past anything that is still a handover.");
        Assert.NotEmpty(window.Notes);

        // And the search must actually land inside the window rather than be dragged there afterwards.
        IReadOnlyList<CrossoverProposal> proposals =
            CrossoverAutoSetup.Propose(channels, Options());
        double corner = proposals[1].HighPassEdge!.Value.FrequencyHz;
        Assert.InRange(corner, window.LowHz, window.HighHz);
    }

    [Fact]
    public void Propose_FiveWayWithTwoSubwoofers_OrdersEveryHandover()
    {
        var sources = new List<AutoSetupSource>
        {
            new(BandCurve(20, 55, 0), DriverType.Subwoofer),
            new(BandCurve(40, 130, 0), DriverType.Subwoofer),
            new(BandCurve(60, 900, 0), DriverType.Midbass),
            new(BandCurve(250, 6_000, 0), DriverType.Midrange),
            new(BandCurve(2_200, 20_000, 0), DriverType.Tweeter)
        };

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            sources, Options());

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[4].Kind);
        double[] splits = proposals
            .Take(4)
            .Select(proposal => proposal.LowPassEdge!.Value.FrequencyHz)
            .ToArray();
        Assert.Equal(splits.OrderBy(frequency => frequency), splits);

        for (int j = 0; j < splits.Length; j++)
        {
            Assert.InRange(
                splits[j],
                CrossoverAutoSetup.EstimateBand(sources[j + 1].MagnitudeDb).LowHz,
                CrossoverAutoSetup.EstimateBand(sources[j].MagnitudeDb).HighHz);
        }

        Assert.All(
            proposals.Skip(1).Take(3),
            proposal => Assert.Equal(CrossoverKind.BandPass, proposal.Kind));
    }

    [Fact]
    public void Propose_SecondSubwoofer_DoesNotSetTheLevelTheSystemIsCutTo()
    {
        // Subs are separately amped: a quiet sub must not drag the flat top down, for every sub in the chain.
        var lowSub = new AutoSetupSource(BandCurve(20, 60, 0), DriverType.Subwoofer);
        var highSub = new AutoSetupSource(BandCurve(35, 120, -10), DriverType.Subwoofer);
        var midrange = new AutoSetupSource(BandCurve(150, 4_000, 0), DriverType.Midrange);
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [lowSub, highSub, midrange, tweeter], Options());

        // Levelled to each other and left at the top, nowhere near the quiet sub's -10 dB. They are not bit-equal:
        // each level is the average over that channel's assigned passband, and the corners decide those passbands.
        Assert.InRange(Math.Abs(proposals[2].GainDb - proposals[3].GainDb), 0, 0.25);
        Assert.InRange(proposals[2].GainDb, -0.5, 0);
        Assert.InRange(proposals[3].GainDb, -0.5, 0);
    }

    [Fact]
    public void ProposeSingle_ProtectsTheDriverBelowWhereItPlays()
    {
        // Rear fill: high-pass an octave above its measured edge, no low-pass, gain untouched (levelling is separate).
        var rear = new AutoSetupSource(BandCurve(110, 15_000, 0), DriverType.Midrange);

        CrossoverProposal proposal = CrossoverAutoSetup.ProposeSingle(rear, Options());

        Assert.Equal(CrossoverKind.HighPass, proposal.Kind);
        Assert.Null(proposal.LowPassEdge);
        Assert.Equal(0, proposal.GainDb);
        Assert.InRange(proposal.HighPassEdge!.Value.FrequencyHz, 160, 200);
    }

    [Fact]
    public void ProposeSingle_HoldsATweeterAboveItsResonance()
    {
        // An octave above the edge would sit barely above the resonance; the excursion rule lifts the corner.
        var tweeter = new AutoSetupSource(BandCurve(880, 20_000, 0), DriverType.Tweeter);

        CrossoverProposal proposal = CrossoverAutoSetup.ProposeSingle(tweeter, Options());

        double corner = proposal.HighPassEdge!.Value.FrequencyHz;
        double required = CrossoverAutoSetup.TweeterMinCrossoverHz(
            CrossoverAutoSetup.TweeterResonanceHz(
                CrossoverAutoSetup.EstimateBand(tweeter.MagnitudeDb).LowHz),
            proposal.HighPassEdge!.Value.SlopeDbPerOctave);
        Assert.True(
            corner >= required - 25,
            $"A standalone tweeter was crossed at {corner:0} Hz, under the " +
            $"{required:0} Hz its resonance asks for.");
    }

    [Fact]
    public void Propose_AChainOfNothingButSubs_LevelsThemToEachOtherEitherWayRound()
    {
        // No flat top in a two-sub group: level to the quieter member regardless of order.
        List<SignalPoint> lowCurve = BandCurve(20, 60, 8);
        List<SignalPoint> highCurve = BandCurve(35, 120, 0);
        var loudLow = new AutoSetupSource(lowCurve, DriverType.Subwoofer);
        var quietHigh = new AutoSetupSource(highCurve, DriverType.Subwoofer);

        IReadOnlyList<CrossoverProposal> loudFirst = CrossoverAutoSetup.Propose(
            [loudLow, quietHigh], Options());

        // Not exactly 8 dB: each level is averaged over that driver's own passband.
        Assert.InRange(loudFirst[0].GainDb, -10, -6.5);
        Assert.Equal(0, loudFirst[1].GainDb, 1);

        IReadOnlyList<CrossoverProposal> quietFirst = CrossoverAutoSetup.Propose(
            [
                new AutoSetupSource(BandCurve(20, 60, 0), DriverType.Subwoofer),
                new AutoSetupSource(BandCurve(35, 120, 8), DriverType.Subwoofer)
            ],
            Options());

        Assert.Equal(0, quietFirst[0].GainDb, 1);
        Assert.InRange(quietFirst[1].GainDb, -10, -6.5);
    }

    [Fact]
    public void ProposeSingle_RefusesRatherThanCrossUnderTheDriversOwnSafetyFloor()
    {
        // No protective corner fits inside a 1.5 kHz window: report none rather than a mislabeled one.
        var tweeter = new AutoSetupSource(BandCurve(880, 20_000, 0), DriverType.Tweeter);

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => CrossoverAutoSetup.ProposeSingle(tweeter, Options(maxHz: 1_500)));

        Assert.Contains("protective high-pass", refused.Message);
    }

    [Fact]
    public void ProposeSingle_SqueezesTheHeadroomMarginForANarrowDriver()
    {
        // The octave of headroom is a preference: a driver narrower than two octaves gets a corner inside its band.
        var narrow = new AutoSetupSource(BandCurve(1_000, 1_500, 0), DriverType.Midrange);

        CrossoverProposal proposal = CrossoverAutoSetup.ProposeSingle(narrow, Options());

        DriverBandEstimate band = CrossoverAutoSetup.EstimateBand(narrow.MagnitudeDb);
        Assert.InRange(proposal.HighPassEdge!.Value.FrequencyHz, band.LowHz, band.HighHz);
    }

    [Fact]
    public void ProposeSingle_KeepsTheCornerInsideTheUsersWindow()
    {
        var rear = new AutoSetupSource(BandCurve(110, 15_000, 0), DriverType.Midrange);

        CrossoverProposal proposal = CrossoverAutoSetup.ProposeSingle(
            rear, Options(minHz: 300, maxHz: 8_000));

        Assert.Equal(CrossoverKind.BandPass, proposal.Kind);
        Assert.True(proposal.HighPassEdge!.Value.FrequencyHz >= 300);
        Assert.Equal(8_000, proposal.LowPassEdge!.Value.FrequencyHz, 0);
    }

    [Fact]
    public void OffsetToReferenceLevel_CutsALoudGroupOntoTheFrontStage_Rigidly()
    {
        // The whole group slides; its internal balance must survive.
        var midbass = new AutoSetupSource(BandCurve(80, 2_000, 6), DriverType.Midbass);
        var tweeter = new AutoSetupSource(BandCurve(2_000, 20_000, 4), DriverType.Tweeter);
        var group = new List<AutoSetupSource> { midbass, tweeter };
        IReadOnlyList<CrossoverProposal> own = CrossoverAutoSetup.Propose(group, Options());
        double ownReference = CrossoverAutoSetup.ReferenceLevelDb(group, own, SampleRate);

        IReadOnlyList<CrossoverProposal> levelled = CrossoverAutoSetup.OffsetToReferenceLevel(
            group, own, SampleRate, ownReference - 6);

        Assert.Equal(-6, levelled[0].GainDb - own[0].GainDb, 1);
        Assert.Equal(-6, levelled[1].GainDb - own[1].GainDb, 1);
        Assert.Equal(
            own[0].GainDb - own[1].GainDb,
            levelled[0].GainDb - levelled[1].GainDb,
            1);
    }

    [Fact]
    public void OffsetToReferenceLevel_LeavesAQuietGroupAlone()
    {
        // Cut-only: a group under the front stage is not boosted.
        var midbass = new AutoSetupSource(BandCurve(80, 2_000, -8), DriverType.Midbass);
        var tweeter = new AutoSetupSource(BandCurve(2_000, 20_000, -8), DriverType.Tweeter);
        var group = new List<AutoSetupSource> { midbass, tweeter };
        IReadOnlyList<CrossoverProposal> own = CrossoverAutoSetup.Propose(group, Options());
        double ownReference = CrossoverAutoSetup.ReferenceLevelDb(group, own, SampleRate);

        IReadOnlyList<CrossoverProposal> levelled = CrossoverAutoSetup.OffsetToReferenceLevel(
            group, own, SampleRate, ownReference + 10);

        Assert.Equal(own[0].GainDb, levelled[0].GainDb, 1);
        Assert.Equal(own[1].GainDb, levelled[1].GainDb, 1);
    }

    [Fact]
    public void OffsetToReferenceLevel_LevelsAGroupOfOne()
    {
        var rear = new AutoSetupSource(BandCurve(110, 15_000, 5), DriverType.Midrange);
        CrossoverProposal single = CrossoverAutoSetup.ProposeSingle(rear, Options());

        IReadOnlyList<CrossoverProposal> levelled = CrossoverAutoSetup.OffsetToReferenceLevel(
            [rear], [single], SampleRate, 0);

        // Not exactly -5: the level is read over the passband the high-pass leaves.
        Assert.InRange(levelled[0].GainDb, -5.3, -4.7);
        Assert.Equal(single.HighPassEdge, levelled[0].HighPassEdge);
    }

    // Flipping only the channel above a junction also flipped the next junction's relation, so every lower junction's
    // inverted option was scored with the one above it broken.
    [Fact]
    public void SetRelativeInversion_ChangesOnlyThatJunctionsRelation()
    {
        bool[] invert = [false, false, true, true];

        CrossoverAutoSetup.SetRelativeInversion(invert, 0, invertRelative: true);

        Assert.Equal([false, true, false, false], invert);
        Assert.True(invert[0] ^ invert[1]);
        Assert.True(invert[1] ^ invert[2]);
        Assert.False(invert[2] ^ invert[3]);

        CrossoverAutoSetup.SetRelativeInversion(invert, 0, invertRelative: true);
        Assert.Equal([false, true, false, false], invert);
    }
}
