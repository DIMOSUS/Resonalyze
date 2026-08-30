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
            SampleRate,
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
            .SummedResponseDb(channels, proposals, SampleRate, SampleRate)
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
        // The placement heuristics cross the woofer/midrange handover as low as
        // the midrange's sensible floor (200 Hz) allows — below its cone-breakup
        // region — so the wide woofer/mid overlap does not linger up where it
        // interferes; still gated by the measured midrange band.
        Assert.InRange(wooferToMid, 200, 500);
        // The placement heuristics cross the mid/tweeter as low as the tweeter's
        // resonance bound and its measured band allow, out of the 2–4 kHz
        // ear-sensitivity band; a low tweeter handover must protect Fs.
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
        // A broad-band midbass whose flatness-optimal handover to the midrange would
        // otherwise sit near the top of the Midbass->Midrange class band (~500 Hz).
        // The localization bias pulls THAT handover down below the ~300 Hz threshold,
        // but is scoped to a handover INTO the midrange — the midrange->tweeter
        // handover (upper driver a Tweeter) is left to the resonance floor, unmoved.
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
        // The bias is a nudge, not a clamp: a midrange that only plays down to ~450 Hz
        // cannot take the handover at 250 without a gaping hole, and the flatness cost
        // of that hole holds the junction up — the midbass keeps carrying the low-mids
        // it must. So the handover stays well above the 250 Hz threshold here.
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
        // A tweeter that measures clean down to ~1.2 kHz: rather than pin it
        // maximally low on a steep 48 dB/oct slope (the specialist choice), the auto
        // keeps the 24 dB/oct standard and lets the handover sit a little higher —
        // even into the 2–4 kHz ear-sensitive band. Deviations from 24 must be
        // justified by the flatness/protection they buy, and a clean tweeter does not
        // justify one, so the slope stays 24 and the crossover respects the resonance
        // floor at that slope.
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
        // The 24 dB/oct preference is a penalty, not a lock. When the crossover is
        // constrained below the tweeter's 24 dB/oct resonance floor (here by a low
        // max-crossover limit), the resonance protection overrides the penalty and
        // still forces the steep slope that keeps the tweeter safe.
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
        // A three-way with an explicit Midbass driver exercises the Midbass row of
        // SensibleRange (80-500 Hz), which the other systems never hit. Its lower
        // handover to the sub lands inside that range; the upper handover to the
        // tweeter is pulled up by the tweeter's own resonance protection (crossing
        // this 2 kHz-band tweeter no lower than ~3 kHz at 24 dB/oct) and must stay
        // ordered and respect that floor.
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
        // A woofer flat 40-400 Hz, then a deep dead gap, then a lone breakup
        // resonance up near 3 kHz. The usable band must stay on the woofer: the
        // isolated peak past the gap must not stretch HighHz and relabel the
        // driver a midbass.
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
        // A wide band (100 Hz - 2 kHz) with a single narrow deep null inside it
        // (an interference or room dip). The narrow gap is bridged, so the band
        // stays whole rather than splitting at the notch.
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
        // A driver with a real, coherent passband (100-300 Hz) plus a broad,
        // LOUD but incoherent region up high (2-8 kHz, γ² 0.2 — a rattle, buzz,
        // or a channel picking up another driver). By magnitude area alone the
        // loud broad region wins, so without coherence the band is read there.
        // With coherence it is gated out and the real band is chosen.
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

        // A mismatched-length coherence is ignored, not trusted.
        DriverBandEstimate mismatched = CrossoverAutoSetup.EstimateBand(mag, new[] { 0.9 });
        Assert.Equal(noCoh.HighHz, mismatched.HighHz);
    }

    [Fact]
    public void EstimateBand_CoherenceDoesNotChopACoherentBand()
    {
        // Guard against over-tightening: a clean band whose γ² stays above the
        // floor everywhere must read identically with and without coherence.
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
        // A wide woofer so the driver overlap comfortably contains the tweeter's
        // resonance-protected low handover (a tweeter measuring down to 1 kHz is
        // held above ~2.3 kHz at 24 dB/oct, not crossed at its resonance).
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
        // A woofer that rolls off gently below where a flat tweeter takes over: a
        // fixed LR24 electrical split overshoots the acoustic slope and dips. The
        // optimizer is free to pick gentler/other filters and must not do worse
        // than the naive LR24-at-the-crossover baseline. The crossover sits at
        // ~2.3 kHz — above the tweeter's resonance-protection floor — so the
        // comparison is a fair one the protected search can actually reach.
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
    public void Propose_WalksTheCallersOrder_NotTheDriverTypes()
    {
        // The chain is the order it was given in, and nothing else: a caller that
        // hands over the channels shuffled gets a chain walked in THAT order — the
        // tweeter first, handing down to the woofer — not a quietly re-sorted one.
        // Which is the whole point: with two drivers of one class (the case this
        // exists for) the type cannot say which plays lower, so the caller must.
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);
        var woofer = new AutoSetupSource(BandCurve(30, 500, 0), DriverType.Woofer);

        IReadOnlyList<CrossoverProposal> shuffled = CrossoverAutoSetup.Propose(
            [tweeter, woofer], Options());

        // Position 0 is the chain's BOTTOM, so it takes the low-pass — even though
        // the driver sitting there is the tweeter.
        Assert.Equal(CrossoverKind.LowPass, shuffled[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, shuffled[1].Kind);
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
    public void Propose_RejectsTooFewChannels()
    {
        var a = new AutoSetupSource(BandCurve(40, 2_000, 0), DriverType.Woofer);

        Assert.Throws<ArgumentException>(
            () => CrossoverAutoSetup.Propose([a], Options()));
    }

    [Fact]
    public void Propose_SplitsTwoSubwoofersBetweenThemselves()
    {
        // The case the wizard used to refuse outright: two drivers of one class,
        // here a pair of subwoofers dividing the bottom, which is an ordinary car
        // install and not a mistake in the type assignment.
        var lower = new AutoSetupSource(BandCurve(20, 60, 0), DriverType.Subwoofer);
        var upper = new AutoSetupSource(BandCurve(35, 120, 0), DriverType.Subwoofer);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [lower, upper], Options());

        Assert.Equal(CrossoverKind.LowPass, proposals[0].Kind);
        Assert.Equal(CrossoverKind.HighPass, proposals[1].Kind);
        double split = proposals[0].LowPassEdge!.Value.FrequencyHz;
        Assert.Equal(split, proposals[1].HighPassEdge!.Value.FrequencyHz);
        // Inside the band the two actually share, not shoved to the top of the
        // subwoofer class's range by the hand-over-up bias.
        Assert.InRange(split, 35, 60);
    }

    [Fact]
    public void Propose_SameClassJunction_CarriesNoClassPlacementBias()
    {
        // "A sub hands over where it stops being localizable (~80 Hz)" answers
        // which of two CLASSES owns the region they share. Between two subs there
        // is no such question, and the bias applied anyway would drag their split
        // toward 80 Hz for no reason at all.
        //
        // The two runs below differ ONLY in the upper driver's class, and the
        // 40 Hz lower limit pins them to the same search window: the measured
        // edges put the floor under 40 either way, and the ceiling is the
        // subwoofer class's own 80 Hz in both. So the whole difference in where
        // the split lands is the bias — present across classes, absent within one.
        var lower = new AutoSetupSource(BandCurve(20, 100, 0), DriverType.Subwoofer);
        var curve = BandCurve(25, 500, 0);
        CrossoverAutoSetupOptions options = Options(minHz: 40);

        double Split(DriverType upperType) => CrossoverAutoSetup
            .Propose([lower, new AutoSetupSource(curve, upperType)], options)[0]
            .LowPassEdge!.Value.FrequencyHz;

        double sameClass = Split(DriverType.Subwoofer);
        double acrossClasses = Split(DriverType.Woofer);

        Assert.True(
            sameClass < acrossClasses - 20,
            $"A sub-to-sub split was biased up like a sub-to-woofer one: " +
            $"{sameClass:0} Hz against {acrossClasses:0} Hz.");
    }

    [Fact]
    public void Propose_FiveWayWithTwoSubwoofers_OrdersEveryHandover()
    {
        // The installation this whole grouping exists for: a pair of subs
        // splitting the bottom under a three-way front. Five channels, four
        // junctions, two of the five sharing a driver type — which the wizard
        // used to refuse outright.
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

        // Each handover inside the band its two drivers actually share, the
        // sub-to-sub one included — no junction invented where nothing overlaps.
        for (int j = 0; j < splits.Length; j++)
        {
            Assert.InRange(
                splits[j],
                CrossoverAutoSetup.EstimateBand(sources[j + 1].MagnitudeDb).LowHz,
                CrossoverAutoSetup.EstimateBand(sources[j].MagnitudeDb).HighHz);
        }

        // Every channel between the two ends is a band-pass: the second sub is a
        // chain member like any other, not an appendage.
        Assert.All(
            proposals.Skip(1).Take(3),
            proposal => Assert.Equal(CrossoverKind.BandPass, proposal.Kind));
    }

    [Fact]
    public void Propose_SecondSubwoofer_DoesNotSetTheLevelTheSystemIsCutTo()
    {
        // The flat top is fitted to the quietest driver that is not a sub, because
        // a sub is separately amped and a quiet one must not drag the whole system
        // down to it. That holds for EVERY sub in the chain: with the upper sub 10
        // dB down, the midrange and tweeter must still level to each other.
        var lowSub = new AutoSetupSource(BandCurve(20, 60, 0), DriverType.Subwoofer);
        var highSub = new AutoSetupSource(BandCurve(35, 120, -10), DriverType.Subwoofer);
        var midrange = new AutoSetupSource(BandCurve(150, 4_000, 0), DriverType.Midrange);
        var tweeter = new AutoSetupSource(BandCurve(2_500, 20_000, 0), DriverType.Tweeter);

        IReadOnlyList<CrossoverProposal> proposals = CrossoverAutoSetup.Propose(
            [lowSub, highSub, midrange, tweeter], Options());

        Assert.Equal(proposals[2].GainDb, proposals[3].GainDb, 1);
        Assert.Equal(0, proposals[2].GainDb, 1);
    }

    [Fact]
    public void ProposeSingle_ProtectsTheDriverBelowWhereItPlays()
    {
        // A rear fill measured down to ~90 Hz (the -8 dB edge of an 110 Hz
        // driver): the high-pass goes an octave above that, which is the same
        // bound the optimizer holds the upper driver of a junction to. Nothing
        // low-passes it — there is nothing above it to hand over to — and the
        // gain stays put, because levelling this group is a separate step.
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
        // A tweeter measured well down into its own roll-off: an octave above the
        // measured edge (~700 Hz → 1.4 kHz) would sit barely above the resonance
        // floor, so the excursion rule takes over and lifts the corner to where
        // the filter attenuates Fs by the target.
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
    public void ProposeSingle_KeepsTheCornerInsideTheUsersWindow()
    {
        // The window is the user's, and it binds a protective filter as it binds
        // a junction — including the brickwall the topmost driver of a chain gets
        // when the window is pulled in below where it still plays.
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
        // A two-way rear fitted on its own, then levelled against a front stage
        // 6 dB quieter. The whole group slides; what its own fit decided about
        // the balance between its two drivers must survive untouched.
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
        // Cut-only, as everywhere else in the wizard: a group already under the
        // front stage is not boosted into the amplifier's headroom to meet it.
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
        // The rear fill and the centre are groups of one, and they are levelled
        // by the same call as a two-way rear — a lone channel is not a special
        // case, it is a group whose internal fit had nothing to decide.
        var rear = new AutoSetupSource(BandCurve(110, 15_000, 5), DriverType.Midrange);
        CrossoverProposal single = CrossoverAutoSetup.ProposeSingle(rear, Options());

        IReadOnlyList<CrossoverProposal> levelled = CrossoverAutoSetup.OffsetToReferenceLevel(
            [rear], [single], SampleRate, 0);

        // Not exactly -5: the level is read over the passband the high-pass
        // leaves, whose top edge runs a little way into the driver's own roll-off.
        Assert.InRange(levelled[0].GainDb, -5.3, -4.7);
        Assert.Equal(single.HighPassEdge, levelled[0].HighPassEdge);
    }
}
