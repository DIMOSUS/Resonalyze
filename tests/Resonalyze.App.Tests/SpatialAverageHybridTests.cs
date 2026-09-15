using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class SpatialAverageHybridTests
{
    /// <summary>A spatial average is RMS |H| over the volume and a filter is position-independent, so the chain adds exactly.</summary>
    [Fact]
    public void TheChainIsAddedAsItsAnalyticMagnitude()
    {
        LiveCaptureDocument document = Capture(-20);
        var chain = new DspChannelChain { GainDb = -6 };

        List<SignalPoint> curve = Build(document, chain);

        Assert.All(curve, point => Assert.Equal(-26, point.Y, 6));
    }

    [Fact]
    public void DelayAndPolarityLeaveTheCurveAlone()
    {
        LiveCaptureDocument document = Capture(-20);

        List<SignalPoint> plain = Build(document, new DspChannelChain());
        List<SignalPoint> moved = Build(
            document,
            new DspChannelChain { DelayMs = 3.5, InvertPolarity = true });

        for (int i = 0; i < plain.Count; i++)
        {
            Assert.Equal(plain[i].Y, moved[i].Y, 9);
        }
    }

    /// <summary>The capture's own correction is undone on its own grid before the caller's is applied (corrections are additive per frequency).</summary>
    [Fact]
    public void TheCapturesOwnCalibrationIsUndoneBeforeTheCallersIsApplied()
    {
        LiveCaptureDocument document = Capture(-20);
        document.CalibrationCorrectionDb =
            document.CurveDb.Select(_ => 2.0).ToArray();

        List<SignalPoint> bare = Build(document, new DspChannelChain());
        Assert.All(bare, point => Assert.Equal(-18, point.Y, 6));

        List<SignalPoint> recalibrated = Build(
            document,
            new DspChannelChain(),
            SpatialAverageCalibration.Specific(Calibration(5)));
        Assert.All(recalibrated, point => Assert.Equal(-23, point.Y, 6));
    }

    [Fact]
    public void AGapIsNeitherBridgedNorSpread()
    {
        LiveCaptureDocument document = Capture(-20);
        for (int i = 0; i < document.CurveDb.Length; i++)
        {
            if (document.FrequencyAt(i) < 200)
            {
                document.CurveDb[i] = double.NaN;
            }
        }

        List<SignalPoint> curve = Build(
            document,
            new DspChannelChain(),
            calibration: SpatialAverageCalibration.Off,
            smoothingCode: 6);

        Assert.All(
            curve.Where(point => point.X < 150),
            point => Assert.True(double.IsNaN(point.Y)));
        Assert.All(
            curve.Where(point => point.X > 400),
            point => Assert.Equal(-20, point.Y, 3));
    }

    /// <summary>Interpolating toward a NaN successor gives NaN (NaN*0); it must not swallow the last good point.</summary>
    [Fact]
    public void ThePointBeforeAGapIsNotSwallowedByIt()
    {
        LiveCaptureDocument document = Capture(-20);
        const int LastGood = 500;
        for (int i = LastGood + 1; i < document.CurveDb.Length; i++)
        {
            document.CurveDb[i] = double.NaN;
        }

        List<SignalPoint> curve = Build(
            document,
            new DspChannelChain(),
            SpatialAverageCalibration.Off,
            smoothingCode: 0,
            frequenciesHz:
            [
                document.FrequencyAt(LastGood - 1),
                document.FrequencyAt(LastGood),
                document.FrequencyAt(LastGood + 1)
            ]);

        Assert.Equal(-20, curve[0].Y, 6);
        Assert.Equal(-20, curve[1].Y, 6);
        Assert.True(double.IsNaN(curve[2].Y));
    }

    [Fact]
    public void BeyondTheCapturesGridThereIsNoCurve()
    {
        List<SignalPoint> curve = Build(
            Capture(-20),
            new DspChannelChain(),
            SpatialAverageCalibration.Off,
            smoothingCode: 0,
            frequenciesHz: [5, 10, 100, 1_000, 25_000, 40_000]);

        Assert.True(double.IsNaN(curve[0].Y));
        Assert.True(double.IsNaN(curve[1].Y));
        Assert.Equal(-20, curve[2].Y, 6);
        Assert.Equal(-20, curve[3].Y, 6);
        Assert.True(double.IsNaN(curve[4].Y));
        Assert.True(double.IsNaN(curve[5].Y));
    }

    private static List<SignalPoint> Build(
        LiveCaptureDocument document,
        DspChannelChain chain,
        SpatialAverageCalibration? calibration = null,
        int smoothingCode = 0,
        IReadOnlyList<double>? frequenciesHz = null)
    {
        List<double> grid = frequenciesHz?.ToList()
            ?? Enumerable.Range(0, 256)
                .Select(i => 25 * Math.Pow(10, 2.8 * i / 255))
                .ToList();
        List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            chain,
            48_000,
            calibration ?? SpatialAverageCalibration.Off,
            grid,
            smoothingCode);
        Assert.NotNull(curve);
        return curve;
    }

    private static CalibrationFile Calibration(double db) =>
        CalibrationFile.FromPoints(
            [new CalibrationPoint(10, db), new CalibrationPoint(30_000, db)]);

    [Fact]
    public void TheEndPointsSurviveTheirOwnGridBeingBuiltTwoWays()
    {
        // Capture grid starts at 20.000000000000004, the resampler's at exactly 20: an index of -3.3e-14 must not drop 20 Hz.
        LiveCaptureDocument capture = new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "capture",
            CurveDb = Enumerable.Repeat(-30.0, 1_024).ToArray(),
            GridStartHz = EqualizationCurve.LogFrequencyGrid(20, 20_000, 1_024)[0],
            GridStopHz = EqualizationCurve.LogFrequencyGrid(20, 20_000, 1_024)[^1],
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = 48_000
            }
        };

        List<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            capture,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Off,
            [20.0, 1_000.0, 20_000.0],
            smoothingCode: 0);

        Assert.NotNull(curve);
        Assert.All(curve!, point => Assert.Equal(-30.0, point.Y, 6));
    }

    [Fact]
    public void BandLevelDeltaDb_ReadsTheOffsetBetweenFlatCurves()
    {
        List<SignalPoint> left = Flat(-20, count: 97);
        List<SignalPoint> right = Flat(-23, count: 97);

        Assert.Equal(3.0, SpatialAverageHybrid.BandLevelDeltaDb(left, right)!.Value, 9);
        Assert.Equal(-3.0, SpatialAverageHybrid.BandLevelDeltaDb(right, left)!.Value, 9);
    }

    /// <summary>A gap on either side removes that frequency from both, keeping the comparison symmetric.</summary>
    [Fact]
    public void BandLevelDeltaDb_PairsThePointsSoAGapRemovesTheFrequencyFromBothSides()
    {
        List<SignalPoint> left = Flat(-20, count: 96);
        List<SignalPoint> right = Flat(-20, count: 96);
        for (int i = 48; i < 96; i++)
        {
            left[i] = new SignalPoint(left[i].X, double.NaN);
            right[i] = new SignalPoint(right[i].X, -8);
        }

        Assert.Equal(0.0, SpatialAverageHybrid.BandLevelDeltaDb(left, right)!.Value, 9);
    }

    [Fact]
    public void BandLevelDeltaDb_NullWhenTheCurvesNeverOverlap()
    {
        List<SignalPoint> left = Flat(-20, count: 8);
        List<SignalPoint> right = Flat(-20, count: 8);
        for (int i = 0; i < 8; i++)
        {
            if (i < 4)
            {
                left[i] = new SignalPoint(left[i].X, double.NaN);
            }
            else
            {
                right[i] = new SignalPoint(right[i].X, double.NaN);
            }
        }

        Assert.Null(SpatialAverageHybrid.BandLevelDeltaDb(left, right));
    }

    /// <summary>Powers add; a gap OUTSIDE a member's band is absence (the crossover removed it).</summary>
    [Fact]
    public void PowerSum_AddsPowersAndTreatsAGapOutsideTheMembersBandAsAbsence()
    {
        List<SignalPoint> mid = Flat(-20, count: 8);
        List<SignalPoint> tweeter = Flat(-20, count: 8);
        tweeter[5] = new SignalPoint(tweeter[5].X, double.NaN);

        List<SignalPoint> sum = SpatialAverageHybrid.PowerSum(
            [mid, tweeter],
            [(20, 20_000), (tweeter[6].X, 20_000)]);

        Assert.Equal(8, sum.Count);
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[0].Y, 9);
        Assert.Equal(-20, sum[5].Y, 9);
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[7].Y, 9);
    }

    /// <summary>A gap INSIDE a member's band breaks the group point; summing the rest would quote part of the group as all.</summary>
    [Fact]
    public void PowerSum_BreaksTheGroupWhereACaptureIsSilentInsideItsMembersBand()
    {
        List<SignalPoint> mid = Flat(-20, count: 8);
        List<SignalPoint> tweeter = Flat(-20, count: 8);
        tweeter[5] = new SignalPoint(tweeter[5].X, double.NaN);

        List<SignalPoint> sum = SpatialAverageHybrid.PowerSum(
            [mid, tweeter],
            [(20, 20_000), (20, 20_000)]);

        Assert.True(double.IsNaN(sum[5].Y));
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[4].Y, 9);
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[6].Y, 9);
    }

    /// <summary>A bypassed member plays full range, so its idle crossover corners must not excuse a capture gap.</summary>
    [Fact]
    public void PowerSum_ABypassedMembersIdleCrossoverDoesNotExcuseItsCaptureGap()
    {
        var tweeter = new VirtualCrossoverChannel("Tweeter");
        VirtualCrossoverChannelSettings settings = tweeter.SideSettings(false);
        settings.CrossoverKind = CrossoverKind.BandPass;
        settings.HighPassEdge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
        settings.LowPassEdge = new CrossoverEdge(
            CrossoverFilterFamily.LinkwitzRiley, 8_000, 24);
        tweeter.Pair.Bypass = true;

        (double LowHz, double HighHz) band =
            VirtualCrossoverPanel.HybridGroupMemberBand(tweeter, rightSide: false);

        Assert.Equal((20.0, 20_000.0), band);
        tweeter.Pair.Bypass = false;
        Assert.Equal(
            (2_000.0, 8_000.0),
            VirtualCrossoverPanel.HybridGroupMemberBand(tweeter, rightSide: false));

        List<SignalPoint> mid = Flat(-20, count: 8);
        List<SignalPoint> bypassed = Flat(-20, count: 8);
        bypassed[5] = new SignalPoint(bypassed[5].X, double.NaN);

        List<SignalPoint> sum = SpatialAverageHybrid.PowerSum(
            [mid, bypassed],
            [(20, 20_000), (20.0, 20_000.0)]);

        Assert.True(double.IsNaN(sum[5].Y));
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[4].Y, 9);
    }

    private static List<SignalPoint> Flat(double db, int count) =>
        Enumerable.Range(0, count)
            .Select(i => new SignalPoint(100 * Math.Pow(2, i / 48.0), db))
            .ToList();

    private static LiveCaptureDocument Capture(double db) => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "capture",
        CurveDb = Enumerable.Repeat(db, 1_024).ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = 48_000
        }
    };
}
