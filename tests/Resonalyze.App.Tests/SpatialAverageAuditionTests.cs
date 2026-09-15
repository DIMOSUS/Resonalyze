using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Driven from the capture side over a flat response, so the expected correction is the capture's own shape.</summary>
public sealed class SpatialAverageAuditionTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void ACaptureThatAgreesWithTheResponse_AsksForNoCorrection()
    {
        SpatialAverageAuditionPlan plan = Build(Channel(Capture(_ => -30)));

        SpatialAverageAuditionCorrection correction = plan.Corrections[0];
        Assert.True(correction.Corrects);
        Assert.All(correction.SubtractDb, point => Assert.Equal(0.0, point.Y, 3));
    }

    [Fact]
    public void TheCorrectionFollowsWhatTheAverageSays()
    {
        SpatialAverageAuditionPlan plan = Build(Channel(Capture(
            frequency => frequency is >= 500 and <= 1_000 ? -24 : -30)));

        SpatialAverageAuditionCorrection correction = plan.Corrections[0];
        Assert.Equal(-6.0, At(correction, 700), 1);
        Assert.Equal(0.0, At(correction, 100), 1);
        Assert.Equal(0.0, At(correction, 5_000), 1);
    }

    [Fact]
    public void ADisagreementBeyondTheLimitIsBounded()
    {
        SpatialAverageAuditionPlan plan = Build(Channel(Capture(
            frequency => frequency is >= 500 and <= 1_000 ? -5 : -30)));

        SpatialAverageAuditionCorrection correction = plan.Corrections[0];
        Assert.Equal(-SpatialAverageAudition.LimitDb, At(correction, 700), 6);
        Assert.Equal(-SpatialAverageAudition.LimitDb, correction.LowestDb, 6);
        Assert.True(correction.LimitedPoints > 0);
    }

    /// <summary>Held at the last known value where the capture stops: a magnitude step makes the kernel ring.</summary>
    [Fact]
    public void AGapInTheCaptureIsBridged_NotStepped()
    {
        LiveCaptureDocument capture = Capture(_ => -24);
        for (int i = 0; i < capture.CurveDb.Length; i++)
        {
            if (capture.FrequencyAt(i) < 200)
            {
                capture.CurveDb[i] = double.NaN;
            }
        }

        SpatialAverageAuditionCorrection correction = Build(Channel(capture)).Corrections[0];

        Assert.All(correction.SubtractDb, point => Assert.True(double.IsFinite(point.Y)));
        Assert.Equal(At(correction, 250), At(correction, 25), 6);
    }

    [Fact]
    public void AChannelWithoutACapture_KeepsItsPointMeasurement()
    {
        SpatialAverageAuditionPlan plan = Build(
            Channel(Capture(_ => -30)), Channel(capture: null));

        Assert.True(plan.Corrections[0].Corrects);
        Assert.False(plan.Corrections[1].Corrects);
        Assert.Equal(1, plan.PointMeasuredCount);
    }

    /// <summary>One offset for the whole set, so relative driver levels survive into the render.</summary>
    [Fact]
    public void OneOffsetLevelsTheSet_SoTheChannelsKeepTheirRelativeLevels()
    {
        SpatialAverageAuditionPlan plan = Build(
            Channel(Capture(_ => -30)), Channel(Capture(_ => -34)));

        Assert.Equal(-2.0, At(plan.Corrections[0], 1_000), 2);
        Assert.Equal(2.0, At(plan.Corrections[1], 1_000), 2);
        Assert.Equal(4.0, plan.SpreadDb, 2);
    }

    /// <summary>The correction states how far the point response sits ABOVE the average, so applying it subtracts.</summary>
    [Fact]
    public void ApplyingACorrection_MovesTheResponseByIt()
    {
        Complex[] response = Delta();
        Complex[] corrected =
            SpatialAverageAudition.Apply(response, Flat(6.0), SampleRate);

        Assert.Equal(-6.0, LevelAt(corrected, 1_000) - LevelAt(response, 1_000), 1);
    }

    /// <summary>The correction delays by half a kernel, so an uncorrected channel must be delayed equally.</summary>
    [Fact]
    public void AnUncorrectedChannelIsDelayedLikeACorrectedOne()
    {
        Complex[] response = Delta();

        Complex[] plain = SpatialAverageAudition.Apply(
            response, SpatialAverageAuditionCorrection.None, SampleRate);
        Complex[] corrected = SpatialAverageAudition.Apply(response, Flat(6.0), SampleRate);

        Assert.Equal(corrected.Length, plain.Length);
        Assert.Equal(PeakIndex(corrected), PeakIndex(plain));
        Assert.Equal(
            LevelAt(response, 1_000), LevelAt(plain, 1_000), 1);
    }

    [Fact]
    public void TheCorrectedResponseReadsAsTheAverage()
    {
        LiveCaptureDocument capture = Capture(
            frequency => frequency is >= 500 and <= 1_000 ? -24 : -30);
        SpatialAverageAuditionPlan plan = Build(Channel(capture));

        Complex[] corrected =
            SpatialAverageAudition.Apply(Delta(), plan.Corrections[0], SampleRate);

        double offset = LevelAt(corrected, 2_000) - CaptureLevel(capture, 2_000);
        foreach (double frequency in new[] { 100.0, 300.0, 700.0, 2_000.0, 8_000.0 })
        {
            Assert.InRange(
                LevelAt(corrected, frequency),
                CaptureLevel(capture, frequency) + offset - 0.4,
                CaptureLevel(capture, frequency) + offset + 0.4);
        }
    }

    private static double CaptureLevel(LiveCaptureDocument capture, double frequency)
    {
        int nearest = 0;
        for (int i = 1; i < capture.CurveDb.Length; i++)
        {
            if (Math.Abs(Math.Log(capture.FrequencyAt(i) / frequency)) <
                Math.Abs(Math.Log(capture.FrequencyAt(nearest) / frequency)))
            {
                nearest = i;
            }
        }

        return capture.CurveDb[nearest];
    }

    /// <summary>Each measurement is read through its own calibration, or array vs measurement-mic capsules tilt the render.</summary>
    [Fact]
    public void EachMeasurementIsReadThroughItsOwnCalibration()
    {
        CalibrationFile microphone = CalibrationFile.FromPoints(
            [new CalibrationPoint(20, 0), new CalibrationPoint(999, 0),
             new CalibrationPoint(1_001, 4), new CalibrationPoint(20_000, 4)]);

        SpatialAverageAuditionCorrection corrected =
            Build(Channel(Capture(_ => -30), microphone)).Corrections[0];
        SpatialAverageAuditionCorrection bare =
            Build(Channel(Capture(_ => -30))).Corrections[0];

        Assert.Equal(-4.0, At(corrected, 4_000) - At(corrected, 200), 1);
        Assert.Equal(0.0, At(bare, 4_000) - At(bare, 200), 1);
    }

    /// <summary>Both sides must use the same band-power estimator: interpolated FFT bins on one 5 ms reflection parted by ~11 dB at 500 Hz.</summary>
    [Fact]
    public void ACaptureBuiltFromTheResponseItself_AsksForNothing()
    {
        Complex[] response = Reflection();
        LiveCaptureDocument capture = CaptureOf(response);

        SpatialAverageAuditionCorrection correction = SpatialAverageAudition.Build(
            [new SpatialAverageAuditionChannel(
                response, SampleRate, MeasuredBand.Everything, null, capture)])
            .Corrections[0];

        Assert.All(
            correction.SubtractDb.Where(point => point.X is >= 30 and <= 15_000),
            point => Assert.InRange(point.Y, -0.05, 0.05));
    }

    private static Complex[] Reflection()
    {
        var response = new Complex[32_768];
        response[64] = Complex.One;
        response[64 + (SampleRate * 5 / 1_000)] = 0.9;
        return response;
    }

    private static LiveCaptureDocument CaptureOf(Complex[] response) => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "capture",
        CurveDb = DataHelper.GetUngatedBandLevels(
            new ImpulseMeasurementView(response, 0, SampleRate)),
        GridStartHz = SpatialAverage.GridStartHz,
        GridStopHz = SpatialAverage.GridStopHz,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };

    private static SpatialAverageAuditionPlan Build(
        params SpatialAverageAuditionChannel[] channels) =>
        SpatialAverageAudition.Build(channels);

    private static SpatialAverageAuditionChannel Channel(
        LiveCaptureDocument? capture, CalibrationFile? calibration = null) =>
        new(Delta(), SampleRate, MeasuredBand.Everything, calibration, capture);

    private static Complex[] Delta()
    {
        var response = new Complex[8_192];
        response[64] = Complex.One;
        return response;
    }

    private static LiveCaptureDocument Capture(Func<double, double> levelAt)
    {
        var document = new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "capture",
            CurveDb = new double[1_024],
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = SampleRate
            }
        };
        for (int i = 0; i < document.CurveDb.Length; i++)
        {
            document.CurveDb[i] = levelAt(document.FrequencyAt(i));
        }

        return document;
    }

    private static SpatialAverageAuditionCorrection Flat(double db)
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 1_024);
        return new SpatialAverageAuditionCorrection(
            [.. grid.Select(frequency => new SignalPoint(frequency, db))], 0.0, db, db, 0);
    }

    private static double At(SpatialAverageAuditionCorrection correction, double frequency)
    {
        SignalPoint nearest = correction.SubtractDb
            .OrderBy(point => Math.Abs(Math.Log(point.X / frequency)))
            .First();
        return nearest.Y;
    }

    private static double LevelAt(Complex[] response, double frequency)
    {
        double[] levels = DataHelper.GetUngatedBandLevels(
            new ImpulseMeasurementView(response, 0, SampleRate));
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        List<SignalPoint> curve = DataHelper.SmoothBandLevels(
            [.. levels.Select((level, i) => new SignalPoint(grid[i], level))],
            SpatialAverageAudition.SmoothingOctaves,
            psychoacoustic: false);
        return curve
            .OrderBy(point => Math.Abs(Math.Log(point.X / frequency)))
            .First()
            .Y;
    }

    private static int PeakIndex(Complex[] response)
    {
        int peak = 0;
        for (int i = 1; i < response.Length; i++)
        {
            if (Math.Abs(response[i].Real) > Math.Abs(response[peak].Real))
            {
                peak = i;
            }
        }

        return peak;
    }
}
