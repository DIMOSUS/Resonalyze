namespace Resonalyze.Dsp.Tests;

public sealed class EqAutoTunerShelfTests
{
    private const double Rate = 48_000;

    private static IReadOnlyList<SignalPoint> Grid(
        Func<double, double> valueDb,
        int count = 400)
    {
        IReadOnlyList<double> frequencies =
            EqualizationCurve.LogFrequencyGrid(20, 20_000, count);
        return frequencies.Select(f => new SignalPoint(f, valueDb(f))).ToList();
    }

    private static double StepAbove(double f, double cornerHz, double gainDb) =>
        gainDb / (1.0 + Math.Pow(f / cornerHz, -2.0));

    private static double StepBelow(double f, double cornerHz, double gainDb) =>
        gainDb / (1.0 + Math.Pow(f / cornerHz, 2.0));

    private static double Bump(double f, double centreHz, double octaves, double gainDb) =>
        gainDb * Math.Exp(-Math.Pow(Math.Log2(f / centreHz) / octaves, 2));

    private static double TopLiftDb(EqualizationCurve curve) =>
        EqualizationCurve
            .LogFrequencyGrid(6_000, 20_000, 200)
            .Max(f => DigitalEqualizationResponse.MagnitudeDbAt(curve, f, Rate));

    private static double FitRmsDb(
        EqualizationCurve curve,
        IReadOnlyList<SignalPoint> source,
        IReadOnlyList<SignalPoint> target)
    {
        double sumSquares = 0;
        for (int i = 0; i < source.Count; i++)
        {
            double corrected = source[i].Y +
                DigitalEqualizationResponse.MagnitudeDbAt(curve, source[i].X, Rate);
            double residual = target[i].Y - corrected;
            sumSquares += residual * residual;
        }

        return Math.Sqrt(sumSquares / source.Count);
    }

    private static EqAutoTuner.Options CutsOnly => new()
    {
        MaxBands = 10,
        SampleRateHz = Rate,
        QMin = 0.1,
        QMax = 6.0,
        Boosts = EqAutoTuneBoosts.Off,
        TotalGainMaxDb = 0
    };

    private static EqAutoTuner.Options Boosting => new()
    {
        MaxBands = 10,
        SampleRateHz = Rate,
        QMin = 0.1,
        QMax = 6.0,
        BandGainMinDb = -15,
        BandGainMaxDb = 8,
        PreampMinDb = 0,
        PreampMaxDb = 0
    };

