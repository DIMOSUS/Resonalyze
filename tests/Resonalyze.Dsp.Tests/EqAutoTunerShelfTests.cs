namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The optional shelf stage (<see cref="EqAutoTuner.Options.AllowShelves"/>): when it
/// places a shelf, when it refuses to, and what it may never do while placing one.
/// </summary>
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

    // A smooth step: gainDb above cornerHz and nothing below it, over about two
    // octaves of transition — the shape a shelf exists for, and the one a bell cannot
    // make.
    private static double StepAbove(double f, double cornerHz, double gainDb) =>
        gainDb / (1.0 + Math.Pow(f / cornerHz, -2.0));

    // Its mirror: gainDb below cornerHz, nothing above it.
    private static double StepBelow(double f, double cornerHz, double gainDb) =>
        gainDb / (1.0 + Math.Pow(f / cornerHz, 2.0));

    private static double Bump(double f, double centreHz, double octaves, double gainDb) =>
        gainDb * Math.Exp(-Math.Pow(Math.Log2(f / centreHz) / octaves, 2));

    // The most the finished EQ lifts anything in the top two octaves of the range.
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
        CutsOnlyMode = true,
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
        // The option has to default off: a caller that says nothing — and every curve
        // fitted before the stage existed — keeps the bells-only result.
        IReadOnlyList<SignalPoint> source = Grid(f => StepAbove(f, 4_000, 8));
        IReadOnlyList<SignalPoint> target = Grid(_ => 0.0);

        EqualizationCurve curve = EqAutoTuner.Tune(source, target, CutsOnly);

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, band => Assert.Equal(PeqBandType.Peaking, band.Type));
    }

    [Fact]
    public void Tune_HotTopEnd_TakesOneShelfWhereBellsNeededSeveral()
    {
        // The case the stage is for: the top of the range runs uniformly hot against the
        // target. Bells can only nibble at a plateau — each one leaves the shoulders of
        // the last — while one high shelf is the shape of the error.
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
        // The boosting half of the same case, and the one a car target asks for: the
        // bottom of the range sits below the target across whole octaves. With the
        // preamp pinned, only a low shelf can lift it.
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
        // What a car target is made of — a bass shelf and a downward tilt — against a
        // cabin with resonances of its own, on the same band budget both ways. The bass
        // shelf is where a shelf earns its slot; a constant-slope tilt on its own is a
        // shape no shelf reproduces, so it is deliberately not what this asks about.
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
    public void Tune_BumpsOnly_SpendsNoSlotOnAShelf()
    {
        // Nothing here is a trend: three resonances on an otherwise flat response. A
        // shelf would have to move everything beside a bump to reach it, and the stage
        // has to see that and stay out.
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
        // Cuts-only promises a profile that never boosts. A shelf keeps that promise
        // only while its knee stays at or below 1/sqrt(2): a sharper one overshoots the
        // shelf gain before settling on it, and on a cut that overshoot IS a boost.
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
        // The mask policy for shelves. A boosting shelf lifts its whole plateau, nulls
        // and all, so it is gated on that plateau being measured rather than on each bin
        // (the per-bin skirt guard would refuse every shelf that could be proposed). The
        // same deficit is shelved when the tail is coherent and refused when it is not.
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

        // Judged on what reaches the top of the range rather than on the band list: the
        // question is whether the deficit up there got lifted, and a shelf is not the
        // only band that could have done it.
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
        // A shelf takes a slot like any other band, and the strips it is written into
        // accept nothing outside the configured gain range. Both ends of the range are
        // sloped here, so both directions are on offer at every budget.
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
        // A corner needs an octave of range on each side before the span beyond its knee
        // means anything. A window narrower than that leaves the stage nothing to stand
        // on — and it must not throw on the way to finding that out.
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
        // The shelf is scored through the same digital response the DSP will run, so the
        // curve fitted for a 96 kHz processor corrects at 96 kHz — it is not the 48 kHz
        // one relabelled. Checked by realizing each at its own rate.
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
