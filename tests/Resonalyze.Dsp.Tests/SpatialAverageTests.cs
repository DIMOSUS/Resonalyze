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

    // A driver's passband: flat inside, falling away outside, so a curve has a
    // working band the trim can find and a floor it must ignore.
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
    public void Grid_IsTheOneTheRestOfTheApplicationDrawsOn()
    {
        Assert.Equal(SpatialAverage.GridBandCount, Grid.Count);
        Assert.Equal(SpatialAverage.GridStartHz, Grid[0], 9);
        Assert.Equal(SpatialAverage.GridStopHz, Grid[^1], 9);

        // Evenly spaced in log frequency, which is what lets a consumer derive
        // the octaves per step from the grid itself and re-smooth on it.
        double step = Math.Log2(Grid[1] / Grid[0]);
        for (int i = 1; i < Grid.Count; i++)
        {
            Assert.Equal(step, Math.Log2(Grid[i] / Grid[i - 1]), 9);
        }
    }

    [Fact]
    public void Grid_MatchesTheFrequencyResponseCurveGrid()
    {
        // The array curves are drawn beside frequency responses and handed to
        // the same consumers, so the two grids have to be the same one — not
        // merely similar. A resample between them would be a silent smoothing.
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
        // A flat |H| of 0.5 is -6.02 dB whatever the transform length: the band
        // MEAN must not grow with how many bins the band happens to span.
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
        // Alternating bins 6 dB apart: a band spanning many of them must report
        // their mean power, not whichever bin the grid point landed on.
        double binWidth = 48_000.0 / 131_072;
        double[] magnitude = Enumerable
            .Range(0, 65_537)
            .Select(bin => bin % 2 == 0 ? 1.0 : 0.5)
            .ToArray();

        double[] levels = SpatialAverage.FromTransferMagnitude(magnitude, binWidth);

        // Mean power of 1 and 0.25 is 0.625 => -2.04 dB.
        int band = NearestBand(10_000);
        Assert.Equal(10.0 * Math.Log10(0.625), levels[band], 2);
    }

    [Fact]
    public void FromTransferMagnitude_BandsTheSweepNeverReachedAreGaps()
    {
        // The excitation gate zeroes the bins below the sweep start. Those bands
        // must read as "not measured", never as a very low level: a curve that
        // dives to -200 dB below 30 Hz looks like a measurement of a rolled-off
        // system, and an equalizer would try to fill it.
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
        // Half the band excited, half not: the level is that of the excited half,
        // not half of it. The unexcited bins are absent, not zero-valued.
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
        // 0 dB and 20 dB: the power mean is 10·log10((1 + 100)/2) = 17.04 dB,
        // while averaging the decibels would answer 10. The difference is the
        // whole point — a position sitting in a null must not drag the average
        // down as hard as a hot position pushes it up.
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

        // The hole is filled by the microphone that could measure there — not
        // spread to its neighbours, and not turned into a gap of its own.
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
        // A tweeter: the array agrees over its two working octaves, but the eight
        // octaves below hold each microphone's own noise floor, 12 dB apart. A
        // trim measured over the whole grid would answer with that floor
        // difference; the working band is where the sensitivity actually shows.
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
        // One microphone is 3 dB down overall and additionally 25 dB into an
        // interference notch over a sixth of an octave. The median asks where the
        // two curves agree, so the notch does not move the placement.
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

        // Placed, the three are the same curve, so the average is it and the
        // spread is zero: the sensitivity difference has left the measurement.
        int band = NearestBand(1_000);
        Assert.Equal(anchor[band], result.AverageDb[band], 9);
        Assert.Equal(0.0, result.SpreadDb[band], 9);
    }

    [Fact]
    public void Average_AnchorKeepsItsOwnLevel()
    {
        // Two loud microphones and a quiet anchor: the average must stay on the
        // anchor's level, not drift to the set's mean. This is what keeps the
        // array tethered to the impulse response it was measured beside.
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

        // With the dead microphone out, one remains: the average is the anchor
        // and the spread is unknown rather than zero.
        int band = NearestBand(1_000);
        Assert.Equal(anchor[band], result.AverageDb[band], 9);
        Assert.True(double.IsNaN(result.SpreadDb[band]));
    }

    [Fact]
    public void Average_SpreadReportsWhereThePositionsDisagree()
    {
        // The array agrees in the bass and parts company at 4 kHz, which is what
        // a head-sized array does: a 30 cm spacing is a fraction of a wavelength
        // at 100 Hz and several wavelengths at 4 kHz.
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
        // The property the whole hybrid rests on: a filter that does not depend
        // on position can be applied before or after the spatial average, and the
        // answer is the same. Positions differing by 15 dB, then a 9 dB cut.
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
