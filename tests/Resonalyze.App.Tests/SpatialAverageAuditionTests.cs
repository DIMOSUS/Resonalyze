using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The correction that makes an audition render read as the spatial averages instead
/// of as the one microphone position the impulse responses were measured at.
/// </summary>
/// <remarks>
/// Every case here drives the correction from the CAPTURE side, with a flat impulse
/// response behind it: the difference between the two measurements is then exactly the
/// capture's own shape, so what the correction asks for can be stated in advance
/// rather than measured out of the fixture.
/// </remarks>
public sealed class SpatialAverageAuditionTests
{
    private const int SampleRate = 48_000;

    /// <summary>
    /// Two measurements that agree ask for nothing. The level they agree at is
    /// irrelevant — the set's offset is what removes it — and this is the case a
    /// correction must not invent work in.
    /// </summary>
    [Fact]
    public void ACaptureThatAgreesWithTheResponse_AsksForNoCorrection()
    {
        SpatialAverageAuditionPlan plan = Build(Channel(Capture(_ => -30)));

        SpatialAverageAuditionCorrection correction = plan.Corrections[0];
        Assert.True(correction.Corrects);
        Assert.All(correction.SubtractDb, point => Assert.Equal(0.0, point.Y, 3));
    }

    /// <summary>
    /// Where the average says the driver is LOUDER than this position measured, the
    /// correction is negative — it is subtracted, so the render gains there.
    /// </summary>
    [Fact]
    public void TheCorrectionFollowsWhatTheAverageSays()
    {
        // Six decibels of it, over an octave in the middle of the band.
        SpatialAverageAuditionPlan plan = Build(Channel(Capture(
            frequency => frequency is >= 500 and <= 1_000 ? -24 : -30)));

        SpatialAverageAuditionCorrection correction = plan.Corrections[0];
        Assert.Equal(-6.0, At(correction, 700), 1);
        // And nowhere else: the median took the level out, so the flat part is zero.
        Assert.Equal(0.0, At(correction, 100), 1);
        Assert.Equal(0.0, At(correction, 5_000), 1);
    }

    /// <summary>
    /// A disagreement past the limit is bounded rather than obeyed, and the count says
    /// so — twenty-five decibels is not a tonal difference, it is a measurement that
    /// has stopped agreeing, and a render must not be asked to realize it.
    /// </summary>
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

    /// <summary>
    /// Where the capture stops, the correction is held at the last thing it knew
    /// rather than snapped back to zero. A step in a filter's magnitude is a step the
    /// kernel rings at, and holding costs nothing: a response is zeroed where it was
    /// never measured, and any gain over zero is still zero.
    /// </summary>
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

    /// <summary>
    /// A channel with no capture keeps the response the microphone measured, and is
    /// counted as such: the render is then a mixture, and the report has to say so.
    /// </summary>
    [Fact]
    public void AChannelWithoutACapture_KeepsItsPointMeasurement()
    {
        SpatialAverageAuditionPlan plan = Build(
            Channel(Capture(_ => -30)), Channel(capture: null));

        Assert.True(plan.Corrections[0].Corrects);
        Assert.False(plan.Corrections[1].Corrects);
        Assert.Equal(1, plan.PointMeasuredCount);
    }

    /// <summary>
    /// ONE offset for the whole set, so what survives into the render is how far the
    /// channels differ from EACH OTHER — which is the balance between the drivers, and
    /// the thing a spatial average has to say that a point measurement does not.
    /// </summary>
    [Fact]
    public void OneOffsetLevelsTheSet_SoTheChannelsKeepTheirRelativeLevels()
    {
        // Two captures of the same (flat) response four decibels apart.
        SpatialAverageAuditionPlan plan = Build(
            Channel(Capture(_ => -30)), Channel(Capture(_ => -34)));

        Assert.Equal(-2.0, At(plan.Corrections[0], 1_000), 2);
        Assert.Equal(2.0, At(plan.Corrections[1], 1_000), 2);
        Assert.Equal(4.0, plan.SpreadDb, 2);
    }

    /// <summary>
    /// The filter realizes the curve it was given, in the direction the curve is
    /// stated in: the correction says how far the point response sits ABOVE the
    /// average, so applying it takes that much off.
    /// </summary>
    [Fact]
    public void ApplyingACorrection_MovesTheResponseByIt()
    {
        Complex[] response = Delta();
        Complex[] corrected =
            SpatialAverageAudition.Apply(response, Flat(6.0), SampleRate);

        Assert.Equal(-6.0, LevelAt(corrected, 1_000) - LevelAt(response, 1_000), 1);
    }

    /// <summary>
    /// A channel with nothing to correct is filtered anyway, by a flat design that is
    /// exactly a delay. It has to be: the correction's delay is half a kernel, and a
    /// channel that skipped it would arrive that much early against the rest — which
    /// is the one thing an audition exists to judge.
    /// </summary>
    [Fact]
    public void AnUncorrectedChannelIsDelayedLikeACorrectedOne()
    {
        Complex[] response = Delta();

        Complex[] plain = SpatialAverageAudition.Apply(
            response, SpatialAverageAuditionCorrection.None, SampleRate);
        Complex[] corrected = SpatialAverageAudition.Apply(response, Flat(6.0), SampleRate);

        Assert.Equal(corrected.Length, plain.Length);
        Assert.Equal(PeakIndex(corrected), PeakIndex(plain));
        // And it is a delay and nothing else: the response comes back at its own level.
        Assert.Equal(
            LevelAt(response, 1_000), LevelAt(plain, 1_000), 1);
    }

    /// <summary>
    /// The whole point, end to end: what the render MEASURES after the correction is
    /// what the capture said, to a fraction of a decibel, instead of what the one
    /// microphone position did.
    /// </summary>
    [Fact]
    public void TheCorrectedResponseReadsAsTheAverage()
    {
        LiveCaptureDocument capture = Capture(
            frequency => frequency is >= 500 and <= 1_000 ? -24 : -30);
        SpatialAverageAuditionPlan plan = Build(Channel(capture));

        Complex[] corrected =
            SpatialAverageAudition.Apply(Delta(), plan.Corrections[0], SampleRate);

        // The two families sit at their own levels; one offset is what the set's datum
        // removed, and everything past it has to agree.
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

    private static SpatialAverageAuditionPlan Build(
        params SpatialAverageAuditionChannel[] channels) =>
        SpatialAverageAudition.Build(channels);

    private static SpatialAverageAuditionChannel Channel(LiveCaptureDocument? capture) =>
        new(Delta(), SampleRate, MeasuredBand.Everything, capture);

    // A unit impulse: its magnitude is flat at every frequency, so whatever the
    // correction asks for came from the capture beside it.
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
        List<SignalPoint> curve = DataHelper.GetUngatedMagnitude(
            new ImpulseMeasurementView(response, 0, SampleRate),
            SpatialAverageAudition.SmoothingOctaves);
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
