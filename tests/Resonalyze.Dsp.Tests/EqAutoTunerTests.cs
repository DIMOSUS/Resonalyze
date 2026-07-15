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
        // Target sits +10 dB above source with an extra +6 dB local bump: the
        // unconstrained fit hands out preamp +10 plus a +6 boost band — a
        // profile that clips by +16 dB before the UI ever shows the headroom.
        // With the total-gain ceiling the preamp is capped so preamp + band
        // boost never exceeds 0 dB anywhere; the fitted band shape stays.
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
        // Target = source plus a single peaking bump; the tuner should invert it.
        var bump = new PeqBand(1_000, 2.0, 6.0);
        IReadOnlyList<SignalPoint> source = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> target = Grid(f => bump.MagnitudeDbAt(f));

        EqualizationCurve curve = EqAutoTuner.Tune(source, target);

        Assert.NotEmpty(curve.Bands);
        // The curve must reconstruct the error (= bump) across the band.
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
        // A jagged target forces many corrections; the band count must stay bounded.
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
        // A +20 dB deficit cannot be met by a single band capped at +6 dB.
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
        // Source rolls off below 100 Hz (a deficit EQ cannot recover); target is
        // flat. The fit must not stack many max-boost bands at 20 Hz nor exceed the
        // boost ceiling.
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
        // The boost stays near the per-band ceiling (overlapping tails add a little)
        // rather than blowing up to tens of dB as an uncapped fit would.
        Assert.True(maxBoost <= 8.0, $"Max boost {maxBoost:0.0} dB exceeded the cap.");

        // No more than one band should sit in the bottom octave (20-40 Hz).
        int lowBands = curve.Bands.Count(b => b.FrequencyHz < 40);
        Assert.True(lowBands <= 1, $"{lowBands} bands stacked in the low bass.");
    }

    [Fact]
    public void Tune_ClampsCutBandsToTheConfiguredFloor()
    {
        // A +20 dB peak needs a -20 dB cut, but the band floor is -15 dB: every band
        // must respect Math.Max(desired, BandGainMinDb). The existing test only pins
        // the boost ceiling; this pins the cut floor.
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
        // The source only covers 50-5000 Hz, so grid bins outside it resample to NaN
        // and the valid[]/validCount mask must exclude them (validCount > 0). This is
        // the realistic case; every other fitting test spans the full grid.
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
        // No band produces a NaN anywhere on the audible grid.
        Assert.All(full, f => Assert.True(double.IsFinite(curve.MagnitudeDbAt(f))));
        // The fit substantially reduces the error inside the overlap.
        double initial = FitRmsDb(new EqualizationCurve([]), source, source.Select(p => new SignalPoint(p.X, 0.0)).ToList());
        double final = FitRmsDb(curve, source, source.Select(p => new SignalPoint(p.X, 0.0)).ToList());
        Assert.True(final < initial * 0.5, $"Overlap error not reduced: {initial:0.00} -> {final:0.00} dB.");
    }

    [Fact]
    public void Tune_QRangeExcludingAllCandidates_FallsBackWithoutThrowing()
    {
        // [QMin, QMax] = [3, 3.5] excludes every entry of the fixed candidate-Q list
        // (2.8 and 4.0 straddle it), so the fitter must fall back to a single clamped
        // Q rather than crash on an empty candidate array.
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
        // Source and target cover disjoint frequency ranges -> nothing to fit.
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
        // Source has a +8 dB peak at 500 Hz and a -8 dB dip at 3000 Hz against a flat
        // target. Cuts-only must shave the peak but leave the dip alone (no boost).
        var peak = new PeqBand(500, 3.0, 8.0);
        var dip = new PeqBand(3_000, 3.0, -8.0);
        IReadOnlyList<SignalPoint> source = Grid(f => peak.MagnitudeDbAt(f) + dip.MagnitudeDbAt(f));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { CutsOnlyMode = true });

        Assert.All(curve.Bands, band => Assert.True(band.GainDb <= 0 + 1e-9));
        // The whole EQ (bands + preamp) never boosts anywhere.
        double maxGain = EqualizationCurve
            .LogFrequencyGrid(20, 20_000, 400)
            .Max(curve.MagnitudeDbAt);
        Assert.True(maxGain <= 0.05, $"Cuts-only produced a {maxGain:0.0} dB boost.");
        // The +8 dB peak is still corrected downward.
        Assert.True(curve.MagnitudeDbAt(500) < -1.0);
    }

    [Fact]
    public void Tune_BoostsAllowed_SkipsANarrowDeepNull()
    {
        // A narrow deep null at 3 kHz (uncorrectable) and a broad shallow dip at 200 Hz
        // (a correctable trend) against a flat target. With boosts enabled, the fit must
        // boost the broad dip but never centre a boost inside the narrow null.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            NotchDb(f, 3_000, 12, 0.15) + NotchDb(f, 200, 6, 0.7));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, new EqAutoTuner.Options { CutsOnlyMode = false });

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
        // A broad, boostable dip at 1 kHz that the fit WOULD boost — but the coherence
        // there is below the floor, so the boost is withheld. Without the coherence it
        // is boosted (the control assertion), isolating the coherence gate.
        IReadOnlyList<SignalPoint> source = Grid(f => NotchDb(f, 1_000, 6, 0.7));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);
        var options = new EqAutoTuner.Options { CutsOnlyMode = false };

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
    public void Tune_CutsOnly_ClusterOfNarrowPeaks_EachGetsCut()
    {
        // Five narrow peaks packed ~0.2 octave apart between 1-2 kHz above a flat
        // target. The old fixed 0.33-octave band spacing let the first cut sterilise
        // its neighbours, leaving most of the cluster above target; the per-band
        // footprint must now cut essentially the whole cluster.
        double[] centres = { 1050, 1200, 1400, 1600, 1850 };
        IReadOnlyList<SignalPoint> source = Grid(
            f => centres.Sum(c => new PeqBand(c, 8, 5).MagnitudeDbAt(f)));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            new EqAutoTuner.Options
            {
                CutsOnlyMode = true,
                MinFrequencyHz = 200,
                MaxFrequencyHz = 2000,
                BandGainMinDb = -18
            });

        // Nothing in the cluster still pokes meaningfully above the target.
        double worstAbove = EqualizationCurve
            .LogFrequencyGrid(1_000, 2_000, 200)
            .Max(f => SampleDb(source, f) + curve.MagnitudeDbAt(f));
        Assert.True(worstAbove <= 1.0, $"Cluster peak of +{worstAbove:0.0} dB left uncut.");
        // It took more than the ~2 bands the old coarse spacing allowed in one octave.
        Assert.True(curve.Bands.Count >= 3, $"Only {curve.Bands.Count} bands used on the cluster.");
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

    // A symmetric V-notch used to synthesise correctable dips and uncorrectable nulls.
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
