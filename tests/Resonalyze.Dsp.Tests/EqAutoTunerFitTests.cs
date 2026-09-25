namespace Resonalyze.Dsp.Tests;

public sealed class EqAutoTunerFitTests
{
    private const double Rate = 48_000;

    private static IReadOnlyList<SignalPoint> Grid(Func<double, double> valueDb, int count = 400) =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, count)
            .Select(f => new SignalPoint(f, valueDb(f)))
            .ToList();

    private static IReadOnlyList<SignalPoint> Flat => Grid(_ => 0.0);

    private static double Bump(double f, double centreHz, double octaves, double gainDb) =>
        gainDb * Math.Exp(-Math.Pow(Math.Log2(f / centreHz) / octaves, 2));

    // A plateau between two corners with edges this many octaves wide.
    private static double Box(double f, double lowHz, double highHz, double edgeOctaves, double gainDb) =>
        gainDb / (1 + Math.Exp(-Math.Log2(f / lowHz) / edgeOctaves)) / (1 + Math.Exp(Math.Log2(f / highHz) / edgeOctaves));

    private static double Bank(EqualizationCurve curve, double f) =>
        DigitalEqualizationResponse.MagnitudeDbAt(new EqualizationCurve(curve.Bands, 0), f, Rate);

    // Corrected minus target over a window; positive = above.
    private static IEnumerable<double> Deviation(
        EqualizationCurve curve, Func<double, double> source, double lowHz, double highHz) =>
        EqualizationCurve.LogFrequencyGrid(lowHz, highHz, 400)
            .Select(f => source(f) + curve.PreampDb + Bank(curve, f));

    private static EqAutoTuner.Options Options(EqAutoTuneBoosts boosts) => new()
    {
        Boosts = boosts,
        SampleRateHz = Rate,
        QMax = 6,
        TotalGainMaxDb = boosts == EqAutoTuneBoosts.Allowed ? double.PositiveInfinity : 0
    };

    // Narrow work between the fitted bins, with an unmeasured octave: the ceilings are promises about the bank, not the bins.
    private static IReadOnlyList<SignalPoint> Adversarial()
    {
        var points = new List<SignalPoint>();
        foreach (double f in EqualizationCurve.LogFrequencyGrid(20, 20_000, 1_200))
        {
            double value = Bump(f, 971, 0.02, 9) + Bump(f, 1_033, 0.02, -7) + Bump(f, 4_517, 0.015, 8) +
                Bump(f, 4_701, 0.02, -9) + Bump(f, 11_311, 0.03, 7) + Box(f, 40, 90, 0.1, -6);
            points.Add(new SignalPoint(f, f is > 200 and < 400 ? double.NaN : value));
        }

        return points;
    }

    private static double WorstBankPeak(EqualizationCurve curve)
    {
        double worst = double.NegativeInfinity;
        foreach (double f in EqualizationCurve.LogFrequencyGrid(10, Rate * 0.49, 60_000))
        {
            worst = Math.Max(worst, Bank(curve, f));
        }

        return worst;
    }

    [Theory]
    // added, removed, bands, budget
    [InlineData(1, 0, 3, 6, true)]
    [InlineData(0, 1, 3, 6, true)]
    [InlineData(2, 1, 3, 6, true)]
    [InlineData(0, 0, 3, 6, false)]
    [InlineData(1, 1, 6, 6, false)]
    public void APassThatOnlyDroppedABand_EarnsAnotherLook(
        int added, int removed, int bands, int budget, bool again)
    {
        // A dropped band leaves a free slot and a changed residual, which is as good a reason to look again as an
        // insertion. No fixture reaches this: 8_000 fits over the corpus and random sources never took the branch.
        Assert.Equal(again, EqBandFitter.AnotherPass(added, removed, bands, budget));
    }

    [Theory]
    [InlineData(EqAutoTuneBoosts.RefillOwnCuts)]
    [InlineData(EqAutoTuneBoosts.Off)]
    public void Tune_ABankThatMayNotLift_StaysAtOrBelowZero_BetweenTheFittedBinsToo(EqAutoTuneBoosts boosts)
    {
        EqAutoTuner.Options options = Options(boosts) with { QMax = 20 };

        EqualizationCurve curve = EqAutoTuner.Tune(Adversarial(), Flat, options);

        double peak = WorstBankPeak(curve);
        Assert.True(peak <= 1e-6, $"the bank lifts {peak:0.000} dB somewhere.");
        Assert.True(curve.PreampDb <= 0, $"preamp {curve.PreampDb:0.0} dB.");
    }

    [Theory]
    [InlineData(2.25, 2.25)]
    [InlineData(0.55, 0.58)]
    public void Tune_AQRangeHoldingNoTenth_KeepsItsBandsInsideIt(double qMin, double qMax)
    {
        // The strip rounds Q to a tenth, and these ranges hold none.
        EqAutoTuner.Options options = Options(EqAutoTuneBoosts.Allowed) with { QMin = qMin, QMax = qMax };

        EqualizationCurve curve = EqAutoTuner.Tune(Adversarial(), Flat, options);

        Assert.NotEmpty(curve.Bands);
        Assert.All(
            curve.Bands.Where(band => band.Type == PeqBandType.Peaking),
            band => Assert.InRange(band.Q, qMin - 1e-9, qMax + 1e-9));
    }

    [Theory]
    [InlineData(2.27, 2.25, 2.25, 2.25)]
    [InlineData(0.5501, 0.55, 0.58, 0.55)]
    [InlineData(4.04, 0.5, 10, 4.0)]
    public void QuantizeQ_TakesTheFinestStepTheRangeHolds(double q, double low, double high, double expected)
    {
        Assert.Equal(expected, EqBandFitter.QuantizeQ(q, Math.Exp(Math.Log(low)), Math.Exp(Math.Log(high))));
    }

    [Fact]
    public void Tune_TheTotalGainCeiling_HoldsBetweenTheFittedBinsToo()
    {
        EqAutoTuner.Options options = Options(EqAutoTuneBoosts.Allowed) with
        {
            QMax = 20,
            BandGainMaxDb = 12,
            TotalGainMaxDb = 3
        };

        EqualizationCurve curve = EqAutoTuner.Tune(Adversarial(), Flat, options);

        double worst = WorstBankPeak(curve) + curve.PreampDb;
        Assert.True(worst <= 3 + 1e-6, $"the bank reaches {worst:0.000} dB against a 3 dB ceiling.");
    }

    [Fact]
    public void Tune_ADipInsideANoBoostBand_IsLeftAlone()
    {
        // A broad dip the mask trusts: boosts fill it, unless the band says the fall belongs to a filter.
        Func<double, double> source = f => Bump(f, 4_000, 0.8, -6);
        EqAutoTuner.Options options = Options(EqAutoTuneBoosts.Allowed);

        EqualizationCurve free = EqAutoTuner.Tune(Grid(source), Flat, options);
        EqualizationCurve guarded = EqAutoTuner.Tune(
            Grid(source),
            Flat,
            options with { NoBoostBands = [new EqNoBoostBand(3_000, 20_000)] });

        Assert.Contains(free.Bands, band => band.GainDb > 1 && band.FrequencyHz is > 3_000 and < 6_000);
        Assert.DoesNotContain(
            guarded.Bands,
            band => band.GainDb > 0.6 && band.FrequencyHz is > 3_000 and < 6_000);
    }

    [Fact]
    public void Tune_FitsAQBetweenWhatALadderWouldOffer()
    {
        // Q 3.3 sits between the 2.8 and 4.0 steps a fixed ladder had to choose from.
        var peak = new PeqBand(1_000, 3.3, 8);
        Func<double, double> source = f => DigitalEqualizationResponse.MagnitudeDbAt(
            new EqualizationCurve([peak]), f, Rate);

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.Off));

        PeqBand band = Assert.Single(curve.Bands);
        Assert.InRange(band.Q, 3.1, 3.5);
        Assert.InRange(band.FrequencyHz, 980, 1_020);
        double worst = Deviation(curve, source, 500, 2_000).Max(Math.Abs);
        Assert.True(worst < 0.3, $"one matching band left {worst:0.00} dB.");
    }

    [Fact]
    public void Tune_ABroadResonance_TakesOneBandNotAComb()
    {
        Func<double, double> source = f => Bump(f, 300, 0.6, 7);

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.Off));

        PeqBand band = Assert.Single(curve.Bands);
        Assert.True(band.Q < 3, $"a broad resonance was cut with a Q {band.Q:0.0} sliver.");
    }

    [Theory]
    [InlineData(EqAutoTuneBoosts.Off)]
    [InlineData(EqAutoTuneBoosts.RefillOwnCuts)]
    public void Tune_SpendsNoSlotOnRipple(EqAutoTuneBoosts boosts)
    {
        // Ripple of 0.4 dB every third of an octave beside one real resonance; nor may a refill reshape the cut's skirt.
        Func<double, double> source = f =>
            0.4 * Math.Sin(3 * Math.Tau * Math.Log2(f)) + Bump(f, 2_000, 0.3, 6);

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(boosts));

        PeqBand band = Assert.Single(curve.Bands);
        Assert.InRange(band.FrequencyHz, 1_800, 2_200);
        Assert.True(band.GainDb < -4, $"the resonance got {band.GainDb:0.0} dB.");
    }

    [Fact]
    public void Tune_RefillOwnCuts_NeverLiftsTheCurve()
    {
        // A hump with steep flanks: a cut wide enough for its top digs its sides, which only a refill puts back.
        Func<double, double> source = f =>
            Bump(f, 120, 0.45, 12) + Bump(f, 3_000, 0.2, 5) - Bump(f, 900, 0.3, 4);

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.RefillOwnCuts));

        Assert.Contains(curve.Bands, band => band.GainDb > 0);
        double lift = EqualizationCurve.LogFrequencyGrid(20, 20_000, 4_000).Max(f => Bank(curve, f));
        Assert.True(lift <= 1e-6, $"the bank lifts the curve by {lift:0.000000} dB.");
        Assert.True(curve.PreampDb <= 0);
    }

    [Fact]
    public void Tune_RefillOwnCuts_DigsLessThanCutsAloneAndLeavesNoMoreAbove()
    {
        // A plateau with steep edges: bells wide enough for its top dig beside it, narrow ones comb it.
        Func<double, double> source = f => Box(f, 90, 180, 0.08, 12);

        EqualizationCurve cuts = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.Off));
        EqualizationCurve refill = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.RefillOwnCuts));

        double DugBelow(EqualizationCurve curve) => -Deviation(curve, source, 30, 500).Min();
        double LeftAbove(EqualizationCurve curve) => Deviation(curve, source, 30, 500).Max();
        Assert.True(
            DugBelow(refill) < DugBelow(cuts) - 0.3,
            $"refill dug {DugBelow(refill):0.00} dB against {DugBelow(cuts):0.00} for cuts alone.");
        Assert.True(
            LeftAbove(refill) <= LeftAbove(cuts) + 0.1,
            $"refill left {LeftAbove(refill):0.00} dB above against {LeftAbove(cuts):0.00}.");
    }

    [Fact]
    public void Tune_BoostsAllowed_BoostsDoNotStackPastMaxGain()
    {
        // Eight dB short over two octaves: bells stacked on each other could fill it, Max Gain says they may not.
        Func<double, double> source = f => -Bump(f, 500, 1.0, 8);
        EqAutoTuner.Options options = Options(EqAutoTuneBoosts.Allowed) with
        {
            BandGainMaxDb = 4,
            PreampMinDb = 0,
            PreampMaxDb = 0
        };

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, options);

        var boosts = new EqualizationCurve(curve.Bands.Where(band => band.GainDb > 0), 0);
        double stacked = EqualizationCurve.LogFrequencyGrid(20, 20_000, 2_000)
            .Max(f => DigitalEqualizationResponse.MagnitudeDbAt(boosts, f, Rate));
        Assert.True(stacked <= 4 + 1e-6, $"the boosts add up to {stacked:0.00} dB.");
        Assert.True(Bank(curve, 500) > 3.5, $"the deficit got {Bank(curve, 500):0.0} dB of the allowed 4.");
    }

    [Fact]
    public void Tune_BoostsAllowed_NoBoostAndCutWorkAgainstEachOther()
    {
        // A narrow deep null forbids boosts at its core; a cut placed there must not buy a boost room beside it.
        Func<double, double> source = f =>
        {
            double octaves = Math.Abs(Math.Log2(f / 3_000));
            return octaves >= 0.15 ? 0 : -12 * (1 - octaves / 0.15);
        };

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.Allowed));

        foreach (PeqBand boost in curve.Bands.Where(band => band.GainDb > 0.5))
        {
            Assert.DoesNotContain(
                curve.Bands,
                cut => cut.GainDb < -0.5 && Math.Abs(Math.Log2(cut.FrequencyHz / boost.FrequencyHz)) < 1.0 / 3);
        }
    }

    [Fact]
    public void Tune_KeepsEveryValueAtTheStripsPrecision()
    {
        Func<double, double> source = f => Bump(f, 150, 0.3, 9) + Bump(f, 1_700, 0.5, -5) + Bump(f, 7_000, 0.4, 4);

        EqualizationCurve curve = EqAutoTuner.Tune(Grid(source), Flat, Options(EqAutoTuneBoosts.Allowed));

        Assert.NotEmpty(curve.Bands);
        Assert.All(curve.Bands, band =>
        {
            Assert.Equal(Math.Round(band.FrequencyHz), band.FrequencyHz);
            Assert.Equal(Math.Round(band.GainDb, 1), band.GainDb, 9);
            Assert.Equal(Math.Round(band.Q, 1), band.Q, 9);
            Assert.InRange(band.Q, 0.5, 6);
        });
    }

    [Fact]
    public void Tune_IsDeterministic()
    {
        IReadOnlyList<SignalPoint> source = Grid(f => Bump(f, 90, 0.3, 8) + Bump(f, 2_500, 0.6, 3));

        EqualizationCurve first = EqAutoTuner.Tune(source, Flat, Options(EqAutoTuneBoosts.RefillOwnCuts));
        EqualizationCurve second = EqAutoTuner.Tune(source, Flat, Options(EqAutoTuneBoosts.RefillOwnCuts));

        Assert.Equal(first.Bands, second.Bands);
        Assert.Equal(first.PreampDb, second.PreampDb);
    }
}
