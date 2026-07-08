using System.Numerics;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class PlotModelFactoryTests
{
    [Fact]
    public void MeasurementPlotTitles_IncludeImpulseResponseFileName()
    {
        using var measurement = new ExpSweepMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse:
            [
                Complex.Zero,
                Complex.One,
                Complex.Zero
            ],
            sweepDeconvolutionPeakIndex: 1);

        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);
        factory.SetImpulseResponseFileName(@"C:\Temp\My Measurement.json");

        Assert.Equal(
            "Impulse Response - My Measurement.json",
            factory.CreateImpulseResponse(includeCurves: false).Title);
        Assert.Equal(
            "Frequency Response - My Measurement.json",
            factory.CreateFrequencyResponse(includeCurves: false).Title);
        Assert.Equal(
            "Phase Response - My Measurement.json",
            factory.CreatePhaseResponse(includeCurves: false).Title);
        Assert.Equal(
            "Group Delay - My Measurement.json",
            factory.CreateGroupDelay(includeCurves: false).Title);
        Assert.Equal(
            "Fourier Waterfall - My Measurement.json",
            factory.CreateWaterfall(includeCurves: false).Title);
        Assert.Equal(
            "Burst Decay - My Measurement.json",
            factory.CreateBurstDecay(includeCurves: false).Title);
        Assert.Equal(
            "Autocorrelation - My Measurement.json",
            factory.CreateAutocorrelation(includeCurves: false).Title);
    }

    [Fact]
    public void MeasurementPlotTitles_FallBackToBaseTitlesWithoutFileName()
    {
        using var measurement = new ExpSweepMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse:
            [
                Complex.Zero,
                Complex.One,
                Complex.Zero
            ],
            sweepDeconvolutionPeakIndex: 1);

        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);
        factory.SetImpulseResponseFileName(null);

        Assert.Equal(
            "Frequency Response",
            factory.CreateFrequencyResponse(includeCurves: false).Title);
    }

    [Fact]
    public void ImpulseResponse_RespectsShowImpulseFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowImpulse = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateImpulseResponse(includeCurves: true).Series);

        impulseOptions.ShowImpulse = true;
        Assert.NotEmpty(factory.CreateImpulseResponse(includeCurves: true).Series);
    }

    [Fact]
    public void Autocorrelation_RespectsShowAutocorrelationFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowAutocorrelation = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateAutocorrelation(includeCurves: true).Series);

        impulseOptions.ShowAutocorrelation = true;
        Assert.NotEmpty(factory.CreateAutocorrelation(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_RespectsShowGroupDelayFlag()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var groupDelayVisibility = new CurveVisibilityOptions { ShowGroupDelay = false };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, groupDelayVisibility: groupDelayVisibility);

        Assert.Empty(factory.CreateGroupDelay(includeCurves: true).Series);

        groupDelayVisibility.ShowGroupDelay = true;
        Assert.NotEmpty(factory.CreateGroupDelay(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_TagsMainAndCompareCurvesForLinkedOverlays()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        var groupDelayVisibility = new CurveVisibilityOptions { ShowGroupDelay = true };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, groupDelayVisibility: groupDelayVisibility);

        List<CurveTag> mainTags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(
            mainTags,
            tag => tag.Mode == Mode.GroupDelay &&
                tag.Kind == AnalysisCurveKind.Primary &&
                tag.Source == CurveSource.Main);
        Assert.DoesNotContain(mainTags, tag => tag.Source == CurveSource.Compare);

        // A Compare source at the same sample rate adds a second, Compare-tagged curve
        // that a linked overlay slot can bind to.
        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64,
                compareIr,
                64));

        List<CurveTag> comparedTags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(comparedTags, tag => tag.Source == CurveSource.Main);
        Assert.Contains(
            comparedTags,
            tag => tag.Source == CurveSource.Compare &&
                tag.Key == "GroupDelay:Primary:Compare");
    }

    [Fact]
    public void FrequencyResponse_ShowPrimaryFlag_GatesThePrimaryCurve()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();

        var hidden = new CurveVisibilityOptions { ShowPrimary = false };
        Assert.DoesNotContain(
            "Frequency Response",
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, frequencyResponseVisibility: hidden)
                .CreateFrequencyResponse(includeCurves: true)));

        var shown = new CurveVisibilityOptions { ShowPrimary = true };
        Assert.Contains(
            "Frequency Response",
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, frequencyResponseVisibility: shown)
                .CreateFrequencyResponse(includeCurves: true)));
    }

    [Theory]
    [InlineData(nameof(CurveVisibilityOptions.ShowMeasuredPhase), "Phase")]
    [InlineData(nameof(CurveVisibilityOptions.ShowMinimumPhase), "Minimum Phase")]
    [InlineData(nameof(CurveVisibilityOptions.ShowExcessPhase), "Excess Phase")]
    public void PhaseResponse_VisibilityFlagGatesItsCurve(string flag, string title)
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();

        var hidden = PhaseAllOff();
        Assert.DoesNotContain(
            title,
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, phaseResponseVisibility: hidden)
                .CreatePhaseResponse(includeCurves: true)));

        var shown = PhaseAllOff();
        typeof(CurveVisibilityOptions).GetProperty(flag)!.SetValue(shown, true);
        Assert.Contains(
            title,
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, phaseResponseVisibility: shown)
                .CreatePhaseResponse(includeCurves: true)));
    }

    [Fact]
    public void PhaseResponse_ShowCoherenceFlag_GatesTheCoherenceCurve()
    {
        using var measurement = CreateTransferMeasurementWithCoherence();
        using var noiseMeasurement = new NoiseMeasurement();

        var hidden = PhaseAllOff();
        hidden.ShowCoherence = false;
        Assert.DoesNotContain(
            "Coherence",
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, phaseResponseVisibility: hidden)
                .CreatePhaseResponse(includeCurves: true)));

        var shown = PhaseAllOff();
        shown.ShowCoherence = true;
        Assert.Contains(
            "Coherence",
            SeriesTitles(CreateFactory(
                    measurement, noiseMeasurement, phaseResponseVisibility: shown)
                .CreatePhaseResponse(includeCurves: true)));
    }

    private static CurveVisibilityOptions PhaseAllOff() => new()
    {
        ShowMeasuredPhase = false,
        ShowMinimumPhase = false,
        ShowExcessPhase = false,
        ShowCoherence = false
    };

    private static IReadOnlyList<string> SeriesTitles(OxyPlot.PlotModel model) =>
        model.Series.OfType<LineSeries>().Select(series => series.Title).ToList();

    [Fact]
    public void ComplexSum_OfTwoIdenticalTransferResponses_AddsSixDecibels()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // Without a Compare measurement the complex sum has nothing to add.
        Assert.Null(factory.TryBuildComplexSumCurve());

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64,
                compareIr,
                64));

        AnalysisCurve? sum = factory.TryBuildComplexSumCurve();
        Assert.NotNull(sum);

        // The Compare IR equals the main transfer IR, so the coherent (complex)
        // sum is exactly double the amplitude everywhere: +20·log10(2) dB.
        AnalysisCurve main = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                measurement.TransferImpulseResponse!,
                measurement.TransferPeakIndex,
                measurement.SampleRate),
            new FrequencyResponseOptions(),
            calibration: null);
        double expectedDelta = 20.0 * Math.Log10(2.0);
        Assert.Equal(main.Points.Count, sum.Points.Count);
        for (int i = 0; i < main.Points.Count; i++)
        {
            Assert.Equal(main.Points[i].X, sum.Points[i].X, precision: 9);
            Assert.Equal(main.Points[i].Y + expectedDelta, sum.Points[i].Y, precision: 6);
        }
    }

    [Fact]
    public void ComplexSum_CompareDelayRealignsAnEarlierArrival()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // The Compare impulse arrives 10 samples early; delaying it by exactly
        // 10 samples' worth of milliseconds re-aligns it with the main impulse,
        // restoring the fully coherent +6 dB sum.
        var compareIr = new Complex[2048];
        compareIr[54] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 54, compareIr, 54));

        double delayMs = 10.0 / 44_100.0 * 1_000.0;
        AnalysisCurve? aligned = factory.TryBuildComplexSumCurve(delayMs);
        Assert.NotNull(aligned);

        AnalysisCurve main = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                measurement.TransferImpulseResponse!,
                measurement.TransferPeakIndex,
                measurement.SampleRate),
            new FrequencyResponseOptions(),
            calibration: null);
        double expectedDelta = 20.0 * Math.Log10(2.0);
        for (int i = 0; i < main.Points.Count; i++)
        {
            Assert.Equal(
                main.Points[i].Y + expectedDelta,
                aligned.Points[i].Y,
                precision: 5);
        }
    }

    [Fact]
    public void ComplexSum_InvertedComparePolarityCancelsAnIdenticalResponse()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64, compareIr, 64));

        // An identical response in opposite polarity sums to silence everywhere.
        AnalysisCurve? cancelled = factory.TryBuildComplexSumCurve(
            compareDelayMs: 0,
            invertComparePolarity: true);
        Assert.NotNull(cancelled);
        Assert.All(cancelled.Points, point => Assert.True(point.Y < -100.0));
    }

    [Fact]
    public void ComplexSum_RequiresMatchingSampleRateAndTransferIr()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;

        // A Compare at a different sample rate cannot be summed sample-wise.
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 48_000, compareIr, 64, compareIr, 64));
        Assert.Null(factory.TryBuildComplexSumCurve());

        // A Compare without a transfer IR has no loopback time reference.
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, Array.Empty<Complex>(), 0, compareIr, 64));
        Assert.Null(factory.TryBuildComplexSumCurve());
    }

    [Fact]
    public void ComplexSumLoss_IsZero_WhenSourcesSumCoherently()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // Without a Compare measurement there is nothing to compare against.
        Assert.Null(factory.TryBuildComplexSumLossCurve());

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64, compareIr, 64));

        // Identical, in-phase responses: the magnitude sum and the complex sum are both
        // exactly double the amplitude, so the phase-blind addition loses nothing.
        AnalysisCurve? loss = factory.TryBuildComplexSumLossCurve();
        Assert.NotNull(loss);
        Assert.All(loss.Points, point => Assert.Equal(0.0, point.Y, precision: 4));
    }

    [Fact]
    public void ComplexSumLoss_IsLarge_WhenSourcesCancel()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64, compareIr, 64));

        // Opposite polarity: the complex sum cancels to near silence while the magnitude
        // sum stays at full level, so the real sum falls far below it (a large negative gap).
        AnalysisCurve? loss = factory.TryBuildComplexSumLossCurve(
            compareDelayMs: 0,
            invertComparePolarity: true);
        Assert.NotNull(loss);
        Assert.All(loss.Points, point => Assert.True(point.Y < -40.0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpulseResponse_LocksValueAxisToCurveRange(bool logarithmic)
    {
        var ir = new Complex[8192];
        int peak = 1024;
        for (int i = 0; i < 2000 && peak + i < ir.Length; i++)
        {
            ir[peak + i] = new Complex(Math.Exp(-i / 200.0) * Math.Cos(i * 0.3), 0);
        }

        using var measurement = new ExpSweepMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        // The impulse view is now derived from the mandatory loopback transfer IR.
        measurement.RestoreImpulseResponse(
            octaves: 12, sampleRate: 44_100, bits: 24, sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak);

        var options = new ImpulseResponseOptions { Logarithmic = logarithmic, ShowImpulse = true };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: options);

        var model = factory.CreateImpulseResponse(includeCurves: true);
        var series = (OxyPlot.Series.LineSeries)model.Series[0];
        var valueAxis = model.Axes.First(axis =>
            axis.Position == OxyPlot.Axes.AxisPosition.Left);
        var timeAxis = model.Axes.First(axis =>
            axis.Position == OxyPlot.Axes.AxisPosition.Bottom);

        double expectedMinY = series.Points.Min(point => point.Y);
        double expectedMaxY = series.Points.Max(point => point.Y);
        Assert.Equal(expectedMinY, valueAxis.Minimum, precision: 9);
        Assert.Equal(expectedMaxY, valueAxis.Maximum, precision: 9);
        Assert.Equal(expectedMinY, valueAxis.AbsoluteMinimum, precision: 9);
        Assert.Equal(expectedMaxY, valueAxis.AbsoluteMaximum, precision: 9);

        double expectedMinX = series.Points.Min(point => point.X);
        double expectedMaxX = series.Points.Max(point => point.X);
        Assert.Equal(expectedMinX, timeAxis.Minimum, precision: 9);
        Assert.Equal(expectedMaxX, timeAxis.Maximum, precision: 9);
        Assert.Equal(expectedMinX, timeAxis.AbsoluteMinimum, precision: 9);
        Assert.Equal(expectedMaxX, timeAxis.AbsoluteMaximum, precision: 9);
    }

    [Fact]
    public void AnalysisModes_RequireTransferIr_ShowAnnotationWhenAbsent()
    {
        using var measurement = CreateSweepOnlyMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // With no loopback transfer IR, every analysis mode draws nothing and shows an
        // explanatory annotation instead. Sweep deconvolution alone is no longer rendered.
        OxyPlot.PlotModel[] models =
        [
            factory.CreateFrequencyResponse(includeCurves: true),
            factory.CreatePhaseResponse(includeCurves: true),
            factory.CreateGroupDelay(includeCurves: true),
            factory.CreateImpulseResponse(includeCurves: true),
            factory.CreateAutocorrelation(includeCurves: true),
        ];
        foreach (OxyPlot.PlotModel model in models)
        {
            Assert.Empty(model.Series);
            Assert.NotEmpty(model.Annotations);
        }
    }

    [Fact]
    public void FrequencyResponse_DrawsCurves_WhenTransferIrPresent()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement();
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        Assert.NotEmpty(factory.CreateFrequencyResponse(includeCurves: true).Series);
    }

    private static ExpSweepMeasurement CreateSweepOnlyMeasurement()
    {
        var sweep = new Complex[2048];
        sweep[64] = Complex.One;

        var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: sweep,
            sweepDeconvolutionPeakIndex: 64);
        return measurement;
    }

    private static ExpSweepMeasurement CreateTransferMeasurement()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = Complex.One;

        var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64);
        return measurement;
    }

    private static ExpSweepMeasurement CreateTransferMeasurementWithCoherence()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = Complex.One;
        double[] coherence = new double[1025];
        Array.Fill(coherence, 0.9);

        var measurement = new ExpSweepMeasurement();
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64,
            transferCoherence: coherence);
        return measurement;
    }

    private static PlotModelFactory CreateFactory(
        ExpSweepMeasurement measurement,
        NoiseMeasurement noiseMeasurement,
        ImpulseResponseOptions? impulseOptions = null,
        FrequencyResponseOptions? groupDelayOptions = null,
        CurveVisibilityOptions? frequencyResponseVisibility = null,
        CurveVisibilityOptions? phaseResponseVisibility = null,
        CurveVisibilityOptions? groupDelayVisibility = null)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement,
            noiseMeasurement,
            mode => new CalibrationFile(calibrationPath),
            new FrequencyResponseOptions(),
            new FrequencyResponseOptions(),
            groupDelayOptions ?? new FrequencyResponseOptions(),
            frequencyResponseVisibility ?? new CurveVisibilityOptions(),
            phaseResponseVisibility ?? new CurveVisibilityOptions(),
            groupDelayVisibility ?? new CurveVisibilityOptions(),
            impulseOptions ?? new ImpulseResponseOptions(),
            new LiveSpectrumOptions(),
            new WaterfallGenerateOptions(),
            new WaterfallGenerateOptions());
    }
}
