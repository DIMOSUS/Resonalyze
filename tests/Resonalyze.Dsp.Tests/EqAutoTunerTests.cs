namespace Resonalyze.Dsp.Tests;

public sealed class EqAutoTunerTests
{
    private static IReadOnlyList<SignalPoint> Grid(
        Func<double, double> valueDb,
        int count = 400)
    {
        IReadOnlyList<double> frequencies =
            EqualizationCurve.LogFrequencyGrid(20, 20_000, count);
        return frequencies.Select(f => new SignalPoint(f, valueDb(f))).ToList();
    }

    private static double FitRmsDb(
        EqualizationCurve curve,
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target)
    {
        double sumSquares = 0;
        for (int i = 0; i < source.Count; i++)
        {
            double corrected = source[i].Y + curve.MagnitudeDbAt(source[i].X);
            double residual = target[i].Y - corrected;
            sumSquares += residual * residual;
        }

        return Math.Sqrt(sumSquares / source.Count);
    }

    [Fact]
    public void Tune_TotalGainCapPreventsAClippingProfile()
    {
        // The total-gain ceiling caps preamp + band boost at 0 dB; the fitted band shape stays.
        var bump = new PeqBand(1_000, 2.0, 6.0);
        IReadOnlyList<SignalPoint> source = Grid(_ => -40.0);
        IReadOnlyList<SignalPoint> target = Grid(
            f => -30.0 + bump.MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { TotalGainMaxDb = 0 });

        double maxTotal = EqualizationCurve
            .LogFrequencyGrid(20, 20_000, 400)
            .Max(f => curve.MagnitudeDbAt(f));
        Assert.True(
            maxTotal <= 0.05,
            $"total EQ gain peaks at {maxTotal:0.0} dB — a clipping profile.");
        Assert.NotEmpty(curve.Bands);
    }

