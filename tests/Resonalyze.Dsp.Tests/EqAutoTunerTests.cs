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
}
