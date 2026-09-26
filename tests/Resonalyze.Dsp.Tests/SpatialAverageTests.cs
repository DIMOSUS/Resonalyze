namespace Resonalyze.Dsp.Tests;

public sealed class SpatialAverageTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static double[] Flat(double db) =>
        Enumerable.Repeat(db, Grid.Count).ToArray();

    private static double[] Curve(Func<double, double> db) =>
        Grid.Select(db).ToArray();

    private static int NearestBand(double frequencyHz)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Grid.Count; i++)
        {
            double distance = Math.Abs(Math.Log2(Grid[i] / frequencyHz));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static double[] Driver(double lowHz, double highHz, double levelDb, double floorDb)
    {
        return Curve(f =>
        {
            if (f >= lowHz && f <= highHz)
            {
                return levelDb;
            }
            double octaves = f < lowHz
                ? Math.Log2(lowHz / f)
                : Math.Log2(f / highHz);
            return Math.Max(floorDb, levelDb - 24.0 * octaves);
        });
    }

    [Fact]
    public void FromLevels_PointsInsideABand_AreAPowerMean()
    {
        double centre = Grid[500];
        double[] levels = SpatialAverage.FromLevels([centre * 0.999, centre * 1.001], [70.0, 80.0]);

        Assert.Equal(10.0 * Math.Log10((1e7 + 1e8) / 2.0), levels[500], 9);
    }

    [Fact]
    public void FromLevels_SparseTable_IsBridgedInLogFrequencyAndNotExtrapolated()
    {
        double[] levels = SpatialAverage.FromLevels([100.0, 400.0], [60.0, 80.0]);
        int band = NearestBand(200.0);

        Assert.Equal(60.0 + 20.0 * Math.Log(Grid[band] / 100.0) / Math.Log(4.0), levels[band], 9);
        Assert.True(double.IsNaN(levels[NearestBand(50.0)]));
        Assert.True(double.IsNaN(levels[NearestBand(1_000.0)]));
    }

    [Fact]
    public void Grid_IsTheOneTheRestOfTheApplicationDrawsOn()
    {
        Assert.Equal(SpatialAverage.GridBandCount, Grid.Count);
        Assert.Equal(SpatialAverage.GridStartHz, Grid[0], 9);
        Assert.Equal(SpatialAverage.GridStopHz, Grid[^1], 9);

        // Evenly spaced in log frequency, so consumers derive octaves per step from the grid.
        double step = Math.Log2(Grid[1] / Grid[0]);
        for (int i = 1; i < Grid.Count; i++)
        {
            Assert.Equal(step, Math.Log2(Grid[i] / Grid[i - 1]), 9);
        }
    }

    [Fact]
    public void Grid_MatchesTheFrequencyResponseCurveGrid()
    {
        // Identical to the response grid: a resample between them would be a silent smoothing.
        IReadOnlyList<double> responseGrid =
            EqualizationCurve.LogFrequencyGrid(20, 20_000, 1_024);

        Assert.Equal(responseGrid.Count, Grid.Count);
        for (int i = 0; i < Grid.Count; i++)
        {
            Assert.Equal(responseGrid[i], Grid[i], 9);
        }
    }

    [Fact]
    public void FromTransferMagnitude_ReadsAFlatResponseAtItsOwnLevel()
    {
        // The band mean must not grow with the number of bins spanned.
        double[] coarse = SpatialAverage.FromTransferMagnitude(
            Enumerable.Repeat(0.5, 4_097).ToArray(), 48_000.0 / 8_192);
        double[] fine = SpatialAverage.FromTransferMagnitude(
            Enumerable.Repeat(0.5, 65_537).ToArray(), 48_000.0 / 131_072);

        int band = NearestBand(1_000);
        Assert.Equal(-6.0206, coarse[band], 3);
        Assert.Equal(-6.0206, fine[band], 3);
    }

    [Fact]
    public void FromTransferMagnitude_AveragesTheBandRatherThanSamplingIt()
    {
        double binWidth = 48_000.0 / 131_072;
        double[] magnitude = Enumerable
            .Range(0, 65_537)
            .Select(bin => bin % 2 == 0 ? 1.0 : 0.5)
            .ToArray();

        double[] levels = SpatialAverage.FromTransferMagnitude(magnitude, binWidth);

        int band = NearestBand(10_000);
        Assert.Equal(10.0 * Math.Log10(0.625), levels[band], 2);
    }

    [Fact]
    public void FromTransferMagnitude_BandsTheSweepNeverReachedAreGaps()
    {
        // Gated bins below the sweep start are 'not measured', not a very low level an equalizer would try to fill.
        double binWidth = 48_000.0 / 65_536;
        var magnitude = new double[32_769];
        for (int bin = 0; bin < magnitude.Length; bin++)
        {
            magnitude[bin] = bin * binWidth >= 100.0 ? 1.0 : 0.0;
        }

        double[] levels = SpatialAverage.FromTransferMagnitude(magnitude, binWidth);

        Assert.True(double.IsNaN(levels[NearestBand(30)]));
        Assert.True(double.IsNaN(levels[NearestBand(80)]));
        Assert.Equal(0.0, levels[NearestBand(200)], 6);
    }

    [Fact]
    public void FromTransferMagnitude_ABandStraddlingTheSweepEdgeReadsItsMeasuredBinsOnly()
    {
        double binWidth = 1.0;
        var magnitude = new double[24_001];
        for (int bin = 0; bin < magnitude.Length; bin++)
        {
            magnitude[bin] = bin >= 10_000 ? 2.0 : 0.0;
        }

        double[] levels = SpatialAverage.FromTransferMagnitude(magnitude, binWidth);

        Assert.Equal(20.0 * Math.Log10(2.0), levels[NearestBand(10_000)], 3);
    }

    [Fact]
    public void RmsAverage_OfIdenticalCurves_IsThatCurve()
    {
        double[] curve = Driver(80, 4_000, 90, 40);

        double[] average = SpatialAverage.RmsAverageDb([curve, curve, curve]);

        for (int band = 0; band < Grid.Count; band++)
        {
            Assert.Equal(curve[band], average[band], 9);
        }
    }

    [Fact]
    public void RmsAverage_IsPowerMean_NotDecibelMean()
    {
        // Power mean 17.04 dB vs 10 dB averaged: a null must not drag the average as hard as a hot position lifts it.
        double[] average = SpatialAverage.RmsAverageDb([Flat(0.0), Flat(20.0)]);

        Assert.Equal(10.0 * Math.Log10(101.0 / 2.0), average[0], 9);
        Assert.True(average[0] > 17.0);
    }

    [Fact]
    public void RmsAverage_SkipsGapsBandByBand()
    {
        double[] complete = Flat(60.0);
        double[] holed = Flat(60.0);
        int hole = NearestBand(1_000);
        holed[hole] = double.NaN;

        double[] average = SpatialAverage.RmsAverageDb([complete, holed]);

        Assert.Equal(60.0, average[hole], 9);
        Assert.Equal(60.0, average[hole - 1], 9);
    }

    [Fact]
    public void RmsAverage_BandNoMicrophoneMeasured_StaysAGap()
    {
        double[] first = Flat(60.0);
        double[] second = Flat(60.0);
        int hole = NearestBand(1_000);
        first[hole] = double.NaN;
        second[hole] = double.NaN;

        double[] average = SpatialAverage.RmsAverageDb([first, second]);

        Assert.True(double.IsNaN(average[hole]));
        Assert.Equal(60.0, average[hole + 1], 9);
    }

    [Fact]
    public void Spread_OfOneMicrophone_IsUnknownRatherThanZero()
    {
        double[] spread = SpatialAverage.SpreadDb([Flat(60.0)]);

        Assert.All(spread, value => Assert.True(double.IsNaN(value)));
    }

    [Fact]
    public void Spread_IsLoudestMinusQuietest()
    {
        int band = NearestBand(200);
        double[] first = Flat(60.0);
        double[] second = Flat(60.0);
        double[] third = Flat(60.0);
        second[band] = 48.0;
        third[band] = 63.0;

        double[] spread = SpatialAverage.SpreadDb([first, second, third]);

        Assert.Equal(15.0, spread[band], 9);
        Assert.Equal(0.0, spread[band - 1], 9);
    }

    [Fact]
    public void Trim_RecoversAPlainSensitivityDifference()
    {
        double[] anchor = Driver(80, 4_000, 90, 40);
        double[] quiet = anchor.Select(db => db - 7.5).ToArray();

        double? trim = SpatialAverage.ResolveTrimDb(quiet, anchor);

        Assert.Equal(7.5, Assert.NotNull(trim), 9);
    }

    [Fact]
    public void Trim_IsMeasuredInTheWorkingBand_NotOverTheNoiseFloor()
    {
        // Below the working band each mic's own noise floor differs; the trim must use the working band only.
        double[] anchor = Driver(2_000, 16_000, 90, 30);
        double[] other = Curve(f => f >= 2_000 && f <= 16_000
            ? anchor[NearestBand(f)] - 2.0
            : 42.0);

        double? trim = SpatialAverage.ResolveTrimDb(other, anchor);

        Assert.Equal(2.0, Assert.NotNull(trim), 6);
    }

    [Fact]
    public void Trim_SurvivesAPositionSittingInANotch()
    {
        // The median ignores an interference notch in one mic.
        double[] anchor = Driver(80, 4_000, 90, 40);
        double[] other = anchor.Select(db => db - 3.0).ToArray();
        int centre = NearestBand(1_000);
        for (int band = centre - 4; band <= centre + 4; band++)
        {
            other[band] -= 25.0;
        }

        double? trim = SpatialAverage.ResolveTrimDb(other, anchor);

        Assert.Equal(3.0, Assert.NotNull(trim), 9);
    }

    [Fact]
    public void Trim_WithNoCommonWorkingBand_IsUnknown()
    {
        double[] anchor = Driver(80, 4_000, 90, 40);
        double[] dead = Flat(double.NaN);

        Assert.Null(SpatialAverage.ResolveTrimDb(dead, anchor));
    }

    [Fact]
    public void Average_PlacesEveryMicrophoneOnTheAnchor()
    {
        double[] anchor = Driver(80, 4_000, 90, 40);
        double[] hot = anchor.Select(db => db + 4.0).ToArray();
        double[] quiet = anchor.Select(db => db - 6.0).ToArray();

        SpatialAverageResult result = SpatialAverage.Average([anchor, hot, quiet], anchorIndex: 0);

        Assert.Equal(0.0, Assert.NotNull(result.TrimsDb[0]), 9);
        Assert.Equal(-4.0, Assert.NotNull(result.TrimsDb[1]), 9);
        Assert.Equal(6.0, Assert.NotNull(result.TrimsDb[2]), 9);

        int band = NearestBand(1_000);
        Assert.Equal(anchor[band], result.AverageDb[band], 9);
        Assert.Equal(0.0, result.SpreadDb[band], 9);
    }

    [Fact]
    public void Average_AnchorKeepsItsOwnLevel()
    {
        // The average stays on the anchor's level, tethered to the impulse response measured beside it.
        double[] anchor = Driver(80, 4_000, 70, 20);
        double[] loud = anchor.Select(db => db + 12.0).ToArray();

        SpatialAverageResult result =
            SpatialAverage.Average([anchor, loud, loud], anchorIndex: 0);

        int band = NearestBand(1_000);
        Assert.Equal(70.0, result.AverageDb[band], 9);
    }

    [Fact]
    public void Average_LeavesOutAMicrophoneItCannotPlace()
    {
        double[] anchor = Driver(80, 4_000, 90, 40);
        double[] dead = Flat(double.NaN);

        SpatialAverageResult result = SpatialAverage.Average([anchor, dead], anchorIndex: 0);

        Assert.Null(result.TrimsDb[1]);
        Assert.Null(result.TrimmedCurvesDb[1]);

        int band = NearestBand(1_000);
        Assert.Equal(anchor[band], result.AverageDb[band], 9);
        Assert.True(double.IsNaN(result.SpreadDb[band]));
    }

    [Fact]
    public void Average_SpreadReportsWhereThePositionsDisagree()
    {
        double[] anchor = Flat(80.0);
        double[] other = Flat(80.0);
        int low = NearestBand(100);
        int high = NearestBand(4_000);
        other[high] = 62.0;

        SpatialAverageResult result = SpatialAverage.Average([anchor, other], anchorIndex: 0);

        Assert.Equal(0.0, result.SpreadDb[low], 6);
        Assert.Equal(18.0, result.SpreadDb[high], 6);
    }

    [Fact]
    public void Average_LinearFilterFactorsOutOfTheAverage()
    {
        // A position-independent filter commutes with the spatial average: the hybrid rests on this.
        double[] first = Driver(80, 4_000, 90, 40);
        double[] second = first.Select((db, i) => db + 15.0 * Math.Sin(i * 0.31)).ToArray();
        double[] chainDb = Curve(f => -9.0 * Math.Min(1.0, Math.Log2(Math.Max(f, 20.0) / 20.0) / 5.0));

        double[] averageThenFilter = SpatialAverage
            .RmsAverageDb([first, second])
            .Select((db, i) => db + chainDb[i])
            .ToArray();
        double[] filterThenAverage = SpatialAverage.RmsAverageDb([
            first.Select((db, i) => db + chainDb[i]).ToArray(),
            second.Select((db, i) => db + chainDb[i]).ToArray()
        ]);

        for (int band = 0; band < Grid.Count; band++)
        {
            Assert.Equal(averageThenFilter[band], filterThenAverage[band], 9);
        }
    }

    [Fact]
    public void Average_RefusesCurvesOnDifferentGrids()
    {
        Assert.Throws<ArgumentException>(() =>
            SpatialAverage.RmsAverageDb([Flat(60.0), new double[Grid.Count - 1]]));
    }

    [Fact]
    public void Average_RefusesAnEmptySet()
    {
        Assert.Throws<ArgumentException>(() => SpatialAverage.RmsAverageDb([]));
    }

    [Fact]
    public void Average_RefusesAnAnchorOutsideTheSet()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpatialAverage.Average([Flat(60.0)], anchorIndex: 1));
    }
}
