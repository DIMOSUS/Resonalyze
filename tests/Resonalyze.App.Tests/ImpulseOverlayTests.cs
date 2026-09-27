using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ImpulseOverlayTests
{
    private const int SampleRate = 48_000;

    private static ImpulseOverlayCapture Capture(
        AnalysisCurveKind kind = AnalysisCurveKind.Primary,
        double peakReference = 1.0,
        int sampleRateHz = SampleRate) =>
        new(
            [
                new SignalPoint(0, 0.0),
                new SignalPoint(480, 0.5),
                new SignalPoint(960, -0.25)
            ],
            kind,
            peakReference,
            sampleRateHz);

    private static ImpulseOverlayFrame Frame(
        Action<ImpulseResponseOptions>? configure = null,
        double origin = 0.0,
        double? reference = 1.0,
        int sampleRate = SampleRate)
    {
        var options = new ImpulseResponseOptions
        {
            TimeUnit = ImpulseTimeUnit.Samples,
            AmplitudeScale = ImpulseAmplitudeScale.Linear
        };
        configure?.Invoke(options);
        return new ImpulseOverlayFrame(options, origin, reference, sampleRate);
    }

    [Fact]
    public void Render_InSamplesKeepsTheRecordsOwnIndices()
    {
        DataPoint[] points = ImpulseOverlayRenderer.Render(Capture(), Frame());

        Assert.Equal(480.0, points[1].X, precision: 9);
        Assert.Equal(0.5, points[1].Y, precision: 9);
    }

    [Fact]
    public void Render_FollowsTheViewsTimeUnit()
    {
        DataPoint[] points = ImpulseOverlayRenderer.Render(
            Capture(),
            Frame(o => o.TimeUnit = ImpulseTimeUnit.Milliseconds));

        Assert.Equal(10.0, points[1].X, precision: 9);
    }

    [Fact]
    public void Render_FollowsTheViewsTimeOrigin()
    {
        DataPoint[] points = ImpulseOverlayRenderer.Render(
            Capture(),
            Frame(o => o.TimeUnit = ImpulseTimeUnit.Milliseconds, origin: 480));

        Assert.Equal(-10.0, points[0].X, precision: 9);
        Assert.Equal(0.0, points[1].X, precision: 9);
        Assert.Equal(10.0, points[2].X, precision: 9);
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear, 0.5)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak, 50.0)]
    [InlineData(ImpulseAmplitudeScale.Decibels, -6.0206)]
    public void Render_FollowsTheViewsAmplitudeScale(
        ImpulseAmplitudeScale scale, double expected)
    {
        DataPoint[] points = ImpulseOverlayRenderer.Render(
            Capture(), Frame(o => o.AmplitudeScale = scale));

        Assert.Equal(expected, points[1].Y, precision: 3);
    }

    [Fact]
    public void Render_NormalizesAgainstTheLiveRecordNotItsOwnPeak()
    {
        // Re-normalizing a snapshot to its own peak would erase the level difference against the live curve.
        DataPoint[] points = ImpulseOverlayRenderer.Render(
            Capture(peakReference: 0.5),
            Frame(o => o.AmplitudeScale = ImpulseAmplitudeScale.Decibels, reference: 1.0));

        Assert.Equal(-6.0206, points[1].Y, precision: 3);
    }

    [Fact]
    public void Render_FallsBackToItsOwnPeakWhenNothingIsMeasured()
    {
        DataPoint[] points = ImpulseOverlayRenderer.Render(
            Capture(peakReference: 0.5),
            Frame(o => o.AmplitudeScale = ImpulseAmplitudeScale.Decibels, reference: null));

        Assert.Equal(0.0, points[1].Y, precision: 3);
    }

    [Fact]
    public void Render_AppliesThePolarityFlipToAnImpulseButNotToAnEnvelope()
    {
        DataPoint[] impulse = ImpulseOverlayRenderer.Render(
            Capture(), Frame(o => o.Invert = true));
        DataPoint[] envelope = ImpulseOverlayRenderer.Render(
            Capture(AnalysisCurveKind.ImpulseEnvelope), Frame(o => o.Invert = true));

        Assert.Equal(-0.5, impulse[1].Y, precision: 9);
        Assert.Equal(0.5, envelope[1].Y, precision: 9);
    }

    [Fact]
    public void Render_NormalizesAStoredStepWithTheViewsCurrentChoice()
    {
        // A step is stored as the raw integral so the "against IR peak" toggle still works on a snapshot.
        ImpulseOverlayCapture capture = Capture(AnalysisCurveKind.ImpulseStep);

        DataPoint[] againstPeak = ImpulseOverlayRenderer.Render(
            capture,
            Frame(o => o.NormalizeStepToImpulsePeak = true, reference: 4.0));
        DataPoint[] againstItself = ImpulseOverlayRenderer.Render(
            capture,
            Frame(o => o.NormalizeStepToImpulsePeak = false, reference: 4.0));

        Assert.Equal(0.125, againstPeak[1].Y, precision: 9);
        Assert.Equal(1.0, againstItself[1].Y, precision: 9);
    }

    [Fact]
    public void Render_TheAmplitudeScaleDoesNotTouchAStep()
    {
        ImpulseOverlayCapture capture = Capture(AnalysisCurveKind.ImpulseStep);

        DataPoint[] linear = ImpulseOverlayRenderer.Render(
            capture, Frame(o => o.AmplitudeScale = ImpulseAmplitudeScale.Linear));
        DataPoint[] decibels = ImpulseOverlayRenderer.Render(
            capture, Frame(o => o.AmplitudeScale = ImpulseAmplitudeScale.Decibels));

        Assert.Equal(linear[1].Y, decibels[1].Y, precision: 12);
    }

    [Fact]
    public void Render_RestatesAnotherClocksSamplesOnTheSampleAxis()
    {
        // 441 at 44.1 kHz and 480 at 48 kHz are the same instant.
        var capture = new ImpulseOverlayCapture(
            [new SignalPoint(0, 0.0), new SignalPoint(441, 1.0)],
            AnalysisCurveKind.Primary,
            1.0,
            44_100);

        DataPoint[] points = ImpulseOverlayRenderer.Render(
            capture,
            Frame(o => o.TimeUnit = ImpulseTimeUnit.Samples, origin: 480, sampleRate: 48_000));

        Assert.Equal(-480.0, points[0].X, precision: 9);
        Assert.Equal(0.0, points[1].X, precision: 9);
    }

    [Fact]
    public void Render_PlacesASnapshotFromAnotherClockAtTheRightInstant()
    {
        var capture = new ImpulseOverlayCapture(
            [new SignalPoint(0, 0.0), new SignalPoint(441, 1.0)],
            AnalysisCurveKind.Primary,
            1.0,
            44_100);

        DataPoint[] points = ImpulseOverlayRenderer.Render(
            capture,
            Frame(
                o => o.TimeUnit = ImpulseTimeUnit.Milliseconds,
                origin: 480,
                sampleRate: 48_000));

        Assert.Equal(-10.0, points[0].X, precision: 9);
        Assert.Equal(0.0, points[1].X, precision: 9);
    }

    [Fact]
    public void Thinning_PassesAShortTraceThrough()
    {
        var points = new List<SignalPoint>();
        for (int i = 0; i < 1_000; i++)
        {
            points.Add(new SignalPoint(i, i));
        }

        Assert.Same(points, ImpulseOverlayThinning.Thin(points, peakX: 0));
    }

    [Fact]
    public void Thinning_KeepsTheExtremesAndTheirOwnSampleIndices()
    {
        int count = ImpulseOverlayThinning.MaximumPoints * 4;
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(i, 0.0));
        }

        points[12_345] = new SignalPoint(12_345, 7.0);
        points[12_346] = new SignalPoint(12_346, -3.0);

        IReadOnlyList<SignalPoint> thinned = ImpulseOverlayThinning.Thin(points, peakX: 150_000);

        Assert.True(thinned.Count <= ImpulseOverlayThinning.MaximumPoints);
        Assert.Contains(thinned, point => point.X == 12_345 && point.Y == 7.0);
        Assert.Contains(thinned, point => point.X == 12_346 && point.Y == -3.0);
        for (int i = 1; i < thinned.Count; i++)
        {
            Assert.True(thinned[i].X >= thinned[i - 1].X);
        }
    }

    [Theory]
    [InlineData(10_000.0, 1_808.0)]      // the detail window straddles the peak, a quarter of it before
    [InlineData(-95_000.0, -98_304.0)]   // a peak near the record's start: the window starts there
    [InlineData(98_000.0, 65_536.0)]     // and near its end, the window ends there
    public void Thinning_KeepsEverySampleInAWindowAroundThePeak(double peakX, double expectedFirstX)
    {
        // A signed record of 196 608 samples, -98 304 .. 98 303.
        int count = ImpulseOverlayThinning.MaximumPoints * 4;
        int lead = count / 2;
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(i - lead, Math.Sin(i * 0.3)));
        }

        IReadOnlyList<SignalPoint> thinned = ImpulseOverlayThinning.Thin(points, peakX);

        Assert.True(thinned.Count <= ImpulseOverlayThinning.MaximumPoints);
        int first = thinned.ToList().FindIndex(point => point.X == expectedFirstX);
        Assert.True(first >= 0, $"the window does not start at {expectedFirstX}");
        for (int k = 0; k < ImpulseOverlayThinning.DetailSamples; k++)
        {
            Assert.Equal(points[(int)expectedFirstX + lead + k], thinned[first + k]);
        }
        Assert.InRange(peakX, thinned[first].X, thinned[first + ImpulseOverlayThinning.DetailSamples - 1].X);
        for (int i = 1; i < thinned.Count; i++)
        {
            Assert.True(thinned[i].X > thinned[i - 1].X);
        }
    }

    [Fact]
    public void BuildImpulseCapture_StoresTheRecordsOwnCoordinatesWhateverTheViewShows()
    {
        var ir = new Complex[4096];
        int peak = 512;
        ir[peak] = new Complex(0.5, 0.0);

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: SampleRate, bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak));

        var options = new ImpulseResponseOptions
        {
            AmplitudeScale = ImpulseAmplitudeScale.Decibels,
            TimeUnit = ImpulseTimeUnit.Milliseconds,
            TimeOrigin = ImpulseTimeOrigin.Peak,
            Invert = true
        };
        PlotModelFactory factory =
            CreateFactoryFor(measurement, options);

        ImpulseOverlayCapture capture = Assert.NotNull(
            factory.BuildImpulseCapture(
                new CurveTag(Mode.ImpulseResponse, AnalysisCurveKind.Primary)));

        Assert.Equal(AnalysisCurveKind.Primary, capture.Kind);
        Assert.Equal(SampleRate, capture.SampleRateHz);
        Assert.Equal(0.5, capture.PeakReference, precision: 9);
        SignalPoint stored = capture.Samples.Single(point => point.X == peak);
        Assert.Equal(0.5, stored.Y, precision: 9);
    }

    [Fact]
    public void BuildImpulseCapture_KeepsEverySampleAroundThePeakOfALongRecord()
    {
        var ir = new Complex[131_072];
        int peak = 40_000;
        for (int k = -200; k <= 200; k++)
        {
            ir[peak + k] = new Complex(Math.Exp(-Math.Abs(k) / 50.0) * Math.Cos(k * 0.7), 0.0);
        }

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: SampleRate, bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak));
        PlotModelFactory factory = CreateFactoryFor(measurement, new ImpulseResponseOptions());

        ImpulseOverlayCapture capture = Assert.NotNull(
            factory.BuildImpulseCapture(
                new CurveTag(Mode.ImpulseResponse, AnalysisCurveKind.Primary)));

        Assert.True(capture.Samples.Count <= ImpulseOverlayThinning.MaximumPoints);
        var stored = capture.Samples.ToDictionary(point => point.X, point => point.Y);
        for (int k = -200; k <= 200; k++)
        {
            Assert.Equal(ir[peak + k].Real, stored[peak + k], precision: 12);
        }
    }

    [Fact]
    public void BuildImpulseCapture_RefusesACurveThatIsNotAnImpulseTrace()
    {
        using var measurement = new TestAnalyzer();
        PlotModelFactory factory = CreateFactoryFor(
            measurement, new ImpulseResponseOptions());

        Assert.Null(factory.BuildImpulseCapture(
            new CurveTag(Mode.FrequencyResponse, AnalysisCurveKind.Primary)));
    }

    private static PlotModelFactory CreateFactoryFor(
        TestAnalyzer measurement,
        ImpulseResponseOptions impulseOptions) =>
        new(
            measurement.Document,
            measurement.Engine,
            _ => null,
            new AnalyzerViewSettings
            {
                PhaseResponse = new FrequencyResponseOptions(),
                GroupDelay = new FrequencyResponseOptions(),
                ImpulseResponse = impulseOptions,
                Waterfall = new WaterfallGenerateOptions(),
                BurstDecay = new WaterfallGenerateOptions()
            });
}
