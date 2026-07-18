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
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());

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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());

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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());

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

    [Fact]
    public void PhaseResponse_AutoDetrendPreservesMainCompareRelativeDelay()
    {
        const int sampleRate = 44_100;
        const int mainSample = 64;
        const int compareSample = 86;
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        var phaseOptions = new FrequencyResponseOptions
        {
            PhaseGateOffsetMs = mainSample * 1_000.0 / sampleRate,
            PhaseWindowMode = PhaseWindowMode.Fixed,
            PhaseFdwCycles = 6,
            PhaseDetrendMode = PhaseDetrendMode.Auto,
            Unwrap = true,
            SmoothingInverseOctaves = 0.0
        };
        var visibility = PhaseAllOff();
        visibility.ShowMeasuredPhase = true;
        visibility.ShowExcessPhase = true;
        PlotModelFactory factory = CreateFactory(
            measurement,
            noiseMeasurement,
            phaseResponseOptions: phaseOptions,
            phaseResponseVisibility: visibility);
        var compareImpulse = new Complex[2048];
        compareImpulse[compareSample] = Complex.One;
        factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
            "Delayed reference", sampleRate, compareImpulse, compareSample));

        List<LineSeries> series = factory.CreatePhaseResponse(includeCurves: true)
            .Series.OfType<LineSeries>().ToList();
        double expectedChange = -360.0 * (compareSample - mainSample) /
            sampleRate * 1_000.0;
        AssertRelativePhaseSlope(series, AnalysisCurveKind.Primary, expectedChange);

        IImpulseMeasurement mainView =
            new MeasurementPlotContext(measurement).CreatePrimaryMeasurement();
        var compareView = new ImpulseMeasurementView(
            compareImpulse, compareSample, sampleRate);
        PhaseAnalysisSettings autoSettings = phaseOptions.CreatePhaseAnalysisSettings();
        double commonDetrend = DataHelper.ResolvePhaseDetrendMilliseconds(
            mainView, autoSettings);
        PhaseAnalysisSettings commonSettings = autoSettings with
        {
            DetrendMode = PhaseDetrendMode.Manual,
            ManualDetrendMilliseconds = commonDetrend
        };
        AnalysisCurve expectedExcess = DataHelper.GetExcessPhase(
            compareView, commonSettings);
        LineSeries compareExcess = series.Single(item => item.Tag is CurveTag
        {
            Kind: AnalysisCurveKind.ExcessPhase,
            Source: CurveSource.Compare
        });
        AssertCurveValueAt(compareExcess, expectedExcess, 500.0);
        AssertCurveValueAt(compareExcess, expectedExcess, 1_500.0);
    }

    private static void AssertCurveValueAt(
        LineSeries actual,
        AnalysisCurve expected,
        double frequency)
    {
        OxyPlot.DataPoint actualPoint = actual.Points.MinBy(point =>
            Math.Abs(point.X - frequency));
        SignalPoint expectedPoint = expected.Points.MinBy(point =>
            Math.Abs(point.X - frequency));
        Assert.Equal(expectedPoint.Y, actualPoint.Y, tolerance: 1e-9);
    }

    private static void AssertRelativePhaseSlope(
        IEnumerable<LineSeries> series,
        AnalysisCurveKind kind,
        double expectedChange)
    {
        List<LineSeries> matching = series.Where(item => item.Tag is CurveTag tag &&
            tag.Mode == Mode.PhaseResponse && tag.Kind == kind).ToList();
        LineSeries main = matching.Single(item =>
            ((CurveTag)item.Tag!).Source == CurveSource.Main);
        LineSeries compare = matching.Single(item =>
            ((CurveTag)item.Tag!).Source == CurveSource.Compare);
        double differenceAt500 = PhaseDifferenceAt(main, compare, 500.0);
        double differenceAt1500 = PhaseDifferenceAt(main, compare, 1_500.0);
        Assert.Equal(expectedChange, differenceAt1500 - differenceAt500, tolerance: 2.0);
    }

    private static double PhaseDifferenceAt(
        LineSeries main,
        LineSeries compare,
        double frequency)
    {
        int index = main.Points
            .Select((point, i) => (Distance: Math.Abs(point.X - frequency), Index: i))
            .MinBy(candidate => candidate.Distance).Index;
        return compare.Points[index].Y - main.Points[index].Y;
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

    [Theory]
    [InlineData(0, CurveSource.Main)]
    [InlineData(6, CurveSource.Main)]
    [InlineData(SpectrumSmoothing.PsychoacousticCode, CurveSource.Main)]
    [InlineData(0, CurveSource.Compare)]
    [InlineData(6, CurveSource.Compare)]
    [InlineData(SpectrumSmoothing.PsychoacousticCode, CurveSource.Compare)]
    public void RawOverlayCapture_WithCalibration_ReproducesDisplayedFrequencyResponse(
        int smoothing,
        CurveSource source)
    {
        CalibrationFile calibration = CalibrationFile.Parse(
            """
            20 18
            35 -12
            70 15
            130 -9
            250 12
            500 -7
            1000 10
            2000 -6
            4000 9
            8000 -5
            12000 8
            20000 -4
            """);
        var options = new FrequencyResponseOptions
        {
            CalibrationMode = MicrophoneCalibrationMode.Degrees0,
            SmoothingInverseOctaves = smoothing
        };
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(
            measurement,
            noiseMeasurement,
            frequencyResponseOptions: options,
            calibration: calibration);

        if (source == CurveSource.Compare)
        {
            var compareImpulse = new Complex[2048];
            compareImpulse[64] = Complex.One;
            compareImpulse[77] = new Complex(0.35, 0.0);
            factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareImpulse,
                64));
        }

        LineSeries displayed = factory.CreateFrequencyResponse(includeCurves: true)
            .Series
            .OfType<LineSeries>()
            .Single(series => series.Tag is CurveTag tag &&
                tag.Mode == Mode.FrequencyResponse &&
                tag.Kind == AnalysisCurveKind.Primary &&
                tag.Source == source);
        RawCurveCapture? capture = factory.BuildRawCurve((CurveTag)displayed.Tag!);

        Assert.True(capture.HasValue);
        Assert.Equal(
            RawCurveRenderer.PointCount,
            capture.Value.CalibrationCorrectionDb.Count);
        List<SignalPoint> overlay = RawCurveRenderer.Render(
            capture.Value.Spectrum,
            capture.Value.CalibrationCorrectionDb,
            smoothing);

        Assert.Equal(displayed.Points.Count, overlay.Count);
        for (int i = 0; i < overlay.Count; i++)
        {
            Assert.Equal(displayed.Points[i].X, overlay[i].X);
            Assert.Equal(displayed.Points[i].Y, overlay[i].Y, tolerance: 1e-12);
        }
    }

    [Fact]
    public void ComplexSum_OfTwoIdenticalTransferResponses_AddsSixDecibels()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // The Compare impulse arrives 10 samples early; delaying it by exactly
        // 10 samples' worth of milliseconds re-aligns it with the main impulse,
        // restoring the fully coherent +6 dB sum.
        var compareIr = new Complex[2048];
        compareIr[54] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 54));

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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;

        // A Compare at a different sample rate cannot be summed sample-wise.
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 48_000, compareIr, 64));
        Assert.Null(factory.TryBuildComplexSumCurve());

        // A Compare without a transfer IR has no loopback time reference.
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, Array.Empty<Complex>(), 0));
        Assert.Null(factory.TryBuildComplexSumCurve());
    }

    [Fact]
    public void ComplexSumLoss_IsZero_WhenSourcesSumCoherently()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        // Without a Compare measurement there is nothing to compare against.
        Assert.Null(factory.TryBuildComplexSumLossCurve());

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

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

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
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
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

        Assert.NotEmpty(factory.CreateFrequencyResponse(includeCurves: true).Series);
    }

    [Fact]
    public void CreateFrequencyResponse_InSplMode_UsesTheSplAxisAndLimits()
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement();
        // The result's own frozen calibration (matching the default Wave input) plus
        // its input identity and a captured loopback level are the ingredients of K.
        var anchor = new SplCalibration
        {
            ReferenceLevelDbSpl = 94,
            MeasuredLevelDbFs = -20,
            Backend = Resonalyze.Audio.AudioBackend.Wave,
            SampleRate = 44_100,
            Bits = 24,
            MicrophoneChannelOffset = 0,
            InputDeviceNumber = -1
        };
        measurement.MeasurementSplCalibration = anchor;
        measurement.MeasurementInput = anchor.CaptureIdentity;
        measurement.RestoreLevelSnapshot(new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(true, -6, -9, false, true)));
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        var splOptions = new FrequencyResponseOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        OxyPlot.PlotModel model = CreateFactory(
                measurement, noise, frequencyResponseOptions: splOptions)
            .CreateFrequencyResponse(includeCurves: true);

        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        // Curves sit near 40–110 dB, so the axis must reach well above the dBr
        // ceiling of 10 or the plot would be blank.
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Equal(PlotModelStyle.SplDecibelAbsoluteMaximum, dbAxis.AbsoluteMaximum);
    }

    [Fact]
    public void CreateFrequencyResponse_WithoutCalibration_StaysOnTheRelativeAxis()
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var splOptions = new FrequencyResponseOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        OxyPlot.PlotModel model = CreateFactory(
                measurement, noise, frequencyResponseOptions: splOptions)
            .CreateFrequencyResponse(includeCurves: true);

        // SPL was requested but no calibration is available: fall back to dBr/dBc.
        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dBr/dBc", dbAxis.Title);
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, dbAxis.Maximum);
    }

    private static ExpSweepMeasurement CreateSweepOnlyMeasurement()
    {
        var sweep = new Complex[2048];
        sweep[64] = Complex.One;

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
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

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
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

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
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
        FrequencyResponseOptions? phaseResponseOptions = null,
        CurveVisibilityOptions? frequencyResponseVisibility = null,
        CurveVisibilityOptions? phaseResponseVisibility = null,
        CurveVisibilityOptions? groupDelayVisibility = null,
        FrequencyResponseOptions? frequencyResponseOptions = null,
        LiveSpectrumOptions? liveSpectrumOptions = null,
        CalibrationFile? calibration = null)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement,
            noiseMeasurement,
            mode => calibration ?? new CalibrationFile(calibrationPath),
            frequencyResponseOptions ?? new FrequencyResponseOptions(),
            phaseResponseOptions ?? new FrequencyResponseOptions(),
            groupDelayOptions ?? new FrequencyResponseOptions(),
            frequencyResponseVisibility ?? new CurveVisibilityOptions(),
            phaseResponseVisibility ?? new CurveVisibilityOptions(),
            groupDelayVisibility ?? new CurveVisibilityOptions(),
            impulseOptions ?? new ImpulseResponseOptions(),
            liveSpectrumOptions ?? new LiveSpectrumOptions(),
            new WaterfallGenerateOptions(),
            new WaterfallGenerateOptions());
    }

    // A live analyzer configured for the default Wave input, so it produces a
    // concrete input identity an SPL anchor can be pinned to.
    private static NoiseMeasurement CreateLiveAnalyzer()
    {
        var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        noise.Init(
            44_100,
            24,
            60,
            PlaybackChannel.Mono,
            sequenceLength: 2048,
            waveInputChannelOffset: 0,
            waveLoopbackInputChannelOffset: 1);
        return noise;
    }

    // An SPL anchor captured on the input the live analyzer runs on, so it validates.
    private static SplCalibration LiveAnchorMatching(
        NoiseMeasurement noise,
        double referenceLevelDbSpl,
        double measuredLevelDbFs)
    {
        MeasurementInputIdentity id = noise.CurrentInputIdentity();
        return new SplCalibration
        {
            ReferenceLevelDbSpl = referenceLevelDbSpl,
            MeasuredLevelDbFs = measuredLevelDbFs,
            Backend = id.Backend,
            SampleRate = id.SampleRate,
            Bits = id.Bits,
            MicrophoneChannelOffset = id.MicrophoneChannelOffset,
            InputDeviceNumber = id.InputDeviceNumber,
            WasapiCaptureEndpointId = id.WasapiCaptureEndpointId,
            AsioDriverName = id.AsioDriverName
        };
    }

    [Fact]
    public void CreateLiveSpectrum_InSplMode_UsesTheSplAxis()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        // Live uses the CONFIGURED calibration (there is no frozen snapshot), validated
        // against the live input.
        measurement.SplCalibration = LiveAnchorMatching(noise, 94, -16);
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        Assert.Equal(MagnitudeScale.SoundPressureLevel, factory.EffectiveLiveSpectrumScale);
        Assert.Equal(94 - (-16), factory.LiveSplOffsetDb!.Value, precision: 9);

        OxyPlot.PlotModel model = factory.CreateLiveSpectrum();
        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Contains("SPL", model.Title);
    }

    [Fact]
    public void LiveSplOffset_UnavailableWithoutOrWithMismatchedCalibration()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        // No configured calibration: SPL requested but unavailable, stay on native dB.
        Assert.Null(factory.LiveSplOffsetDb);
        Assert.Equal(MagnitudeScale.Relative, factory.EffectiveLiveSpectrumScale);
        var dbAxis = (OxyPlot.Axes.LinearAxis)factory.CreateLiveSpectrum().Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB", dbAxis.Title);

        // A calibration captured on a different digital input (sample rate) does not
        // apply to this live input.
        SplCalibration mismatched = LiveAnchorMatching(noise, 94, -16);
        mismatched.SampleRate = 48_000;
        measurement.SplCalibration = mismatched;
        Assert.Null(factory.LiveSplOffsetDb);
        Assert.Equal(MagnitudeScale.Relative, factory.EffectiveLiveSpectrumScale);
    }

    [Fact]
    public void LiveSplPeakHold_HoldsBandPowerNotTheSumOfPerBinMaxima()
    {
        // Finding: two frames whose energy sits in different bins of one band must not
        // peak-hold to the SUM of their bin maxima (+3 dB over any real band level).
        // The controller holds the max of BuildMainDisplayPoints (already band powers),
        // so the held level is one frame's band, not both bins added.
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        measurement.SplCalibration = LiveAnchorMatching(noise, 94, -16);
        var options = new LiveSpectrumOptions
        {
            CalibrationMode = MicrophoneCalibrationMode.Off,
            SmoothingInverseOctaves = 6,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        int binCount = noise.SequenceLength / 2;
        // Two adjacent bins near 1 kHz (44100/2048 ≈ 21.5 Hz/bin, ~5 bins per 1/6 oct).
        const int binA = 47;
        const int binB = 48;
        var frameA = new double[binCount];
        var frameB = new double[binCount];
        var frameBoth = new double[binCount];
        frameA[binA] = 1.0;
        frameB[binB] = 1.0;
        frameBoth[binA] = 1.0;
        frameBoth[binB] = 1.0;

        List<SignalPoint> bandA = factory.BuildMainDisplayPoints(frameA, rtaOnly: true);
        List<SignalPoint> bandB = factory.BuildMainDisplayPoints(frameB, rtaOnly: true);
        List<SignalPoint> bandBoth = factory.BuildMainDisplayPoints(frameBoth, rtaOnly: true);

        // The peak band across the two single-bin frames (what a correct peak hold shows).
        int peak = 0;
        for (int i = 1; i < bandBoth.Count; i++)
        {
            if (bandBoth[i].Y > bandBoth[peak].Y)
            {
                peak = i;
            }
        }

        double held = Math.Max(bandA[peak].Y, bandB[peak].Y);
        // Both bins present in one frame is ~3 dB above either alone; the peak hold of
        // the two single-bin frames must stay near a single band, well below that sum.
        Assert.True(
            bandBoth[peak].Y - held > 2.0,
            $"peak hold {held:0.00} dB reached the summed band {bandBoth[peak].Y:0.00} dB");
    }

    [Fact]
    public void CreateLiveSpectrum_MicOnly_ShowsTheRtaWithNoCoherenceAxis()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        // No loopback configured: the analyzer is a single-channel RTA.
        noise.Init(44_100, 24, 60, PlaybackChannel.Mono, sequenceLength: 2048, waveInputChannelOffset: 0);
        Assert.True(noise.IsMicOnly);

        var options = new LiveSpectrumOptions { ShowCoherence = true };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        OxyPlot.PlotModel model = factory.CreateLiveSpectrum();

        Assert.Contains("RTA", model.Title);
        // There is no transfer function, so no coherence axis even though the
        // coherence curve is requested.
        Assert.DoesNotContain(model.Axes, axis => axis.Key == PlotModelFactory.CoherenceAxisKey);
    }

    [Fact]
    public void LiveRta_InSplMode_IsLiftedByTheCalibrationOffset()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();

        // The SPL RTA is power-integrated (not a constant shift of the amplitude
        // trace), so isolate the OFFSET: two calibrations differing only in offset
        // must move the identical power-band curve by exactly the offset difference.
        var options = new LiveSpectrumOptions
        {
            CalibrationMode = MicrophoneCalibrationMode.Off,
            SmoothingInverseOctaves = 0,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        var magnitude = new double[noise.SequenceLength / 2];
        Array.Fill(magnitude, 0.1);

        measurement.SplCalibration = LiveAnchorMatching(noise, 94, -16);  // offset 110
        LineSeries lower = factory.BuildInputMagnitudeSeries(magnitude);
        measurement.SplCalibration = LiveAnchorMatching(noise, 104, -16); // offset 120
        LineSeries higher = factory.BuildInputMagnitudeSeries(magnitude);

        Assert.Equal(lower.Points.Count, higher.Points.Count);
        Assert.NotEmpty(lower.Points);
        for (int i = 0; i < lower.Points.Count; i++)
        {
            Assert.Equal(lower.Points[i].X, higher.Points[i].X, precision: 9);
            Assert.Equal(lower.Points[i].Y + 10.0, higher.Points[i].Y, precision: 6);
        }
    }
}