    [Fact]
    public void Tune_PinnedPreamp_RaisesAWindowBelowTargetInsteadOfLoweringTheCurve()
    {
        // Boost mode pins the preamp and has no ceiling: correction must come from boost bands.
        // A post-fit ceiling once dropped the realised curve by the peak boost.
        IReadOnlyList<SignalPoint> source = Grid(_ => -5.0);
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                MinFrequencyHz = 20,
                MaxFrequencyHz = 300,
                PreampMinDb = 0,
                PreampMaxDb = 0,
                BandGainMaxDb = 6
            });

        Assert.Equal(0, curve.PreampDb, 9);
        Assert.NotEmpty(curve.Bands);
        Assert.True(
            curve.MagnitudeDbAt(100) > 3.0,
            $"the window got {curve.MagnitudeDbAt(100):0.0} dB of the needed +5.");
        // ...and nothing is pulled down (the failure being pinned).
        double minGain = EqualizationCurve
            .LogFrequencyGrid(20, 20_000, 400)
            .Min(curve.MagnitudeDbAt);
        Assert.True(minGain >= -0.5, $"the curve was lowered by {minGain:0.0} dB.");
    }

    [Fact]
    public void Tune_ReadsAnUnmeasuredBandAsUnknown_NotAsSilence()
    {
        // NaN below a divided-out protective high-pass must not read as a 40 dB hole to fill.
        IReadOnlyList<SignalPoint> source = Grid(
            f => f < 560 ? double.NaN : 0.0);
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.Empty(curve.Bands);
        Assert.Equal(0.0, curve.PreampDb, 6);
    }

    [Fact]
    public void Tune_StillCorrectsWhatWasMeasured_BesideAnUnmeasuredBand()
    {
        var bump = new PeqBand(4_000, 2.0, 6.0);
        IReadOnlyList<SignalPoint> source = Grid(
            f => f < 560 ? double.NaN : 0.0);
        IReadOnlyList<SignalPoint> target = Grid(f => bump.MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.NotEmpty(curve.Bands);
        Assert.True(
            Math.Abs(curve.MagnitudeDbAt(4_000) - 6.0) < 0.5,
            $"the measured bump was corrected by {curve.MagnitudeDbAt(4_000):0.00} dB");
    }

    [Fact]
    public void Tune_ConstantLevelDifference_UsesPreampAndNoBands()
    {
        IReadOnlyList<SignalPoint> source = Grid(_ => -40);
        IReadOnlyList<SignalPoint> target = Grid(_ => -30);

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.Empty(curve.Bands);
        Assert.Equal(10, curve.PreampDb, 6);
    }

    [Fact]
    public void Tune_SingleBump_RecoversCorrection()
    {
        var bump = new PeqBand(1_000, 2.0, 6.0);
        IReadOnlyList<SignalPoint> source = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> target = Grid(f => bump.MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.NotEmpty(curve.Bands);
        Assert.True(Math.Abs(curve.MagnitudeDbAt(1_000) - 6.0) < 0.5);
        Assert.True(FitRmsDb(curve, source, target) < 0.75);
    }

    [Fact]
    public void Tune_MultiplePeaks_ReducesErrorSubstantially()
    {
        var peaks = new[]
        {
            new PeqBand(120, 3.0, -8.0),
            new PeqBand(900, 2.0, 5.0),
            new PeqBand(4_500, 4.0, -6.0)
        };
        IReadOnlyList<SignalPoint> source = Grid(_ => -45.0);
        IReadOnlyList<SignalPoint> target = Grid(
            f => -45.0 + peaks.Sum(p => p.MagnitudeDbAt(f)));

        double initialRms = FitRmsDb(
            new EqualizationCurve(Array.Empty<PeqBand>()), source, target);

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        double finalRms = FitRmsDb(curve, source, target);
        Assert.True(
            finalRms < initialRms * 0.2,
            $"Expected strong error reduction, got {initialRms:0.00} -> {finalRms:0.00} dB");
    }

    [Fact]
    public void Tune_RespectsBandBudget()
    {
        var random = new Random(7);
        IReadOnlyList<SignalPoint> source = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> target = Grid(_ => (random.NextDouble() - 0.5) * 20);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options { MaxBands = 5 });

        Assert.True(curve.Bands.Count <= 5);
    }

    [Fact]
    public void Tune_ClampsBandGainToConfiguredRange()
    {
        IReadOnlyList<SignalPoint> source = Grid(_ => 0.0);
        var bump = new PeqBand(1_000, 3.0, 20.0);
        IReadOnlyList<SignalPoint> target = Grid(f => bump.MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options { BandGainMaxDb = 6, PreampMaxDb = 0 });

        Assert.All(curve.Bands, band => Assert.True(band.GainDb <= 6 + 1e-9));
    }

    [Fact]
    public void Tune_LowFrequencyRollOff_CapsBoostAndDoesNotStackBands()
    {
        // A roll-off below 100 Hz is unrecoverable: no stacked max-boost bands at 20 Hz.
        IReadOnlyList<SignalPoint> source = Grid(f => f < 100 ? -30.0 : 0.0);
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        var options = new EqAutoTuner.Options
        {
            BandGainMaxDb = 6,
            PreampMaxDb = 0
        };
        EqualizationCurve curve = EqAutoTuner.Tune(source, target, options);

        double maxBoost = EqualizationCurve
            .LogFrequencyGrid(20, 20_000, 256)
            .Max(curve.MagnitudeDbAt);
        // Overlapping tails add a little over the per-band cap.
        Assert.True(maxBoost <= 8.0, $"Max boost {maxBoost:0.0} dB exceeded the cap.");

        int lowBands = curve.Bands.Count(b => b.FrequencyHz < 40);
        Assert.True(lowBands <= 1, $"{lowBands} bands stacked in the low bass.");
    }

    [Fact]
    public void Tune_ClampsCutBandsToTheConfiguredFloor()
    {
        // Pins the -15 dB cut floor (BandGainMinDb).
        IReadOnlyList<SignalPoint> source = Grid(f => new PeqBand(1_000, 3.0, 20.0).MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options { BandGainMinDb = -15, PreampMinDb = 0 });

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, band => Assert.True(band.GainDb >= -15 - 1e-9));
    }

    [Fact]
    public void Tune_PartialOverlap_FitsTheOverlapWithoutNaN()
    {
        // Source covers 50-5000 Hz only: grid bins outside resample to NaN and must be masked.
        IReadOnlyList<double> full = EqualizationCurve.LogFrequencyGrid(20, 20_000, 400);
        var bump = new PeqBand(1_000, 2.0, 8.0);
        IReadOnlyList<SignalPoint> source = full
            .Where(f => f is >= 50 and <= 5_000)
            .Select(f => new SignalPoint(f, bump.MagnitudeDbAt(f)))
            .ToList();
        IReadOnlyList<SignalPoint> target = full.Select(f => new SignalPoint(f, 0.0)).ToList();

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, b => Assert.InRange(b.FrequencyHz, 50, 5_000));
        Assert.All(full, f => Assert.True(double.IsFinite(curve.MagnitudeDbAt(f))));
        double initial = FitRmsDb(new EqualizationCurve([]), source, source.Select(p => new SignalPoint(p.X, 0.0)).ToList());
        double final = FitRmsDb(curve, source, source.Select(p => new SignalPoint(p.X, 0.0)).ToList());
        Assert.True(final < initial * 0.5, $"Overlap error not reduced: {initial:0.00} -> {final:0.00} dB.");
    }

    [Fact]
    public void Tune_MaxQ_KeepsEveryBandAtOrBelowTheCeiling()
    {
        // Max Q exists to refuse the narrowest bands: a peak read at one mic position.
        IReadOnlyList<SignalPoint> source = Grid(
            f => new PeqBand(1_000, 10.0, 12.0).MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve unbounded = EqAutoTuner.Tune(source, target);
        EqualizationCurve capped = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { QMax = 6.0 });

        Assert.Contains(unbounded.Bands, band => band.Q > 6.0);
        Assert.NotEmpty(capped.Bands);
        Assert.All(
            capped.Bands,
            band => Assert.True(band.Q <= 6.0, $"band at Q {band.Q} is past the ceiling."));
    }

    [Fact]
    public void Tune_NarrowQRange_KeepsEveryBandInsideIt()
    {
        IReadOnlyList<SignalPoint> source = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> target = Grid(f => new PeqBand(1_000, 3.0, 6.0).MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options { QMin = 3.0, QMax = 3.5 });

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, band => Assert.InRange(band.Q, 3.0, 3.5));
    }

    [Fact]
    public void Tune_NoOverlappingFrequencyData_ReturnsEmptyCurve()
    {
        var source = new List<SignalPoint>
        {
            new(20, 0), new(40, 0), new(80, 0)
        };
        var target = new List<SignalPoint>
        {
            new(5_000, 6), new(10_000, 6), new(20_000, 6)
        };

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.Empty(curve.Bands);
        Assert.Equal(0, curve.PreampDb, 6);
    }

    [Fact]
    public void Tune_CutsOnlyMode_NeverBoosts()
    {
        var peak = new PeqBand(500, 3.0, 8.0);
        var dip = new PeqBand(3_000, 3.0, -8.0);
        IReadOnlyList<SignalPoint> source = Grid(f => peak.MagnitudeDbAt(f) + dip.MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { Boosts = EqAutoTuneBoosts.Off });

        Assert.All(curve.Bands, band => Assert.True(band.GainDb <= 0 + 1e-9));
        double maxGain = EqualizationCurve
            .LogFrequencyGrid(20, 20_000, 400)
            .Max(curve.MagnitudeDbAt);
        Assert.True(maxGain <= 0.05, $"Cuts-only produced a {maxGain:0.0} dB boost.");
        Assert.True(curve.MagnitudeDbAt(500) < -1.0);
    }

    [Fact]
    public void Tune_CutsOnly_DoesNotLowerBelowTargetRegionsWithANegativePreamp()
    {
        // Cuts-only cannot lift the dip, so the preamp must not centre on the mean error (-5) and push the dip further down.
        var dip = new PeqBand(1_000, 3.0, -10.0); // 5 above baseline (+5) → 5 below target
        IReadOnlyList<SignalPoint> source = Grid(f => 5.0 + dip.MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { Boosts = EqAutoTuneBoosts.Off });

        // The discriminator: a mean-centred preamp (~ -4) fails this.
        Assert.True(
            curve.PreampDb >= -0.5,
            $"Cuts-only lowered the whole curve by {curve.PreampDb:0.0} dB preamp.");
        double dipCorrected = source.First(p => p.X >= 1_000).Y
            + curve.MagnitudeDbAt(1_000);
        Assert.True(
            dipCorrected is <= 0.5 and >= -8.0,
            $"The below-target dip was pushed to {dipCorrected:0.0} dB (target 0).");
        Assert.True(source.First(p => p.X >= 5_000).Y + curve.MagnitudeDbAt(5_000) < 1.5);
    }

    [Fact]
    public void Tune_CutsOnly_DoesNotGougeAShoulderBelowTargetToCutABroadHfPlateau()
    {
        // A wide band shaving the HF plateau would gouge the 4-6 kHz shoulder below target; prefer tighter bands.
        // Measured on the digital response, which diverges from analog this high.
        const double sampleRate = 44_100;
        var bump = new PeqBand(9_500, 2.5, 5.0);
        IReadOnlyList<SignalPoint> source = Grid(f =>
            (11.0 * 0.5 * (1 + Math.Tanh(Math.Log2(f / 6_500.0) / 0.65))) + bump.MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Off,
                SampleRateHz = sampleRate,
                MinFrequencyHz = 2_000,
                MaxFrequencyHz = 20_000,
                BandGainMinDb = -15
            });

        double worstBelow = 0;
        double atHz = 0;
        foreach (double f in EqualizationCurve.LogFrequencyGrid(2_000, 20_000, 400))
        {
            double corrected = SampleDb(source, f)
                + DigitalEqualizationResponse.MagnitudeDbAt(curve, f, sampleRate);
            if (-corrected > worstBelow)
            {
                worstBelow = -corrected;
                atHz = f;
            }
        }

        Assert.True(
            worstBelow <= 2.0,
            $"Cuts-only gouged {worstBelow:0.0} dB below the target at {atHz:0} Hz.");
    }

    [Fact]
    public void Tune_BoostsAllowed_SkipsANarrowDeepNull()
    {
        // Boost the broad 200 Hz dip, never centre a boost in the narrow 3 kHz null.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            NotchDb(f, 3_000, 12, 0.15) + NotchDb(f, 200, 6, 0.7));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { Boosts = EqAutoTuneBoosts.Allowed });

        Assert.DoesNotContain(
            curve.Bands,
            band => band.GainDb > 0 && band.FrequencyHz is >= 2_400 and <= 3_600);
        Assert.Contains(
            curve.Bands,
            band => band.GainDb > 0 && band.FrequencyHz is >= 130 and <= 320);
    }

    [Fact]
    public void Tune_BoostsAllowed_LowCoherenceRegionIsNotBoosted()
    {
        // Low coherence withholds a boost the fit would otherwise apply (control assertion without coherence).
        IReadOnlyList<SignalPoint> source = Grid(f => NotchDb(f, 1_000, 6, 0.7));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);
        var options = new EqAutoTuner.Options { Boosts = EqAutoTuneBoosts.Allowed };

        IReadOnlyList<SignalPoint> coherence = Grid(
            f => Math.Abs(Math.Log2(f / 1_000)) < 0.5 ? 0.2 : 0.95);

        EqualizationCurve gated = EqAutoTuner.Tune(source, target, options, coherence);
        EqualizationCurve ungated = EqAutoTuner.Tune(source, target, options);

        Assert.DoesNotContain(
            gated.Bands,
            band => band.GainDb > 0 && band.FrequencyHz is >= 700 and <= 1_400);
        Assert.Contains(
            ungated.Bands,
            band => band.GainDb > 0 && band.FrequencyHz is >= 700 and <= 1_400);
    }

    [Fact]
    public void Tune_BoostSkirtDoesNotFillAForbiddenBin()
    {
        // A wide boost on a reliable shoulder pours dB into the low-coherence core through its skirt.
        IReadOnlyList<SignalPoint> source = Grid(f => NotchDb(f, 1_000, 6, 0.8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> coherence = Grid(
            f => f is >= 900 and <= 1_100 ? 0.2 : 0.95);

        EqualizationCurve spilled = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Allowed,
                PreampMinDb = 0,
                PreampMaxDb = 0,
                ForbiddenRegionMaxBoostDb = double.PositiveInfinity
            },
            coherence);
        Assert.True(
            spilled.MagnitudeDbAt(1_000) > 0.5,
            $"Control: skirt should have filled the core, got {spilled.MagnitudeDbAt(1_000):0.00} dB.");

        EqualizationCurve gated = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Allowed,
                PreampMinDb = 0,
                PreampMaxDb = 0
            },
            coherence);
        Assert.True(
            gated.MagnitudeDbAt(1_000) <= 0.5,
            $"Forbidden core boosted by {gated.MagnitudeDbAt(1_000):0.00} dB.");
        Assert.Contains(
            gated.Bands,
            band => band.GainDb > 0 &&
                (band.FrequencyHz is >= 600 and < 900 or >= 1_100 and <= 1_600));
    }

    [Fact]
    public void Tune_MaskedNullInsideABroadDip_StillCorrectsTheShoulders()
    {
        // Blocking only the forbidden null core lets the reliable shoulders be corrected.
        IReadOnlyList<SignalPoint> source = Grid(
            f => NotchDb(f, 1_000, 6, 1.0) + NotchDb(f, 1_000, 12, 0.12));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Allowed,
                PreampMinDb = 0,
                PreampMaxDb = 0
            });

        Assert.True(
            curve.MagnitudeDbAt(750) > 1.5,
            $"Low shoulder left uncorrected ({curve.MagnitudeDbAt(750):0.00} dB).");
        Assert.True(
            curve.MagnitudeDbAt(1_350) > 1.5,
            $"High shoulder left uncorrected ({curve.MagnitudeDbAt(1_350):0.00} dB).");
        Assert.True(
            curve.MagnitudeDbAt(1_000) <= 0.5,
            $"Null floor boosted by {curve.MagnitudeDbAt(1_000):0.00} dB.");
    }

    [Fact]
    public void Tune_CutsOnly_ClusterOfNarrowPeaks_EachGetsCut()
    {
        // Five narrow peaks ~0.2 oct apart: a fixed 0.33-oct spacing would let one cut sterilise its neighbours.
        double[] centres = { 1050, 1200, 1400, 1600, 1850 };
        IReadOnlyList<SignalPoint> source = Grid(
            f => centres.Sum(c => new PeqBand(c, 8, 5).MagnitudeDbAt(f)));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Off,
                MinFrequencyHz = 200,
                MaxFrequencyHz = 2000,
                BandGainMinDb = -18
            });

        double worstAbove = EqualizationCurve
            .LogFrequencyGrid(1_000, 2_000, 200)
            .Max(f => SampleDb(source, f) + curve.MagnitudeDbAt(f));
        Assert.True(worstAbove <= 1.0, $"Cluster peak of +{worstAbove:0.0} dB left uncut.");
        Assert.True(curve.Bands.Count >= 3, $"Only {curve.Bands.Count} bands used on the cluster.");
    }

    [Fact]
    public void Tune_CutsOnly_SmoothLobeBetweenDipsGetsAWideBandNotASwarmOfSlivers()
    {
        // A smooth lobe flanked by dips: a depth-scaled over-cut penalty collapsed bands to max Q and combed the lobe.
        const double sampleRate = 44_100;
        IReadOnlyList<SignalPoint> source = Grid(f =>
            3.5 * Math.Exp(-Math.Pow(Math.Log2(f / 10_500.0) / 0.4, 2))
            + NotchDb(f, 6_800, 4.0, 0.55)
            + NotchDb(f, 16_500, 3.0, 0.45));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                Boosts = EqAutoTuneBoosts.Off,
                SampleRateHz = sampleRate,
                MinFrequencyHz = 2_000,
                MaxFrequencyHz = 20_000,
                BandGainMinDb = -15
            });

        PeqBand deepest = curve.Bands.OrderBy(band => band.GainDb).First();
        Assert.True(
            deepest.FrequencyHz is >= 8_000 and <= 14_000,
            $"Deepest cut landed at {deepest.FrequencyHz:0} Hz, outside the lobe.");
        Assert.True(
            deepest.Q <= 5.6,
            $"The smooth lobe was cut with a Q={deepest.Q:0.0} sliver.");
        double worstAbove = EqualizationCurve
            .LogFrequencyGrid(8_500, 13_000, 200)
            .Max(f => SampleDb(source, f)
                + DigitalEqualizationResponse.MagnitudeDbAt(curve, f, sampleRate));
        Assert.True(
            worstAbove <= 1.2,
            $"The corrected lobe still pokes +{worstAbove:0.0} dB above the target.");
    }

    private static double SampleDb(IReadOnlyList<SignalPoint> curve, double frequencyHz)
    {
        SignalPoint below = curve[0], above = curve[^1];
        foreach (SignalPoint p in curve)
        {
            if (p.X <= frequencyHz && p.X >= below.X) below = p;
            if (p.X >= frequencyHz) { above = p; break; }
        }

        if (above.X <= below.X) return below.Y;
        double t = (Math.Log(frequencyHz) - Math.Log(below.X)) / (Math.Log(above.X) - Math.Log(below.X));
        return below.Y + t * (above.Y - below.Y);
    }

    // Symmetric V-notch for correctable dips and uncorrectable nulls.
    private static double NotchDb(double f, double centerHz, double depthDb, double halfWidthOctaves)
    {
        double octaves = Math.Abs(Math.Log2(f / centerHz));
        return octaves >= halfWidthOctaves ? 0.0 : -depthDb * (1.0 - octaves / halfWidthOctaves);
    }

    [Fact]
    public void Tune_HighFrequencyTarget_FitsDigitalResponseAtConfiguredRate()
    {
        const double sampleRate = 44_100;
        var targetCurve = new EqualizationCurve([new PeqBand(18_000, 2.0, 6.0)]);
        IReadOnlyList<double> frequencies =
            EqualizationCurve.LogFrequencyGrid(8_000, 20_000, 300);
        IReadOnlyList<SignalPoint> source = frequencies
            .Select(frequency => new SignalPoint(frequency, 0))
            .ToArray();
        IReadOnlyList<SignalPoint> target = frequencies
            .Select(frequency => new SignalPoint(
                frequency,
                DigitalEqualizationResponse.MagnitudeDbAt(
                    targetCurve, frequency, sampleRate)))
            .ToArray();

        EqualizationCurve fitted = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                SampleRateHz = sampleRate,
                MinFrequencyHz = 8_000,
                MaxFrequencyHz = 20_000,
                MaxBands = 4
            });

        double expected = DigitalEqualizationResponse.MagnitudeDbAt(
            targetCurve, 18_000, sampleRate);
        double actual = DigitalEqualizationResponse.MagnitudeDbAt(
            fitted, 18_000, sampleRate);
        Assert.InRange(actual, expected - 1.0, expected + 1.0);
    }
}
