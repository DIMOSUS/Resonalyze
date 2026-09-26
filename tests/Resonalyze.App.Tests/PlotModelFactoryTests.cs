using System.Numerics;
using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class PlotModelFactoryTests
{
    [Fact]
    public void MeasurementPlotTitles_IncludeImpulseResponseFileName()
    {
        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
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
            sweepDeconvolutionPeakIndex: 1));

        PlotModelFactory factory = CreateFactory(measurement);
        measurement.Document.Rename(@"C:\Temp\My Measurement.json");

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
        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
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
            sweepDeconvolutionPeakIndex: 1));

        PlotModelFactory factory = CreateFactory(measurement);
        measurement.Document.Rename(null);

        Assert.Equal(
            "Frequency Response",
            factory.CreateFrequencyResponse(includeCurves: false).Title);
    }

    [Fact]
    public void ImpulseResponse_RespectsShowImpulseFlag()
    {
        using var measurement = CreateTransferMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowImpulse = false };
        PlotModelFactory factory =
            CreateFactory(measurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateImpulseResponse(includeCurves: true).Series);

        impulseOptions.ShowImpulse = true;
        Assert.NotEmpty(factory.CreateImpulseResponse(includeCurves: true).Series);
    }

    [Fact]
    public void AFrozenFactory_BuildsWhatWasOpenWhenItWasFrozen()
    {
        using var measurement = CreateTransferMeasurement();
        measurement.Document.Rename("first.json");
        PlotModelFactory factory = CreateFactory(measurement);
        var compareImpulse = new Complex[2048];
        compareImpulse[80] = Complex.One;
        CompareAnalysisSource? compare = new("reference", 44_100, compareImpulse, 80, Band: MeasuredBand.Everything);
        factory.SetCompareSourceProvider(() => compare);

        PlotModelFactory frozen = factory.Freeze();
        measurement.Document.Clear();
        Assert.NotNull(measurement.Document.TryAcquire());
        compare = null;

        PlotModel model = frozen.CreateFrequencyResponse(includeCurves: true);
        Assert.Equal("Frequency Response - first.json", model.Title);
        Assert.Contains(model.Series, series => series.Tag is CurveTag { Source: CurveSource.Main });
        Assert.Contains(model.Series, series => series.Tag is CurveTag { Source: CurveSource.Compare });
        Assert.Empty(factory.CreateFrequencyResponse(includeCurves: true).Series);
    }

    [Theory]
    [InlineData(Mode.ImpulseResponse)]
    [InlineData(Mode.FrequencyResponse)]
    [InlineData(Mode.PhaseResponse)]
    [InlineData(Mode.GroupDelay)]
    [InlineData(Mode.CumulativeSpectrumDecay)]
    [InlineData(Mode.BurstDecay)]
    [InlineData(Mode.Autocorrelation)]
    public void ACurveBuild_StopsOnACancelledToken(Mode mode)
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        Assert.ThrowsAny<OperationCanceledException>(
            () => factory.Create(mode, includeCurves: true, new CancellationToken(true)));
        Assert.NotNull(factory.Create(mode, includeCurves: false, new CancellationToken(true)));
    }

    [Fact]
    public void Autocorrelation_RespectsShowAutocorrelationFlag()
    {
        using var measurement = CreateTransferMeasurement();
        var impulseOptions = new ImpulseResponseOptions { ShowAutocorrelation = false };
        PlotModelFactory factory =
            CreateFactory(measurement, impulseOptions: impulseOptions);

        Assert.Empty(factory.CreateAutocorrelation(includeCurves: true).Series);

        impulseOptions.ShowAutocorrelation = true;
        Assert.NotEmpty(factory.CreateAutocorrelation(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_RespectsShowGroupDelayFlag()
    {
        using var measurement = CreateTransferMeasurement();
        var groupDelayVisibility = new CurveVisibilityOptions
        {
            ShowGroupDelay = false,
            ShowMinimumPhaseGroupDelay = false,
            ShowExcessGroupDelay = false
        };
        PlotModelFactory factory =
            CreateFactory(measurement, groupDelayVisibility: groupDelayVisibility);

        Assert.Empty(factory.CreateGroupDelay(includeCurves: true).Series);

        groupDelayVisibility.ShowGroupDelay = true;
        Assert.NotEmpty(factory.CreateGroupDelay(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_ReadsThroughTheOptionsWindow()
    {
        // Direct arrival plus a copy 6 ms later: FDW drops the reflection's ripple at the top of the band.
        const int sampleRate = 48_000;
        const int peakSample = 480;
        var impulse = new Complex[4_096];
        impulse[peakSample] = Complex.One;
        impulse[peakSample + 288] = new Complex(0.5, 0.0);
        using var measurement = CreateTransferMeasurement(impulse, peakSample, sampleRate);
        var options = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.Fixed,
            GroupDelayFdwCycles = 8,
            GroupDelayLeftMs = 1.0,
            GroupDelayPlateauMs = 10.0,
            GroupDelayRightMs = 3.0,
            SmoothingInverseOctaves = 0
        };
        var visibility = new CurveVisibilityOptions
        {
            ShowGroupDelay = true,
            ShowMinimumPhaseGroupDelay = true,
            ShowExcessGroupDelay = true
        };
        PlotModelFactory factory = CreateFactory(
            measurement,
            groupDelayOptions: options,
            groupDelayVisibility: visibility);

        LineSeries fixedMeasured = MeasuredGroupDelaySeries(factory);
        GroupDelayCurveSet legacy = DataHelper.GetGroupDelayCurves(
            new MeasurementPlotContext(measurement.Document).CreatePrimaryMeasurement(),
            options.GroupDelayGateOffsetMs,
            options.GroupDelayLeftMs,
            options.GroupDelayPlateauMs,
            options.GroupDelayRightMs,
            options.SmoothingInverseOctaves,
            PlotModelFactory.GroupDelayMagnitudeGateDb,
            includeMinimumPhase: true);
        Assert.Equal(legacy.Measured.Points.Count, fixedMeasured.Points.Count);
        for (int i = 0; i < legacy.Measured.Points.Count; i++)
        {
            Assert.Equal(legacy.Measured.Points[i].X, fixedMeasured.Points[i].X);
            Assert.Equal(legacy.Measured.Points[i].Y, fixedMeasured.Points[i].Y);
        }

        options.GroupDelayWindowMode = PhaseWindowMode.FrequencyDependent;
        PlotModel fdwModel = factory.CreateGroupDelay(includeCurves: true);
        List<CurveTag> tags = fdwModel.Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.Primary);
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.MinimumPhaseGroupDelay);
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.ExcessGroupDelay);

        LineSeries fdwMeasured = MeasuredGroupDelaySeries(factory);
        double arrivalMs = peakSample * 1000.0 / sampleRate;
        double fixedSwing = Swing(fixedMeasured, 2_000, 10_000);
        double fdwSwing = Swing(fdwMeasured, 2_000, 10_000);
        Assert.True(fixedSwing > 1.0, $"the fixed gate did not see the reflection ({fixedSwing:0.000} ms)");
        Assert.True(fdwSwing < 0.2, $"FDW still carried the reflection ({fdwSwing:0.000} ms)");
        Assert.All(
            fdwMeasured.Points.Where(p => p.X is >= 2_000 and <= 10_000),
            p => Assert.InRange(p.Y, arrivalMs - 0.1, arrivalMs + 0.1));
    }

    private static LineSeries MeasuredGroupDelaySeries(PlotModelFactory factory) =>
        factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Single(series => series.Tag is CurveTag { Kind: AnalysisCurveKind.Primary });

    private static double Swing(LineSeries series, double lowHz, double highHz)
    {
        List<double> values = series.Points
            .Where(p => p.X >= lowHz && p.X <= highHz && double.IsFinite(p.Y))
            .Select(p => p.Y)
            .ToList();
        Assert.NotEmpty(values);
        return values.Max() - values.Min();
    }

    [Fact]
    public void GroupDelay_MinimumAndExcessCurves_FollowTheirFlags()
    {
        using var measurement = CreateTransferMeasurement();
        var groupDelayVisibility = new CurveVisibilityOptions
        {
            ShowGroupDelay = true,
            ShowMinimumPhaseGroupDelay = true,
            ShowExcessGroupDelay = true
        };
        PlotModelFactory factory =
            CreateFactory(measurement, groupDelayVisibility: groupDelayVisibility);

        List<CurveTag> tags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.Primary);
        Assert.Contains(
            tags, tag => tag.Kind == AnalysisCurveKind.MinimumPhaseGroupDelay);
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.ExcessGroupDelay);

        groupDelayVisibility.ShowGroupDelay = false;
        groupDelayVisibility.ShowExcessGroupDelay = false;
        tags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.DoesNotContain(tags, tag => tag.Kind == AnalysisCurveKind.Primary);
        Assert.Contains(
            tags, tag => tag.Kind == AnalysisCurveKind.MinimumPhaseGroupDelay);
        Assert.DoesNotContain(
            tags, tag => tag.Kind == AnalysisCurveKind.ExcessGroupDelay);
    }

    [Fact]
    public void GroupDelay_AxisAutoFit_FollowsMeasuredAndPinsZeroForMinimum()
    {
        // ~5.4 ms arrival: the fit follows the measured level, extends to zero only for the minimum curve,
        // and never reads the pair's own points (band-edge cepstral swings).
        using var measurement = CreateTransferMeasurement(peakSample: 240);

        OxyPlot.Axes.Axis AxisOf(bool showMeasured, bool showMinimum, bool showExcess)
        {
            var visibility = new CurveVisibilityOptions
            {
                ShowGroupDelay = showMeasured,
                ShowMinimumPhaseGroupDelay = showMinimum,
                ShowExcessGroupDelay = showExcess
            };
            PlotModelFactory factory = CreateFactory(
                measurement, groupDelayVisibility: visibility);
            return factory.CreateGroupDelay(includeCurves: true).Axes
                .First(axis => axis.Key == PlotModelFactory.GroupDelayAxisKey);
        }

        OxyPlot.Axes.Axis allThree = AxisOf(
            showMeasured: true, showMinimum: true, showExcess: true);
        Assert.True(
            allThree.Minimum < 0.2,
            $"the minimum curve is clipped out (axis starts at {allThree.Minimum:0.00} ms)");
        Assert.True(
            allThree.Maximum > 5.0,
            $"the measured level is clipped out (axis ends at {allThree.Maximum:0.00} ms)");

        OxyPlot.Axes.Axis excessOnly = AxisOf(
            showMeasured: false, showMinimum: false, showExcess: true);
        Assert.True(
            excessOnly.Maximum > 5.0,
            $"the excess curve is clipped out (axis ends at {excessOnly.Maximum:0.00} ms)");
        Assert.True(
            excessOnly.Minimum < 5.0,
            $"the excess curve is clipped out (axis starts at {excessOnly.Minimum:0.00} ms)");

        OxyPlot.Axes.Axis withoutMinimum = AxisOf(
            showMeasured: true, showMinimum: false, showExcess: true);
        Assert.True(
            withoutMinimum.Minimum > 2.0,
            $"zero was pinned with no minimum curve shown ({withoutMinimum.Minimum:0.00} ms)");

        OxyPlot.Axes.Axis minimumOnly = AxisOf(
            showMeasured: false, showMinimum: true, showExcess: false);
        Assert.Equal(-5.0, minimumOnly.Minimum);
        Assert.Equal(5.0, minimumOnly.Maximum);
    }

    [Fact]
    public void GroupDelay_CompareSource_GetsMinimumAndExcessCurvesToo()
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64));

        List<CurveTag> tags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(
            tags,
            tag => tag.Source == CurveSource.Compare &&
                tag.Kind == AnalysisCurveKind.MinimumPhaseGroupDelay);
        Assert.Contains(
            tags,
            tag => tag.Source == CurveSource.Compare &&
                tag.Kind == AnalysisCurveKind.ExcessGroupDelay);
    }

    // The minimum-phase curve carries no bulk delay, so it stays comparable across clocks.
    [Theory]
    [InlineData(TimingReference.SynchronizedLoopback, true)]
    [InlineData(TimingReference.RecordedSweep, false)]
    public void GroupDelay_TheTimeReadingCompareCurvesNeedOneClock(
        TimingReference compareTiming,
        bool sharesTheClock)
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64,
                TimingReference: compareTiming));

        List<AnalysisCurveKind> kinds = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .Where(tag => tag.Source == CurveSource.Compare)
            .Select(tag => tag.Kind)
            .ToList();

        Assert.Contains(AnalysisCurveKind.MinimumPhaseGroupDelay, kinds);
        Assert.Equal(sharesTheClock, kinds.Contains(AnalysisCurveKind.Primary));
        Assert.Equal(sharesTheClock, kinds.Contains(AnalysisCurveKind.ExcessGroupDelay));
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(TimingReference.SynchronizedLoopback, true)]
    [InlineData(TimingReference.RecordedSweep, false)]
    public void PhaseResponse_TheTimeReadingCompareCurvesNeedOneClock(
        TimingReference compareTiming,
        bool sharesTheClock)
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64,
                TimingReference: compareTiming));

        List<AnalysisCurveKind> kinds = factory.CreatePhaseResponse(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .Where(tag => tag.Source == CurveSource.Compare)
            .Select(tag => tag.Kind)
            .ToList();

        Assert.Contains(AnalysisCurveKind.MinimumPhase, kinds);
        Assert.Equal(sharesTheClock, kinds.Contains(AnalysisCurveKind.Primary));
        Assert.Equal(sharesTheClock, kinds.Contains(AnalysisCurveKind.ExcessPhase));
    }

    [Fact]
    public void GroupDelay_TagsMainAndCompareCurvesForLinkedOverlays()
    {
        using var measurement = CreateTransferMeasurement();
        var groupDelayVisibility = new CurveVisibilityOptions { ShowGroupDelay = true };
        PlotModelFactory factory =
            CreateFactory(measurement, groupDelayVisibility: groupDelayVisibility);

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

        var hidden = new CurveVisibilityOptions { ShowPrimary = false };
        Assert.DoesNotContain(
            "Frequency Response",
            SeriesTitles(CreateFactory(
                    measurement, frequencyResponseVisibility: hidden)
                .CreateFrequencyResponse(includeCurves: true)));

        var shown = new CurveVisibilityOptions { ShowPrimary = true };
        Assert.Contains(
            "Frequency Response",
            SeriesTitles(CreateFactory(
                    measurement, frequencyResponseVisibility: shown)
                .CreateFrequencyResponse(includeCurves: true)));
    }

    [Theory]
    [InlineData(nameof(CurveVisibilityOptions.ShowMeasuredPhase), "Phase")]
    [InlineData(nameof(CurveVisibilityOptions.ShowMinimumPhase), "Minimum Phase")]
    [InlineData(nameof(CurveVisibilityOptions.ShowExcessPhase), "Excess Phase")]
    public void PhaseResponse_VisibilityFlagGatesItsCurve(string flag, string title)
    {
        using var measurement = CreateTransferMeasurement();

        var hidden = PhaseAllOff();
        Assert.DoesNotContain(
            title,
            SeriesTitles(CreateFactory(
                    measurement, phaseResponseVisibility: hidden)
                .CreatePhaseResponse(includeCurves: true)));

        var shown = PhaseAllOff();
        typeof(CurveVisibilityOptions).GetProperty(flag)!.SetValue(shown, true);
        Assert.Contains(
            title,
            SeriesTitles(CreateFactory(
                    measurement, phaseResponseVisibility: shown)
                .CreatePhaseResponse(includeCurves: true)));
    }

    [Fact]
    public void PhaseResponse_ShowCoherenceFlag_GatesTheCoherenceCurve()
    {
        using var measurement = CreateTransferMeasurementWithCoherence();

        var hidden = PhaseAllOff();
        hidden.ShowCoherence = false;
        Assert.DoesNotContain(
            "Coherence",
            SeriesTitles(CreateFactory(
                    measurement, phaseResponseVisibility: hidden)
                .CreatePhaseResponse(includeCurves: true)));

        var shown = PhaseAllOff();
        shown.ShowCoherence = true;
        Assert.Contains(
            "Coherence",
            SeriesTitles(CreateFactory(
                    measurement, phaseResponseVisibility: shown)
                .CreatePhaseResponse(includeCurves: true)));
    }

    [Fact]
    public void FrequencyResponse_ArrayCurvesFollowTheirOwnFlags()
    {
        using var measurement = CreateTransferMeasurement();
        measurement.Open(measurement.Result with { ArrayMicrophones = SyntheticArray() });

        var off = new CurveVisibilityOptions();
        Assert.DoesNotContain(
            "Array average",
            SeriesTitles(CreateFactory(
                    measurement, frequencyResponseVisibility: off)
                .CreateFrequencyResponse(includeCurves: true)));

        var shown = new CurveVisibilityOptions
        {
            ShowArrayAverage = true,
            ShowArrayMicrophones = true,
            ShowArraySpread = true
        };
        IReadOnlyList<string> titles = SeriesTitles(CreateFactory(
                measurement, frequencyResponseVisibility: shown)
            .CreateFrequencyResponse(includeCurves: true));

        Assert.Contains("Array average", titles);
        Assert.Contains("Array spread", titles);
        Assert.Contains("Input 1 (measurement)", titles);
        Assert.Contains("left ear", titles);
    }

    [Fact]
    public void FrequencyResponse_TheArraySpreadGetsItsOwnAxis()
    {
        using var measurement = CreateTransferMeasurement();
        measurement.Open(measurement.Result with { ArrayMicrophones = SyntheticArray() });
        var shown = new CurveVisibilityOptions { ShowArraySpread = true };

        OxyPlot.PlotModel model = CreateFactory(
                measurement, frequencyResponseVisibility: shown)
            .CreateFrequencyResponse(includeCurves: true);

        // A dB range is not a level, so it must not sit on the magnitude axis.
        OxyPlot.Series.Series spread = model.Series.Single(
            series => series.Title == "Array spread");
        Assert.Equal(
            "array-spread",
            ((OxyPlot.Series.LineSeries)spread).YAxisKey);
        Assert.Contains(model.Axes, axis => axis.Key == "array-spread");
    }

    [Fact]
    public void FrequencyResponse_AMeasurementWithoutAnArrayDrawsNone()
    {
        using var measurement = CreateTransferMeasurement();
        var shown = new CurveVisibilityOptions
        {
            ShowArrayAverage = true,
            ShowArrayMicrophones = true,
            ShowArraySpread = true
        };

        OxyPlot.PlotModel model = CreateFactory(
                measurement, frequencyResponseVisibility: shown)
            .CreateFrequencyResponse(includeCurves: true);

        Assert.DoesNotContain("Array average", SeriesTitles(model));
        Assert.DoesNotContain(model.Axes, axis => axis.Key == "array-spread");
    }

    private static IReadOnlyList<ArrayMicrophoneCurve> SyntheticArray()
    {
        int bands = SpatialAverage.GridBandCount;
        return
        [
            new ArrayMicrophoneCurve(
                0, true, Enumerable.Repeat(-6.0, bands).ToArray(), 1),
            new ArrayMicrophoneCurve(
                2, false, Enumerable.Repeat(-9.0, bands).ToArray(), 1)
            {
                Note = "left ear"
            }
        ];
    }

    [Fact]
    public void PhaseResponse_AutoDetrendPreservesMainCompareRelativeDelay()
    {
        const int sampleRate = 44_100;
        const int mainSample = 64;
        const int compareSample = 86;
        using var measurement = CreateTransferMeasurement();
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
            new MeasurementPlotContext(measurement.Document).CreatePrimaryMeasurement();
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
            CalibrationId = MicrophoneCalibrationIds.ZeroDegrees,
            SmoothingInverseOctaves = smoothing
        };
        using TestAnalyzer measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(
            measurement,
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
    public void ComplexSum_StopsWhereNeitherResponseMeasured()
    {
        // The sum has no band of its own: it stops where both contributors stop and plays where either does.
        var impulse = new Complex[2048];
        impulse[64] = Complex.One;
        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 800,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: impulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: impulse,
            transferPeakIndex: 64,
            achievedLowFrequencyHz: 800,
            achievedHighFrequencyHz: 20_000));
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference",
                44_100,
                compareIr,
                64,
                Band: new MeasuredBand(500, 8_000)));

        AnalysisCurve? sum = factory.TryBuildComplexSumCurve();
        Assert.NotNull(sum);

        Assert.All(
            sum!.Points.Where(point => point.X < 480),
            point => Assert.True(
                double.IsNaN(point.Y),
                $"{point.X:0} Hz is below every contributor and must be a break"));
        Assert.Contains(
            sum.Points,
            point => point.X is > 550 and < 700 && double.IsFinite(point.Y));
        Assert.Contains(
            sum.Points,
            point => point.X is > 9_000 and < 12_000 && double.IsFinite(point.Y));
    }

    [Fact]
    public void ComplexSum_OfTwoIdenticalTransferResponses_AddsSixDecibels()
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

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

        // Compare IR equals the main IR: coherent sum is +20·log10(2) dB everywhere.
        AnalysisCurve main = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(
                measurement.Result.Transfer!.ImpulseResponse,
                measurement.Result.Transfer!.PeakIndex,
                measurement.Result.SampleRate),
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
        PlotModelFactory factory = CreateFactory(measurement);

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
                measurement.Result.Transfer!.ImpulseResponse,
                measurement.Result.Transfer!.PeakIndex,
                measurement.Result.SampleRate),
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
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

        AnalysisCurve? cancelled = factory.TryBuildComplexSumCurve(
            compareDelayMs: 0,
            invertComparePolarity: true);
        Assert.NotNull(cancelled);
        Assert.All(cancelled.Points, point => Assert.True(point.Y < -100.0));
    }

    [Fact]
    public void ComplexSum_WindowsAtTheEarlierRecordsOwnStart_NotTheSumsDominantBand()
    {
        // Quiet LF arrival then a loud HF one 30 ms later: the sum must window at the earlier record's start,
        // not at a start estimated on the sum (which reads the loud driver's late front).
        const int sampleRate = 44_100;
        var mainIr = new Complex[8_192];
        int mainPeak = Wavelet(mainIr, onset: 441, hz: 300, amplitude: 0.05,
            decaySeconds: 0.02, sampleRate);
        var compareIr = new Complex[8_192];
        int comparePeak = Wavelet(compareIr, onset: 1_764, hz: 3_000, amplitude: 1.0,
            decaySeconds: 0.005, sampleRate);

        using var measurement = CreateTransferMeasurement(mainIr, mainPeak, sampleRate);
        PlotModelFactory factory = CreateFactory(measurement);
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", sampleRate, compareIr, comparePeak));

        // The fixture must discriminate: on the sum the estimator locks onto the late front.
        var sum = new Complex[8_192];
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] = mainIr[i] + compareIr[i];
        }
        int mainStart = TransferIrStartCache.ResolveStartIndex(
            mainIr, sampleRate, mainPeak);
        int sumStart = TransferIrStartCache.ResolveStartIndex(
            sum, sampleRate, Math.Min(mainPeak, comparePeak));
        var options = new FrequencyResponseOptions();
        Assert.True(
            sumStart > mainStart + options.LeftTukeyWindow,
            $"fixture: the sum's own estimated start {sumStart} did not lock " +
            $"onto the late driver (main starts at {mainStart})");

        AnalysisCurve? summed = factory.TryBuildComplexSumCurve();
        Assert.NotNull(summed);

        AnalysisCurve main = DataHelper.GetPrimarySpectrum(
            new ImpulseMeasurementView(mainIr, mainPeak, sampleRate),
            options,
            calibration: null);
        double summedAt300 = At(summed.Points, 300);
        double mainAt300 = At(main.Points, 300);
        Assert.True(
            Math.Abs(summedAt300 - mainAt300) < 1.5,
            $"the sum read {summedAt300:0.00} dB at 300 Hz against the early " +
            $"driver's own {mainAt300:0.00} dB");

        // The epsilon covers only the per-record window placements; a lost driver would break loss <= 0.
        AnalysisCurve? loss = factory.TryBuildComplexSumLossCurve();
        Assert.NotNull(loss);
        Assert.All(loss.Points, point => Assert.True(
            point.Y <= 0.1,
            $"summation loss reads +{point.Y:0.000} dB at {point.X:0.#} Hz"));
    }

    private static int Wavelet(
        Complex[] impulse,
        int onset,
        double hz,
        double amplitude,
        double decaySeconds,
        int sampleRate)
    {
        int peak = onset;
        for (int i = 0; onset + i < impulse.Length; i++)
        {
            double t = i / (double)sampleRate;
            impulse[onset + i] += amplitude * Math.Sin(2 * Math.PI * hz * t) *
                Math.Exp(-t / decaySeconds);
            if (impulse[onset + i].Magnitude > impulse[peak].Magnitude)
            {
                peak = onset + i;
            }
        }

        return peak;
    }

    private static double At(IReadOnlyList<SignalPoint> points, double hz)
    {
        SignalPoint best = points[0];
        foreach (SignalPoint point in points)
        {
            if (Math.Abs(point.X - hz) < Math.Abs(best.X - hz))
            {
                best = point;
            }
        }

        return best.Y;
    }

    [Fact]
    public void ComplexSum_RequiresMatchingSampleRateAndTransferIr()
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;

        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 48_000, compareIr, 64));
        Assert.Null(factory.TryBuildComplexSumCurve());

        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, Array.Empty<Complex>(), 0));
        Assert.Null(factory.TryBuildComplexSumCurve());
    }

    [Fact]
    public void ComplexSumLoss_IsZero_WhenSourcesSumCoherently()
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        Assert.Null(factory.TryBuildComplexSumLossCurve());

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

        AnalysisCurve? loss = factory.TryBuildComplexSumLossCurve();
        Assert.NotNull(loss);
        Assert.All(loss.Points, point => Assert.Equal(0.0, point.Y, precision: 4));
    }

    [Fact]
    public void ComplexSumLoss_IsLarge_WhenSourcesCancel()
    {
        using var measurement = CreateTransferMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

        var compareIr = new Complex[2048];
        compareIr[64] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource(
                "Reference", 44_100, compareIr, 64));

        AnalysisCurve? loss = factory.TryBuildComplexSumLossCurve(
            compareDelayMs: 0,
            invertComparePolarity: true);
        Assert.NotNull(loss);
        Assert.All(loss.Points, point => Assert.True(point.Y < -40.0));
    }

    [Fact]
    public void ComplexSumLoss_WithAnExplicitWidth_IgnoresThePlotsOwnSmoothing()
    {
        // The plot's smoothing must not reach the slot's loss curve (operands divided unsmoothed).
        using var measurement = CreateTransferMeasurement();
        var options = new FrequencyResponseOptions
        {
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode
        };
        PlotModelFactory factory = CreateFactory(
            measurement, frequencyResponseOptions: options);
        var compareIr = new Complex[2048];
        compareIr[70] = Complex.One;
        factory.SetCompareSourceProvider(
            () => new CompareAnalysisSource("Reference", 44_100, compareIr, 70));

        AnalysisCurve? underPsychoacoustic =
            factory.TryBuildComplexSumLossCurve(smoothingInverseOctaves: 0);
        options.SmoothingInverseOctaves = 6;
        AnalysisCurve? underSixth =
            factory.TryBuildComplexSumLossCurve(smoothingInverseOctaves: 0);

        Assert.NotNull(underPsychoacoustic);
        Assert.NotNull(underSixth);
        Assert.Equal(underPsychoacoustic.Points.Count, underSixth.Points.Count);
        for (int i = 0; i < underSixth.Points.Count; i++)
        {
            Assert.Equal(underPsychoacoustic.Points[i].Y, underSixth.Points[i].Y, 12);
        }

        AnalysisCurve? smoothed = factory.TryBuildComplexSumLossCurve(
            smoothingInverseOctaves: SpectrumSmoothing.PsychoacousticCode);
        Assert.NotNull(smoothed);
        Assert.Contains(
            smoothed.Points.Zip(underSixth.Points),
            pair => Math.Abs(pair.First.Y - pair.Second.Y) > 0.01);
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak)]
    [InlineData(ImpulseAmplitudeScale.Decibels)]
    public void ImpulseResponse_FramesTheTimeAxisAndOnlyTheDecibelFloor(
        ImpulseAmplitudeScale scale)
    {
        var ir = new Complex[8192];
        int peak = 1024;
        for (int i = 0; i < 2000 && peak + i < ir.Length; i++)
        {
            ir[peak + i] = new Complex(Math.Exp(-i / 200.0) * Math.Cos(i * 0.3), 0);
        }

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: 44_100, bits: 24, sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak));

        var options = new ImpulseResponseOptions { AmplitudeScale = scale, ShowImpulse = true };
        PlotModelFactory factory =
            CreateFactory(measurement, impulseOptions: options);

        var model = factory.CreateImpulseResponse(includeCurves: true);
        var series = (OxyPlot.Series.LineSeries)model.Series[0];
        var valueAxis = model.Axes.First(axis =>
            axis.Position == OxyPlot.Axes.AxisPosition.Left);
        var timeAxis = model.Axes.First(axis =>
            axis.Position == OxyPlot.Axes.AxisPosition.Bottom);

        // Only the dB floor is pinned: the impulse dives to the silence floor at every zero crossing.
        double expectedMaxY = series.Points.Max(point => point.Y);
        if (scale == ImpulseAmplitudeScale.Decibels)
        {
            Assert.Equal(expectedMaxY - 100.0, valueAxis.Minimum, precision: 9);
        }
        else
        {
            Assert.True(double.IsNaN(valueAxis.Minimum));
        }

        Assert.True(double.IsNaN(valueAxis.Maximum));

        double expectedMinX = series.Points.Min(point => point.X);
        double expectedMaxX = series.Points.Max(point => point.X);
        Assert.Equal(expectedMinX, timeAxis.AbsoluteMinimum, precision: 9);
        Assert.Equal(expectedMaxX, timeAxis.AbsoluteMaximum, precision: 9);
        // The view opens on peak + Length: a deconvolved record is mostly silence.
        double expectedVisibleMaxX = (peak + options.Length) * 1000.0 / 44_100.0;
        Assert.Equal(expectedMinX, timeAxis.Minimum, precision: 9);
        Assert.Equal(expectedVisibleMaxX, timeAxis.Maximum, precision: 9);
        Assert.True(timeAxis.Maximum < timeAxis.AbsoluteMaximum);
    }

    private static TestAnalyzer BandedCabin(
        double toneHz, int sampleRate = 48_000, int arrival = 480)
    {
        var ir = new Complex[16_384];
        for (int i = 0; i + arrival < ir.Length; i++)
        {
            double t = i / (double)sampleRate;
            ir[arrival + i] = new Complex(
                Math.Exp(-t * 400.0) * Math.Sin(2 * Math.PI * toneHz * t), 0);
        }

        int peak = 0;
        for (int i = 0; i < ir.Length; i++)
        {
            if (Math.Abs(ir[i].Real) > Math.Abs(ir[peak].Real))
            {
                peak = i;
            }
        }

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: sampleRate, bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak));
        return measurement;
    }

    private static string ImpulsePeakMarkerText(OxyPlot.PlotModel model) =>
        model.Annotations
            .OfType<OxyPlot.Annotations.LineAnnotation>()
            .Select(annotation => annotation.Text ?? string.Empty)
            .Single(text => text.Contains("peak"));

    [Fact]
    public void ImpulseResponse_StatesHowLateTheBandPeaksWhenTheDriverPlaysThere()
    {
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            var options = new ImpulseResponseOptions
            {
                BandFilterOctaves = 1.0,
                BandCenterHz = 250,
                TimeUnit = ImpulseTimeUnit.Milliseconds
            };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.Contains("band peak", text);
            Assert.Contains("ms after arrival", text);
        }
    }

    [Fact]
    public void ImpulseResponse_RefusesTheBandOffsetWhereTheDriverDoesNotPlay()
    {
        // Field case: at 63 Hz a tweeter's "band peak" is leakage landing seconds after the arrival.
        TestAnalyzer measurement = BandedCabin(8_000);
        using (measurement)
        {
            var options = new ImpulseResponseOptions
            {
                BandFilterOctaves = 1.0,
                BandCenterHz = 63,
                TimeUnit = ImpulseTimeUnit.Milliseconds
            };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.Contains("band peak", text);
            Assert.DoesNotContain("after arrival", text);
        }
    }

    [Fact]
    public void ImpulseResponse_OffersNoBandOffsetWithoutABandFilter()
    {
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            PlotModelFactory factory = CreateFactory(
                measurement, impulseOptions: new ImpulseResponseOptions());

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.DoesNotContain("band", text);
            Assert.DoesNotContain("after arrival", text);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ImpulseResponse_KeepsBothAxesForOverlaysCapturedOnTheOther(
        bool showImpulse, bool showStep)
    {
        // A series naming an axis the model lacks cannot bind, so hiding a trace must keep its axis.
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            var options = new ImpulseResponseOptions
            {
                ShowImpulse = showImpulse,
                ShowEnvelope = false,
                ShowStep = showStep
            };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);

            var model = factory.CreateImpulseResponse(includeCurves: true);

            Assert.Contains(model.Axes, axis => axis.Key == PlotModelFactory.ImpulseAxisKey);
            Assert.Contains(
                model.Axes, axis => axis.Key == PlotModelFactory.ImpulseStepAxisKey);
        }
    }

    [Theory]
    [InlineData(ImpulseAmplitudeScale.Linear)]
    [InlineData(ImpulseAmplitudeScale.PercentOfPeak)]
    [InlineData(ImpulseAmplitudeScale.Decibels)]
    public void ImpulseResponse_LevelAxisTakesInAnOverlayAttachedAfterTheBuild(
        ImpulseAmplitudeScale scale)
    {
        // Explicit Minimum/Maximum would win over the data range in OxyPlot and clip a louder overlay.
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            var options = new ImpulseResponseOptions { AmplitudeScale = scale };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);
            var model = factory.CreateImpulseResponse(includeCurves: true);
            var valueAxis = model.Axes.First(axis =>
                axis.Key == PlotModelFactory.ImpulseAxisKey);

            double liveMaximum = ((OxyPlot.Series.LineSeries)model.Series[0])
                .Points.Max(point => point.Y);
            double louder = scale switch
            {
                ImpulseAmplitudeScale.Decibels => 6.0,
                ImpulseAmplitudeScale.PercentOfPeak => 200.0,
                _ => liveMaximum * 2.0
            };
            var overlay = new OxyPlot.Series.LineSeries
            {
                YAxisKey = PlotModelFactory.ImpulseAxisKey
            };
            overlay.Points.Add(new OxyPlot.DataPoint(0, 0));
            overlay.Points.Add(new OxyPlot.DataPoint(1, louder));
            model.Series.Add(overlay);
            ((OxyPlot.IPlotModel)model).Update(true);

            Assert.True(
                valueAxis.ActualMaximum >= louder,
                $"axis stopped at {valueAxis.ActualMaximum}, overlay reaches {louder}");
        }
    }

    [Fact]
    public void ImpulseResponse_FramesOverlaysEvenWithEveryLiveTraceHidden()
    {
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            var options = new ImpulseResponseOptions
            {
                ShowImpulse = false,
                ShowEnvelope = false,
                ShowStep = false,
                TimeOrigin = ImpulseTimeOrigin.Peak,
                AmplitudeScale = ImpulseAmplitudeScale.PercentOfPeak
            };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);

            // Only an impulse model frames an overlay, with its own build's framing.
            Assert.Null(factory.ImpulseFrameOf(factory.CreateFrequencyResponse(includeCurves: true)));
            var model = factory.CreateImpulseResponse(includeCurves: true);
            ImpulseOverlayFrame frame = factory.ImpulseFrameOf(model)!.Value;

            Assert.Empty(model.Series);
            Assert.Equal(measurement.Result.Transfer!.PeakIndex, frame.OriginSamples, precision: 9);
            Assert.NotNull(frame.ReferencePeak);
            Assert.True(frame.ReferencePeak > 0.0);
            Assert.Equal(measurement.Result.SampleRate, frame.SampleRate);
            Assert.Same(options, frame.Options);
        }
    }

    [Fact]
    public void ImpulseResponse_PinsTheDecibelFloorButNotTheTop()
    {
        TestAnalyzer measurement = BandedCabin(250);
        using (measurement)
        {
            var options = new ImpulseResponseOptions
            {
                AmplitudeScale = ImpulseAmplitudeScale.Decibels
            };
            PlotModelFactory factory =
                CreateFactory(measurement, impulseOptions: options);

            var model = factory.CreateImpulseResponse(includeCurves: true);
            var valueAxis = model.Axes.First(axis =>
                axis.Key == PlotModelFactory.ImpulseAxisKey);
            var series = (OxyPlot.Series.LineSeries)model.Series[0];

            Assert.Equal(
                series.Points.Max(point => point.Y) - 100.0,
                valueAxis.Minimum,
                precision: 9);
            Assert.True(double.IsNaN(valueAxis.Maximum));
        }
    }

    [Fact]
    public void ImpulseResponse_DrawsTheWholeRecordNotJustTheDefaultView()
    {
        var ir = new Complex[8192];
        int peak = 1024;
        ir[peak] = Complex.One;
        ir[7000] = new Complex(0.2, 0);

        using var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: 44_100, bits: 24, sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak));

        var options = new ImpulseResponseOptions
        {
            Length = 2048,
            TimeUnit = ImpulseTimeUnit.Samples,
            ShowImpulse = true
        };
        PlotModelFactory factory =
            CreateFactory(measurement, impulseOptions: options);

        var model = factory.CreateImpulseResponse(includeCurves: true);
        var series = (OxyPlot.Series.LineSeries)model.Series[0];
        var timeAxis = model.Axes.First(axis =>
            axis.Position == OxyPlot.Axes.AxisPosition.Bottom);

        Assert.Equal(8192, series.Points.Count);
        Assert.Equal(0.2, series.Points[7000].Y, precision: 9);
        Assert.Equal(peak + options.Length, timeAxis.Maximum, precision: 9);
        Assert.Equal(8191, timeAxis.AbsoluteMaximum, precision: 9);
    }

    [Fact]
    public void AnalysisModes_RequireTransferIr_ShowAnnotationWhenAbsent()
    {
        using var measurement = CreateSweepOnlyMeasurement();
        PlotModelFactory factory = CreateFactory(measurement);

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
        PlotModelFactory factory = CreateFactory(measurement);

        Assert.NotEmpty(factory.CreateFrequencyResponse(includeCurves: true).Series);
    }

    [Fact]
    public void CreateFrequencyResponse_InSplMode_UsesTheSplAxisAndLimits()
    {
        TestAnalyzer measurement = CreateTransferMeasurement();
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
        measurement.Open(measurement.Result with { SplCalibration = anchor });
        measurement.Open(measurement.Result with { Levels = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(true, -6, -9, false, true)) });

        var splOptions = new FrequencyResponseOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        OxyPlot.PlotModel model = CreateFactory(
                measurement, frequencyResponseOptions: splOptions)
            .CreateFrequencyResponse(includeCurves: true);

        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Equal(PlotModelStyle.SplDecibelAbsoluteMaximum, dbAxis.AbsoluteMaximum);
    }

    [Fact]
    public void CreateFrequencyResponse_SplWithoutCalibration_IsViewOnly()
    {
        TestAnalyzer measurement = CreateTransferMeasurement();
        var splOptions = new FrequencyResponseOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        PlotModelFactory factory = CreateFactory(
            measurement, frequencyResponseOptions: splOptions);
        OxyPlot.PlotModel model = factory.CreateFrequencyResponse(includeCurves: true);

        // Without a calibration the axis stays SPL so dB SPL overlays can be viewed; own curves are omitted.
        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            factory.EffectiveFrequencyResponseScale);
        Assert.Empty(model.Series);
        var note = Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("overlays only", note.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFrequencyResponse_InSplMode_DrawsCompareWithItsOwnOffset()
    {
        const int peakSample = 64;
        TestAnalyzer measurement = CreateSplTransferMeasurement(
            peakSample, loopbackPeakDbFs: -6, referenceLevelDbSpl: 94, measuredLevelDbFs: -20);
        // K_main = -6 + (94 - -20) = 108 dB at 0 dBr.

        PlotModelFactory factory = CreateFactory(
            measurement,
            frequencyResponseOptions: new FrequencyResponseOptions
            {
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            });
        var compareImpulse = new Complex[2048];
        compareImpulse[peakSample] = Complex.One;
        factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
            "quieter.json", 44_100, compareImpulse, peakSample, SplOffsetDb: 96.0));

        List<LineSeries> series = factory.CreateFrequencyResponse(includeCurves: true)
            .Series.OfType<LineSeries>().ToList();
        LineSeries main = series.Single(item => item.Tag is CurveTag
        {
            Kind: AnalysisCurveKind.Primary,
            Source: CurveSource.Main
        });
        LineSeries compare = series.Single(item => item.Tag is CurveTag
        {
            Kind: AnalysisCurveKind.Primary,
            Source: CurveSource.Compare
        });

        Assert.Contains("quieter.json", compare.Title);
        Assert.Contains("dB SPL", compare.TrackerFormatString);
        Assert.Equal(main.Points.Count, compare.Points.Count);
        for (int i = 0; i < main.Points.Count; i++)
        {
            Assert.Equal(main.Points[i].X, compare.Points[i].X, tolerance: 1e-9);
            Assert.Equal(main.Points[i].Y - 12.0, compare.Points[i].Y, tolerance: 1e-9);
        }
    }

    [Fact]
    public void CreateFrequencyResponse_InSplMode_OmitsAnUncalibratedCompareAndSaysSo()
    {
        TestAnalyzer measurement = CreateSplTransferMeasurement(
            peakSample: 64, loopbackPeakDbFs: -6, referenceLevelDbSpl: 94,
            measuredLevelDbFs: -20);

        PlotModelFactory factory = CreateFactory(
            measurement,
            frequencyResponseOptions: new FrequencyResponseOptions
            {
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            });
        var compareImpulse = new Complex[2048];
        compareImpulse[64] = Complex.One;
        factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
            "uncalibrated.json", 44_100, compareImpulse, 64));

        OxyPlot.PlotModel model = factory.CreateFrequencyResponse(includeCurves: true);

        Assert.DoesNotContain(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag { Source: CurveSource.Compare });
        OverlayTextAnnotation note =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("uncalibrated.json", note.Text);
    }

    [Fact]
    public void CreateFrequencyResponse_SplViewOnly_StillDrawsACalibratedCompare()
    {
        TestAnalyzer measurement = CreateTransferMeasurement();

        PlotModelFactory factory = CreateFactory(
            measurement,
            frequencyResponseOptions: new FrequencyResponseOptions
            {
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            });
        var compareImpulse = new Complex[2048];
        compareImpulse[64] = Complex.One;
        factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
            "calibrated.json", 44_100, compareImpulse, 64, SplOffsetDb: 100.0));

        OxyPlot.PlotModel model = factory.CreateFrequencyResponse(includeCurves: true);

        Assert.DoesNotContain(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag { Source: CurveSource.Main });
        OverlayTextAnnotation note =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("overlays only", note.Text, StringComparison.OrdinalIgnoreCase);
        LineSeries compare = Assert.Single(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag { Source: CurveSource.Compare });
        Assert.All(
            compare.Points,
            point => Assert.Equal(100.0, point.Y, tolerance: 1e-6));
    }

    // The fundamental's level is read from the curve set, so it is computed even with the primary hidden.
    [Fact]
    public void CreateFrequencyResponse_InSplMode_LiftsHarmonicsWithThePrimaryHidden()
    {
        using TestAnalyzer measurement = CreateSplSweepWithSecondHarmonic();

        LineSeries SecondHarmonic(bool showPrimary)
        {
            OxyPlot.PlotModel model = CreateFactory(
                    measurement,
                    frequencyResponseVisibility: new CurveVisibilityOptions
                    {
                        ShowPrimary = showPrimary,
                        ShowHd3 = false,
                        ShowHd4 = false,
                        ShowThdPlusNoise = false,
                        ShowNoiseFloor = false,
                        ShowCoherence = false
                    },
                    frequencyResponseOptions: new FrequencyResponseOptions
                    {
                        MagnitudeScale = MagnitudeScale.SoundPressureLevel
                    })
                .CreateFrequencyResponse(includeCurves: true);

            Assert.Equal(
                showPrimary,
                model.Series.OfType<LineSeries>().Any(
                    item => item.Tag is CurveTag { Kind: AnalysisCurveKind.Primary }));
            return Assert.Single(
                model.Series.OfType<LineSeries>(),
                item => item.Tag is CurveTag { Kind: AnalysisCurveKind.SecondHarmonic });
        }

        LineSeries shown = SecondHarmonic(showPrimary: true);
        LineSeries hidden = SecondHarmonic(showPrimary: false);

        Assert.NotEmpty(hidden.Points);
        Assert.Equal(shown.Points.Count, hidden.Points.Count);
        bool anyFinite = false;
        for (int i = 0; i < shown.Points.Count; i++)
        {
            Assert.Equal(shown.Points[i].X, hidden.Points[i].X, tolerance: 1e-9);
            if (double.IsNaN(shown.Points[i].Y))
            {
                Assert.True(double.IsNaN(hidden.Points[i].Y));
                continue;
            }

            anyFinite = true;
            Assert.Equal(shown.Points[i].Y, hidden.Points[i].Y, tolerance: 1e-9);
            // Lifted HD2 sits at K + dBc; the bare dBc value (about -34 dB) would be below the floor.
            Assert.True(
                hidden.Points[i].Y > PlotModelStyle.SplDecibelMinimum,
                $"HD2 at {hidden.Points[i].X:0.#} Hz reads {hidden.Points[i].Y:0.0}, " +
                "which is not an absolute level");
        }

        Assert.True(anyFinite, "the synthetic HD2 packet produced no usable points");
    }

    // A harmonic below the noise floor leaves flat noise that the peak-relative edge test misread as a leak.
    [Fact]
    public void CreateFrequencyResponse_HarmonicsBelowTheNoiseFloor_GetANeutralNote()
    {
        using TestAnalyzer measurement = CreateSweepWithNoiseFloorOnly();

        OxyPlot.PlotModel model = CreateFactory(measurement)
            .CreateFrequencyResponse(includeCurves: true);

        Assert.DoesNotContain(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag
            {
                Kind: AnalysisCurveKind.SecondHarmonic
                    or AnalysisCurveKind.ThirdHarmonic
                    or AnalysisCurveKind.FourthHarmonic
            });
        OverlayTextAnnotation note =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("below the measurement noise floor", note.Text);
        Assert.DoesNotContain("overlaps", note.Text);
        Assert.Equal(UiPalette.CurveMuted.ToOxy(), note.TextColor);
    }

    [Fact]
    public void CreateFrequencyResponse_AGenuineOverlap_KeepsTheAmberWarning()
    {
        EssSweepMetadata sweep = NoiseFixtureSweep();
        HarmonicWindowDefinition h2 = EssHarmonicAnalysis.BuildWindow(sweep, 2, 0.5);

        Complex[] deconvolution = NoisyCleanDeconvolution(sweep);
        for (int i = h2.PeakSample; i <= h2.EndSample && i < deconvolution.Length; i++)
        {
            deconvolution[i] = new Complex(0.3 * Math.Cos(0.3 * (i - h2.PeakSample)), 0.0);
        }

        using TestAnalyzer measurement = CreateSweepMeasurement(deconvolution, sweep);

        OxyPlot.PlotModel model = CreateFactory(measurement)
            .CreateFrequencyResponse(includeCurves: true);

        List<OverlayTextAnnotation> notes =
            model.Annotations.OfType<OverlayTextAnnotation>().ToList();
        Assert.Equal(2, notes.Count);
        OverlayTextAnnotation amber =
            Assert.Single(notes, n => n.TextColor == UiPalette.Warning.ToOxy());
        Assert.Contains("HD2", amber.Text);
        Assert.Contains("overlaps its neighbour", amber.Text);
        Assert.DoesNotContain("HD3", amber.Text);
        OverlayTextAnnotation gray = Assert.Single(notes, n => n.TextColor == UiPalette.CurveMuted.ToOxy());
        Assert.Contains("HD3", gray.Text);
        Assert.Contains("HD4", gray.Text);
        Assert.Contains("below the measurement noise floor", gray.Text);
    }

    [Fact]
    public void CreateFrequencyResponse_InRelativeMode_LeavesRoomForAPaddedLoopback()
    {
        TestAnalyzer measurement = CreateTransferMeasurement();

        OxyPlot.PlotModel model = CreateFactory(measurement)
            .CreateFrequencyResponse(includeCurves: true);

        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dBr/dBc", dbAxis.Title);
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, dbAxis.Maximum);
        // Padding the loopback lifts the relative curve by the pad; the clamp must leave room.
        Assert.True(
            dbAxis.AbsoluteMaximum >= 40,
            $"the dBr ceiling of {dbAxis.AbsoluteMaximum} dB cannot show a padded loopback");
    }

    [Fact]
    public void CreateFrequencyResponse_OpensTheViewOnAPaddedResponse()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = new Complex(10.0, 0.0);
        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64));

        OxyPlot.PlotModel padded = CreateFactory(measurement)
            .CreateFrequencyResponse(includeCurves: true);
        var paddedAxis = (OxyPlot.Axes.LinearAxis)padded.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal(30, paddedAxis.Maximum);

        OxyPlot.PlotModel normal = CreateFactory(CreateTransferMeasurement())
            .CreateFrequencyResponse(includeCurves: true);
        var normalAxis = (OxyPlot.Axes.LinearAxis)normal.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, normalAxis.Maximum);
    }

    private static TestAnalyzer CreateSweepOnlyMeasurement()
    {
        var sweep = new Complex[2048];
        sweep[64] = Complex.One;

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: sweep,
            sweepDeconvolutionPeakIndex: 64));
        return measurement;
    }

    private static TestAnalyzer CreateTransferMeasurement(int peakSample = 64)
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[peakSample] = Complex.One;
        return CreateTransferMeasurement(transferImpulse, peakSample, 44_100);
    }

    private static TestAnalyzer CreateTransferMeasurement(
        Complex[] transferImpulse, int peakSample, int sampleRate)
    {

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: sampleRate,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: peakSample,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: peakSample,
            // Stated achieved band: a regenerated 1 s sweep would reach full amplitude inside the grid and mask the points.
            achievedLowFrequencyHz: 20,
            achievedHighFrequencyHz: 20_000));
        return measurement;
    }

    // K = loopbackPeakDbFs + (referenceLevelDbSpl - measuredLevelDbFs).
    private static TestAnalyzer CreateSplTransferMeasurement(
        int peakSample,
        double loopbackPeakDbFs,
        double referenceLevelDbSpl,
        double measuredLevelDbFs)
    {
        TestAnalyzer measurement = CreateTransferMeasurement(peakSample);
        var anchor = new SplCalibration
        {
            ReferenceLevelDbSpl = referenceLevelDbSpl,
            MeasuredLevelDbFs = measuredLevelDbFs,
            Backend = Resonalyze.Audio.AudioBackend.Wave,
            SampleRate = 44_100,
            Bits = 24,
            MicrophoneChannelOffset = 0,
            InputDeviceNumber = -1
        };
        measurement.Open(measurement.Result with { SplCalibration = anchor });
        measurement.Open(measurement.Result with { Levels = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(
                true, loopbackPeakDbFs, loopbackPeakDbFs - 3, false, false)) });
        return measurement;
    }

    // Flat |H1|, flat -34 dBc HD2 and a 0 dBr primary: every SPL level is K (108 dB) plus the curve's dB.
    private static TestAnalyzer CreateSplSweepWithSecondHarmonic()
    {
        const int sampleRate = 48_000;
        const int octaves = 10;
        const int sweepSamples = 200_000;
        const int peakIndex = 150_000;
        const int transferPeak = 64;
        EssSweepMetadata sweep = EssSweepMetadata.FromExponentialSweep(
            sampleRate, octaves, sweepSamples, peakIndex);

        var deconvolution = new Complex[sweepSamples];
        deconvolution[peakIndex] = Complex.One;
        deconvolution[peakIndex - EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2)] =
            new Complex(0.02, 0.0);
        var transferImpulse = new Complex[2048];
        transferImpulse[transferPeak] = Complex.One;

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: sweep.StartFrequencyHz,
            highFrequencyHz: sweep.EndFrequencyHz,
            sampleRate: sampleRate,
            bits: 24,
            sweepDurationSeconds: sweepSamples / (double)sampleRate,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: deconvolution,
            sweepDeconvolutionPeakIndex: peakIndex,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: transferPeak,
            achievedLowFrequencyHz: sweep.StartFrequencyHz,
            achievedHighFrequencyHz: sweep.EndFrequencyHz));

        var anchor = new SplCalibration
        {
            ReferenceLevelDbSpl = 94,
            MeasuredLevelDbFs = -20,
            Backend = Resonalyze.Audio.AudioBackend.Wave,
            SampleRate = sampleRate,
            Bits = 24,
            MicrophoneChannelOffset = 0,
            InputDeviceNumber = -1
        };
        measurement.Open(measurement.Result with { SplCalibration = anchor });
        measurement.Open(measurement.Result with { Levels = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(true, -6, -9, false, false)) });
        return measurement;
    }

    private static EssSweepMetadata NoiseFixtureSweep() =>
        EssSweepMetadata.FromExponentialSweep(
            sampleRate: 48_000, octaves: 10, sweepSampleCount: 200_000,
            deconvolutionPeakIndex: 150_000);

    private static Complex[] NoisyCleanDeconvolution(EssSweepMetadata sweep)
    {
        var random = new Random(9241);
        var deconvolution = new Complex[sweep.SweepSampleCount];
        for (int i = 0; i < deconvolution.Length; i++)
        {
            // Sum of 12 uniforms minus 6: zero-mean, unit-variance, deterministic.
            double gaussian = -6.0;
            for (int k = 0; k < 12; k++)
            {
                gaussian += random.NextDouble();
            }
            deconvolution[i] = new Complex(1e-4 * gaussian, 0.0);
        }

        deconvolution[sweep.DeconvolutionPeakIndex] = Complex.One;
        return deconvolution;
    }

    private static TestAnalyzer CreateSweepWithNoiseFloorOnly()
    {
        EssSweepMetadata sweep = NoiseFixtureSweep();
        return CreateSweepMeasurement(NoisyCleanDeconvolution(sweep), sweep);
    }

    private static TestAnalyzer CreateSweepMeasurement(
        Complex[] deconvolution, EssSweepMetadata sweep)
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = Complex.One;

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: sweep.StartFrequencyHz,
            highFrequencyHz: sweep.EndFrequencyHz,
            sampleRate: (int)sweep.SampleRateHz,
            bits: 24,
            sweepDurationSeconds: sweep.DurationSeconds,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: deconvolution,
            sweepDeconvolutionPeakIndex: sweep.DeconvolutionPeakIndex,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64,
            achievedLowFrequencyHz: sweep.StartFrequencyHz,
            achievedHighFrequencyHz: sweep.EndFrequencyHz));
        return measurement;
    }

    private static TestAnalyzer CreateTransferMeasurementWithCoherence()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = Complex.One;
        double[] coherence = new double[1025];
        Array.Fill(coherence, 0.9);

        var measurement = new TestAnalyzer();
        measurement.Open(TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: 64,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: 64,
            transferCoherence: coherence));
        return measurement;
    }

    private static PlotModelFactory CreateFactory(
        TestAnalyzer measurement,
        ImpulseResponseOptions? impulseOptions = null,
        FrequencyResponseOptions? groupDelayOptions = null,
        FrequencyResponseOptions? phaseResponseOptions = null,
        CurveVisibilityOptions? frequencyResponseVisibility = null,
        CurveVisibilityOptions? phaseResponseVisibility = null,
        CurveVisibilityOptions? groupDelayVisibility = null,
        FrequencyResponseOptions? frequencyResponseOptions = null,
        LiveSpectrumOptions? liveSpectrumOptions = null,
        CalibrationFile? calibration = null,
        Func<string?, CalibrationFile?>? calibrationsById = null)
    {
        string calibrationPath = Path.Combine(
            Path.GetTempPath(),
            $"resonalyze-calibration-{Guid.NewGuid():N}.txt");

        return new PlotModelFactory(
            measurement.Document,
            measurement.Engine,
            id => calibrationsById != null
                ? calibrationsById(id)
                : calibration ?? new CalibrationFile(calibrationPath),
            new AnalyzerViewSettings
            {
                FrequencyResponse = frequencyResponseOptions ?? new FrequencyResponseOptions(),
                PhaseResponse = phaseResponseOptions ?? new FrequencyResponseOptions(),
                GroupDelay = groupDelayOptions ?? new FrequencyResponseOptions(),
                FrequencyResponseVisibility = frequencyResponseVisibility ?? new CurveVisibilityOptions(),
                PhaseResponseVisibility = phaseResponseVisibility ?? new CurveVisibilityOptions(),
                GroupDelayVisibility = groupDelayVisibility ?? new CurveVisibilityOptions(),
                ImpulseResponse = impulseOptions ?? new ImpulseResponseOptions(),
                LiveSpectrum = liveSpectrumOptions ?? new LiveSpectrumOptions(),
                Waterfall = new WaterfallGenerateOptions(),
                BurstDecay = new WaterfallGenerateOptions()
            });
    }
}