    [Fact]
    public void Tune_ShelvesOffByDefault_PlacesBellsOnly()
    {
        // Default off: callers that say nothing keep the bells-only result.
        IReadOnlyList<SignalPoint> source = Grid(f => StepAbove(f, 4_000, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(source, target, CutsOnly);

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, band => Assert.Equal(PeqBandType.Peaking, band.Type));
    }

    [Fact]
    public void Tune_HotTopEnd_TakesOneShelfWhereBellsNeededSeveral()
    {
        // Bells only nibble at a plateau; one high shelf is the shape of the error.
        IReadOnlyList<SignalPoint> source = Grid(f => StepAbove(f, 4_000, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve bells = EqAutoTuner.Tune(source, target, CutsOnly);
        EqualizationCurve shelved = EqAutoTuner.Tune(
            source, target, CutsOnly with { AllowShelves = true });

        PeqBand shelf = Assert.Single(shelved.Bands, band => band.Type.IsShelving());
        Assert.Equal(PeqBandType.HighShelf, shelf.Type);
        Assert.True(
            shelved.Bands.Count < bells.Bands.Count,
            $"the shelf saved no slot: {shelved.Bands.Count} bands against " +
            $"{bells.Bands.Count} without it.");
        double shelvedRms = FitRmsDb(shelved, source, target);
        double bellsRms = FitRmsDb(bells, source, target);
        Assert.True(
            shelvedRms <= bellsRms + 0.01,
            $"the shelf fit is worse: {shelvedRms:0.00} dB against {bellsRms:0.00} dB.");
    }

    [Fact]
    public void Tune_BassDeficit_FitsALowShelfWhenBoostsAreAllowed()
    {
        // With the preamp pinned, only a low shelf can lift the bottom octaves.
        IReadOnlyList<SignalPoint> source = Grid(f => -StepBelow(f, 120, 6));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true });

        PeqBand shelf = Assert.Single(curve.Bands, band => band.Type.IsShelving());
        Assert.Equal(PeqBandType.LowShelf, shelf.Type);
        Assert.True(shelf.GainDb > 0, $"the shelf cuts ({shelf.GainDb:0.0} dB).");
    }

    [Fact]
    public void Tune_CarTarget_ShelvesFitCloserThanBellsAlone()
    {
        // A pure constant-slope tilt is a shape no shelf reproduces, so it is deliberately not asked about.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            Bump(f, 45, 0.3, 5) + Bump(f, 95, 0.25, -6) + Bump(f, 300, 0.4, 3) +
            Bump(f, 2_500, 0.5, -3) + Bump(f, 8_000, 0.6, 4));
        IReadOnlyList<SignalPoint> target = Grid(f =>
            StepBelow(f, 80, 6) - 0.8 * Math.Log2(f / 1_000));

        EqualizationCurve bells = EqAutoTuner.Tune(source, target, Boosting);
        EqualizationCurve shelved = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true });

        Assert.Contains(shelved.Bands, band => band.Type.IsShelving());
        double shelvedRms = FitRmsDb(shelved, source, target);
        double bellsRms = FitRmsDb(bells, source, target);
        Assert.True(
            shelvedRms < bellsRms,
            $"shelves did not help the tilt: {shelvedRms:0.00} dB against " +
            $"{bellsRms:0.00} dB.");
    }

    [Fact]
    public void Tune_BothEndsSloped_TakesAShelfAtEach()
    {
        // Each direction goes to a finished fit and is ranked there; the second shelf is searched against the first's residual.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            StepAbove(f, 3_000, 12) - StepBelow(f, 150, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve bells = EqAutoTuner.Tune(source, target, Boosting);
        EqualizationCurve shelved = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true });

        Assert.Contains(shelved.Bands, band => band.Type == PeqBandType.LowShelf);
        Assert.Contains(shelved.Bands, band => band.Type == PeqBandType.HighShelf);
        Assert.True(
            shelved.Bands.Count < bells.Bands.Count,
            $"the shelves saved no slot: {shelved.Bands.Count} bands against " +
            $"{bells.Bands.Count} without them.");
        double shelvedRms = FitRmsDb(shelved, source, target);
        double bellsRms = FitRmsDb(bells, source, target);
        Assert.True(
            shelvedRms < bellsRms,
            $"two shelves fitted worse than bells: {shelvedRms:0.00} dB against " +
            $"{bellsRms:0.00} dB.");
    }

    [Fact]
    public void Tune_ShelfThatOnlyWinsOnTheFinishedFit_IsStillFound()
    {
        // The best single-band shelf (631 Hz) finishes at 1.276 vs 1.275 for none; the winner lies 2.5 octaves lower
        // and only appears when every candidate is taken to a finished fit.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            -0.6 * Math.Log2(f / 1_000) + Bump(f, 2_000, 0.3, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);
        EqAutoTuner.Options options = CutsOnly with { MaxBands = 2 };

        EqualizationCurve bells = EqAutoTuner.Tune(source, target, options);
        EqualizationCurve shelved = EqAutoTuner.Tune(
            source, target, options with { AllowShelves = true });

        PeqBand shelf = Assert.Single(shelved.Bands, band => band.Type.IsShelving());
        Assert.Equal(PeqBandType.LowShelf, shelf.Type);
        Assert.True(
            shelf.FrequencyHz < 300,
            $"the shelf landed at {shelf.FrequencyHz:0} Hz — the corner the single-band " +
            "score prefers is 631 Hz, and that one finishes worse than placing none.");
        double shelvedRms = FitRmsDb(shelved, source, target);
        double bellsRms = FitRmsDb(bells, source, target);
        Assert.True(
            shelvedRms < bellsRms - 0.1,
            $"the shelf bought nothing: {shelvedRms:0.000} dB against " +
            $"{bellsRms:0.000} dB.");
    }

    [Fact]
    public void Tune_BumpsOnly_SpendsNoSlotOnAShelf()
    {
        // Three resonances, no trend: a shelf would move everything beside a bump.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            Bump(f, 120, 0.2, 8) + Bump(f, 900, 0.25, -6) + Bump(f, 5_000, 0.3, 5));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve cuts = EqAutoTuner.Tune(
            source, target, CutsOnly with { AllowShelves = true });
        EqualizationCurve boosts = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true });

        Assert.DoesNotContain(cuts.Bands, band => band.Type.IsShelving());
        Assert.DoesNotContain(boosts.Bands, band => band.Type.IsShelving());
    }

    [Fact]
    public void Tune_CutsOnly_AShelfNeverLiftsTheCurveAnywhere()
    {
        // Cuts-only needs a shelf knee ≤ 1/sqrt(2): a sharper knee overshoots, and on a cut that overshoot is a boost.
        IReadOnlyList<SignalPoint> source = Grid(f =>
            StepAbove(f, 4_000, 10) + Bump(f, 80, 0.3, 5) + Bump(f, 400, 0.3, -4));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source, target, CutsOnly with { AllowShelves = true });

        Assert.Contains(curve.Bands, band => band.Type.IsShelving());
        foreach (PeqBand band in curve.Bands.Where(band => band.Type.IsShelving()))
        {
            Assert.True(band.GainDb < 0, $"cuts-only fitted a +{band.GainDb:0.0} dB shelf.");
            Assert.True(band.Q <= 0.7, $"shelf Q {band.Q:0.0} overshoots.");
        }

        double peak = EqualizationCurve
            .LogFrequencyGrid(20, 23_000, 2_000)
            .Max(f => DigitalEqualizationResponse.MagnitudeDbAt(curve, f, Rate));
        Assert.True(peak <= 1e-6, $"the cuts-only profile boosts by {peak:0.000000} dB.");
    }

    [Fact]
    public void Tune_LowCoherentTail_IsNotShelvedUpwards()
    {
        // A boosting shelf is gated on its plateau being measured, not per bin (the skirt guard would refuse every shelf).
        IReadOnlyList<SignalPoint> source = Grid(f => -StepAbove(f, 4_000, 7));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);
        IReadOnlyList<SignalPoint> coherent = Grid(_ => 0.99);
        IReadOnlyList<SignalPoint> incoherentTop = Grid(f => f > 2_000 ? 0.1 : 0.99);

        EqualizationCurve trusted = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true }, coherent);
        EqualizationCurve doubted = EqAutoTuner.Tune(
            source, target, Boosting with { AllowShelves = true }, incoherentTop);

        Assert.Contains(
            trusted.Bands,
            band => band.Type == PeqBandType.HighShelf && band.GainDb > 0);

        double trustedLift = TopLiftDb(trusted);
        double doubtedLift = TopLiftDb(doubted);
        Assert.True(
            trustedLift > 4,
            $"the coherent tail was left {trustedLift:0.0} dB short of its deficit.");
        Assert.True(
            doubtedLift < 1.0,
            $"the incoherent tail was lifted by {doubtedLift:0.0} dB.");
    }

    [Fact]
    public void Tune_ShelvesObeyTheBandBudgetAndTheGainRange()
    {
        IReadOnlyList<SignalPoint> source = Grid(f =>
            StepAbove(f, 3_000, 12) - StepBelow(f, 150, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        foreach (int budget in new[] { 1, 2, 3, 10 })
        {
            EqualizationCurve curve = EqAutoTuner.Tune(
                source,
                target,
                Boosting with { AllowShelves = true, MaxBands = budget });

            Assert.True(
                curve.Bands.Count <= budget,
                $"budget {budget} produced {curve.Bands.Count} bands.");
            Assert.True(
                curve.Bands.Count(band => band.Type == PeqBandType.LowShelf) <= 1,
                $"budget {budget} produced more than one low shelf.");
            Assert.True(
                curve.Bands.Count(band => band.Type == PeqBandType.HighShelf) <= 1,
                $"budget {budget} produced more than one high shelf.");
            Assert.All(curve.Bands, band => Assert.InRange(band.GainDb, -15, 8));
        }
    }

    [Fact]
    public void Tune_FittingWindowTooNarrowForAPlateau_PlacesNoShelf()
    {
        // A corner needs an octave each side; a narrower window must not throw.
        IReadOnlyList<SignalPoint> source = Grid(f => StepAbove(f, 300, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(
            source,
            target,
            CutsOnly with
            {
                AllowShelves = true,
                MinFrequencyHz = 200,
                MaxFrequencyHz = 400
            });

        Assert.DoesNotContain(curve.Bands, band => band.Type.IsShelving());
    }

    [Fact]
    public void Tune_ShelfIsFittedAgainstTheProcessorRate()
    {
        // Scored through the digital response at the processor's rate.
        Func<double, double> shape = f => StepAbove(f, 4_000, 8);
        IReadOnlyList<SignalPoint> source = Grid(shape);
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        foreach (double rate in new[] { 48_000.0, 96_000.0 })
        {
            EqualizationCurve curve = EqAutoTuner.Tune(
                source, target, CutsOnly with { AllowShelves = true, SampleRateHz = rate });

            PeqBand shelf = Assert.Single(curve.Bands, band => band.Type.IsShelving());
            Assert.Equal(PeqBandType.HighShelf, shelf.Type);

            double worst = EqualizationCurve
                .LogFrequencyGrid(8_000, 20_000, 200)
                .Max(f => Math.Abs(
                    shape(f) + DigitalEqualizationResponse.MagnitudeDbAt(curve, f, rate)));
            Assert.True(
                worst < 2.0,
                $"the {rate / 1000:0} kHz fit leaves {worst:0.0} dB across the plateau.");
        }
    }
}
