using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The hybrid channel curve itself: a stored spatial average with a DSP chain on top,
/// shared by the Virtual DSP plot and the EQ Wizard so a tune is fitted to the curve
/// the panel drew.
/// </summary>
public sealed class SpatialAverageHybridTests
{
    /// <summary>
    /// The filter comes out of the average exactly, which is what makes the whole
    /// substitution legitimate: a spatial average is the root-mean-square of |H| over
    /// the volume, and a filter does not depend on position.
    /// </summary>
    [Fact]
    public void TheChainIsAddedAsItsAnalyticMagnitude()
    {
        LiveCaptureDocument document = Capture(-20);
        var chain = new DspChannelChain { GainDb = -6 };

        List<SignalPoint> curve = Build(document, chain);

        // A pure gain is flat, so every point moves by exactly it.
        Assert.All(curve, point => Assert.Equal(-26, point.Y, 6));
    }

    /// <summary>
    /// Delay and polarity are pure phase, so they cannot touch a magnitude. Worth
    /// pinning: they are the two settings a user changes most while watching this
    /// curve, and a hybrid that twitched under them would be describing something
    /// other than tonal balance.
    /// </summary>
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

    /// <summary>
    /// The capture's own microphone correction is undone and the caller's applied in
    /// its place, so the hybrid and the measured curves beside it carry ONE correction
    /// rather than two. Exact, because these corrections are additive per frequency —
    /// and done on the capture's own grid, before anything is interpolated, so each
    /// value stays on the frequency it was frozen at.
    /// </summary>
    [Fact]
    public void TheCapturesOwnCalibrationIsUndoneBeforeTheCallersIsApplied()
    {
        LiveCaptureDocument document = Capture(-20);
        // Captured through a microphone whose correction was 2 dB everywhere: the
        // pipeline subtracted it, so the stored level reads 2 dB low.
        document.CalibrationCorrectionDb =
            document.CurveDb.Select(_ => 2.0).ToArray();

        // Drawn with no calibration at all: the capture's correction comes back out.
        List<SignalPoint> bare = Build(document, new DspChannelChain());
        Assert.All(bare, point => Assert.Equal(-18, point.Y, 6));

        // Drawn under a different correction of 5 dB: the old one out, the new one in.
        List<SignalPoint> recalibrated = Build(
            document,
            new DspChannelChain(),
            SpatialAverageCalibration.Specific(Calibration(5)));
        Assert.All(recalibrated, point => Assert.Equal(-23, point.Y, 6));
    }

    /// <summary>
    /// Where the capture has nothing to say the curve breaks, and the break is neither
    /// filled by interpolation nor spread by smoothing.
    /// </summary>
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
        // The smoothing did not drag the gap into the band above it.
        Assert.All(
            curve.Where(point => point.X > 400),
            point => Assert.Equal(-20, point.Y, 3));
    }

    /// <summary>
    /// The point immediately before a gap is a measurement and must survive. It did
    /// not: the sampler interpolated towards its NaN successor, and NaN·0 is NaN, so
    /// the last good point came back as a gap too and the break spread one point
    /// backwards. The test above sits far from the edge and never saw it.
    /// </summary>
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

    /// <summary>
    /// Outside the capture's own grid there is no measurement, so there is no curve —
    /// the ends are not clamped into a plausible-looking extension.
    /// </summary>
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
        // A capture stores the grid EqualizationCurve builds — whose first point is
        // 20.000000000000004 — while the curve beside it is drawn on the grid the
        // resampler builds, whose first point is exactly 20. The same grid, two
        // constructions, endpoints a few ULPs apart: the lowest band lands at an
        // index of -3.3e-14. Read as "outside the capture", that silently dropped
        // 20 Hz from every hybrid channel, which on a subwoofer is content.
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

    /// <summary>
    /// The Δ L−R level rule on hybrid curves: an energy mean per side over one
    /// shared grid, differenced once. Two flat curves read exactly their offset,
    /// whichever way it points.
    /// </summary>
    [Fact]
    public void BandLevelDeltaDb_ReadsTheOffsetBetweenFlatCurves()
    {
        List<SignalPoint> left = Flat(-20, count: 97);
        List<SignalPoint> right = Flat(-23, count: 97);

        Assert.Equal(3.0, SpatialAverageHybrid.BandLevelDeltaDb(left, right)!.Value, 9);
        Assert.Equal(-3.0, SpatialAverageHybrid.BandLevelDeltaDb(right, left)!.Value, 9);
    }

    /// <summary>
    /// A gap on EITHER side removes that frequency from both: the comparison must
    /// stay symmetric, not weigh one side's band against a different part of the
    /// other's. Here the halves differ by 12 dB, and a one-sided exclusion would
    /// drag the figure by several dB.
    /// </summary>
    [Fact]
    public void BandLevelDeltaDb_PairsThePointsSoAGapRemovesTheFrequencyFromBothSides()
    {
        // Both sides: −20 dB in the lower half, −8 dB in the upper. The left loses
        // its upper half to a gap; honest pairing leaves two identical −20 dB
        // halves, so the delta is zero.
        List<SignalPoint> left = Flat(-20, count: 96);
        List<SignalPoint> right = Flat(-20, count: 96);
        for (int i = 48; i < 96; i++)
        {
            left[i] = new SignalPoint(left[i].X, double.NaN);
            right[i] = new SignalPoint(right[i].X, -8);
        }

        Assert.Equal(0.0, SpatialAverageHybrid.BandLevelDeltaDb(left, right)!.Value, 9);
    }

    /// <summary>With no point finite on both sides there is nothing to compare.</summary>
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

    /// <summary>
    /// The group rule under the "vs Front" ΔdB: powers add, so two equal members
    /// read 3 dB over one, a member's own gap contributes nothing (its crossover
    /// has removed it from the group's output anyway), and only a point where NO
    /// member has a value is a gap of the group's.
    /// </summary>
    [Fact]
    public void PowerSum_AddsPowersAndPassesOnlyAWholeGap()
    {
        List<SignalPoint> first = Flat(-20, count: 8);
        List<SignalPoint> second = Flat(-20, count: 8);
        second[5] = new SignalPoint(second[5].X, double.NaN);
        first[6] = new SignalPoint(first[6].X, double.NaN);
        second[6] = new SignalPoint(second[6].X, double.NaN);

        List<SignalPoint> sum = SpatialAverageHybrid.PowerSum([first, second]);

        Assert.Equal(8, sum.Count);
        Assert.Equal(-20 + 10 * Math.Log10(2), sum[0].Y, 9);
        // One member gone: the other's level stands alone.
        Assert.Equal(-20, sum[5].Y, 9);
        // Both gone: the group has nothing to say there.
        Assert.True(double.IsNaN(sum[6].Y));
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
