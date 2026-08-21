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
        var groupDelayVisibility = new CurveVisibilityOptions
        {
            ShowGroupDelay = false,
            ShowMinimumPhaseGroupDelay = false,
            ShowExcessGroupDelay = false
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, groupDelayVisibility: groupDelayVisibility);

        Assert.Empty(factory.CreateGroupDelay(includeCurves: true).Series);

        groupDelayVisibility.ShowGroupDelay = true;
        Assert.NotEmpty(factory.CreateGroupDelay(includeCurves: true).Series);
    }

    [Fact]
    public void GroupDelay_MinimumAndExcessCurves_FollowTheirFlags()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        var groupDelayVisibility = new CurveVisibilityOptions
        {
            ShowGroupDelay = true,
            ShowMinimumPhaseGroupDelay = true,
            ShowExcessGroupDelay = true
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, groupDelayVisibility: groupDelayVisibility);

        List<CurveTag> tags = factory.CreateGroupDelay(includeCurves: true).Series
            .OfType<LineSeries>()
            .Select(series => series.Tag)
            .OfType<CurveTag>()
            .ToList();
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.Primary);
        Assert.Contains(
            tags, tag => tag.Kind == AnalysisCurveKind.MinimumPhaseGroupDelay);
        Assert.Contains(tags, tag => tag.Kind == AnalysisCurveKind.ExcessGroupDelay);

        // Each curve follows its own flag: hiding the measured curve must not
        // take the minimum/excess pair down with it, and vice versa.
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
        // A ~5.4 ms arrival — the typical car-audio scale, where the measured
        // range (±2 ms pad) no longer straddles zero on its own. The fit must
        // follow the measured absolute level even when only the excess is shown
        // (in band the excess tracks it), must extend to zero when the minimum
        // curve is shown (it lives at ≈ 0), and must never read the pair's own
        // point values (their band-edge cepstral swings would wreck the scale).
        using var measurement = CreateTransferMeasurement(peakSample: 240);
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());

        OxyPlot.Axes.Axis AxisOf(bool showMeasured, bool showMinimum, bool showExcess)
        {
            var visibility = new CurveVisibilityOptions
            {
                ShowGroupDelay = showMeasured,
                ShowMinimumPhaseGroupDelay = showMinimum,
                ShowExcessGroupDelay = showExcess
            };
            PlotModelFactory factory = CreateFactory(
                measurement, noiseMeasurement, groupDelayVisibility: visibility);
            return factory.CreateGroupDelay(includeCurves: true).Axes
                .First(axis => axis.Key == PlotModelFactory.GroupDelayAxisKey);
        }

        // All three curves: the ~5.4 ms measured level AND the ≈ 0 minimum
        // curve must both land inside the fitted range.
        OxyPlot.Axes.Axis allThree = AxisOf(
            showMeasured: true, showMinimum: true, showExcess: true);
        Assert.True(
            allThree.Minimum < 0.2,
            $"the minimum curve is clipped out (axis starts at {allThree.Minimum:0.00} ms)");
        Assert.True(
            allThree.Maximum > 5.0,
            $"the measured level is clipped out (axis ends at {allThree.Maximum:0.00} ms)");

        // Excess only (measured hidden): the axis still follows the measured
        // absolute level, where the in-band excess actually lives — not the
        // −5…+5 default that would push an 8–10 ms system off screen.
        OxyPlot.Axes.Axis excessOnly = AxisOf(
            showMeasured: false, showMinimum: false, showExcess: true);
        Assert.True(
            excessOnly.Maximum > 5.0,
            $"the excess curve is clipped out (axis ends at {excessOnly.Maximum:0.00} ms)");
        Assert.True(
            excessOnly.Minimum < 5.0,
            $"the excess curve is clipped out (axis starts at {excessOnly.Minimum:0.00} ms)");

        // Measured + excess without the minimum curve: no zero extension — the
        // range stays tight around the arrival.
        OxyPlot.Axes.Axis withoutMinimum = AxisOf(
            showMeasured: true, showMinimum: false, showExcess: true);
        Assert.True(
            withoutMinimum.Minimum > 2.0,
            $"zero was pinned with no minimum curve shown ({withoutMinimum.Minimum:0.00} ms)");

        // Minimum alone: the default −5…+5 window already contains the curve.
        OxyPlot.Axes.Axis minimumOnly = AxisOf(
            showMeasured: false, showMinimum: true, showExcess: false);
        Assert.Equal(-5.0, minimumOnly.Minimum);
        Assert.Equal(5.0, minimumOnly.Maximum);
    }

    [Fact]
    public void GroupDelay_CompareSource_GetsMinimumAndExcessCurvesToo()
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

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

    // An imported recording's time origin is its own arrival, so nothing that
    // reads absolute time may be drawn beside a measurement that was referenced
    // to a captured loopback. The minimum-phase curve is not such a statement —
    // it is reconstructed from the gated magnitude and carries no bulk delay by
    // construction — so it stays comparable, and hiding it would be hiding valid
    // data rather than avoiding a wrong number.
    [Theory]
    [InlineData(TimingReference.SynchronizedLoopback, true)]
    [InlineData(TimingReference.RecordedSweep, false)]
    public void GroupDelay_TheTimeReadingCompareCurvesNeedOneClock(
        TimingReference compareTiming,
        bool sharesTheClock)
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

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
    [InlineData(TimingReference.SynchronizedLoopback, true)]
    [InlineData(TimingReference.RecordedSweep, false)]
    public void PhaseResponse_TheTimeReadingCompareCurvesNeedOneClock(
        TimingReference compareTiming,
        bool sharesTheClock)
    {
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        PlotModelFactory factory = CreateFactory(measurement, noiseMeasurement);

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
            CalibrationId = MicrophoneCalibrationIds.ZeroDegrees,
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

    [Fact]
    public void ComplexSumLoss_WithAnExplicitWidth_IgnoresThePlotsOwnSmoothing()
    {
        // An overlay slot asks for the loss at ITS width. The plot's smoothing must
        // not reach the curve at all: it neither bakes into the operands (they are
        // divided unsmoothed) nor smooths the ratio, so switching the plot from
        // psychoacoustic to 1/6 octave leaves the slot's curve bit-identical — and
        // a slot set to Off gets a genuinely unsmoothed ratio.
        using var measurement = CreateTransferMeasurement();
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        var options = new FrequencyResponseOptions
        {
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode
        };
        PlotModelFactory factory = CreateFactory(
            measurement, noiseMeasurement, frequencyResponseOptions: options);
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

        // And the width that IS asked for still acts: the same curve smoothed
        // psychoacoustically differs from the unsmoothed one.
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
    public void ImpulseResponse_LocksValueAxisToCurveRange(ImpulseAmplitudeScale scale)
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
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: 44_100, bits: 24, sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak);

        var options = new ImpulseResponseOptions { AmplitudeScale = scale, ShowImpulse = true };
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
        // The VISIBLE range opens on the drawn curve in every scale; the absolute bounds
        // are deliberately left free, because an overlay attached after the build can
        // legitimately re-frame above it (see
        // ImpulseResponse_LeavesTheLevelAxisFreeToWidenForALouderOverlay).
        Assert.Equal(expectedMaxY, valueAxis.Maximum, precision: 9);
        // ...but the dB view OPENS on a 100 dB window: the impulse dives to the
        // deconvolution silence floor at every zero crossing, and fitting the visible
        // range to that spends most of the plot on arithmetic nobody reads.
        double expectedVisibleMinY = scale == ImpulseAmplitudeScale.Decibels
            ? Math.Max(expectedMinY, expectedMaxY - 100.0)
            : expectedMinY;
        Assert.Equal(expectedVisibleMinY, valueAxis.Minimum, precision: 9);

        // The time axis can REACH the whole record — the traces are built whole, so
        // zooming out ends at the end of the tail rather than at a length setting...
        double expectedMinX = series.Points.Min(point => point.X);
        double expectedMaxX = series.Points.Max(point => point.X);
        Assert.Equal(expectedMinX, timeAxis.AbsoluteMinimum, precision: 9);
        Assert.Equal(expectedMaxX, timeAxis.AbsoluteMaximum, precision: 9);
        // ...while it OPENS on the peak plus the Length tail, because a deconvolved
        // record is mostly silence and opening on all of it draws the response as one
        // vertical line.
        double expectedVisibleMaxX = (peak + options.Length) * 1000.0 / 44_100.0;
        Assert.Equal(expectedMinX, timeAxis.Minimum, precision: 9);
        Assert.Equal(expectedVisibleMaxX, timeAxis.Maximum, precision: 9);
        Assert.True(timeAxis.Maximum < timeAxis.AbsoluteMaximum);
    }

    private static (ExpSweepMeasurement Measurement, NoiseMeasurement Noise) BandedCabin(
        double toneHz, int sampleRate = 48_000, int arrival = 480)
    {
        // A decaying tone: its dominant band sits around toneHz, which is what decides
        // whether a band reading is offered at all.
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

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: sampleRate, bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak);
        return (measurement, noise);
    }

    private static string ImpulsePeakMarkerText(OxyPlot.PlotModel model) =>
        model.Annotations
            .OfType<OxyPlot.Annotations.LineAnnotation>()
            .Select(annotation => annotation.Text ?? string.Empty)
            .Single(text => text.Contains("peak"));

    [Fact]
    public void ImpulseResponse_StatesHowLateTheBandPeaksWhenTheDriverPlaysThere()
    {
        (ExpSweepMeasurement measurement, NoiseMeasurement noise) = BandedCabin(250);
        using (measurement)
        using (noise)
        {
            var options = new ImpulseResponseOptions
            {
                BandFilterOctaves = 1.0,
                BandCenterHz = 250,
                TimeUnit = ImpulseTimeUnit.Milliseconds
            };
            PlotModelFactory factory =
                CreateFactory(measurement, noise, impulseOptions: options);

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.Contains("band peak", text);
            Assert.Contains("ms after arrival", text);
        }
    }

    [Fact]
    public void ImpulseResponse_RefusesTheBandOffsetWhereTheDriverDoesNotPlay()
    {
        // The field case this guard exists for: at 63 Hz a tweeter still has a "band
        // peak", and across the archived cabins it landed seconds after the arrival
        // because what peaked was leakage.
        (ExpSweepMeasurement measurement, NoiseMeasurement noise) = BandedCabin(8_000);
        using (measurement)
        using (noise)
        {
            var options = new ImpulseResponseOptions
            {
                BandFilterOctaves = 1.0,
                BandCenterHz = 63,
                TimeUnit = ImpulseTimeUnit.Milliseconds
            };
            PlotModelFactory factory =
                CreateFactory(measurement, noise, impulseOptions: options);

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.Contains("band peak", text);
            Assert.DoesNotContain("after arrival", text);
        }
    }

    [Fact]
    public void ImpulseResponse_OffersNoBandOffsetWithoutABandFilter()
    {
        (ExpSweepMeasurement measurement, NoiseMeasurement noise) = BandedCabin(250);
        using (measurement)
        using (noise)
        {
            PlotModelFactory factory = CreateFactory(
                measurement, noise, impulseOptions: new ImpulseResponseOptions());

            string text = ImpulsePeakMarkerText(
                factory.CreateImpulseResponse(includeCurves: true));

            Assert.DoesNotContain("band", text);
            Assert.DoesNotContain("after arrival", text);
        }
    }

    [Theory]
    [InlineData(true, false)]   // impulse only: the step axis must still exist
    [InlineData(false, true)]   // step only: the level axis must still exist
    public void ImpulseResponse_KeepsBothAxesForOverlaysCapturedOnTheOther(
        bool showImpulse, bool showStep)
    {
        // An overlay carries the axis key it was captured with, and a series naming an
        // axis the model does not have cannot bind — so switching a trace off must not
        // take its axis out of the model.
        (ExpSweepMeasurement measurement, NoiseMeasurement noise) = BandedCabin(250);
        using (measurement)
        using (noise)
        {
            var options = new ImpulseResponseOptions
            {
                ShowImpulse = showImpulse,
                ShowEnvelope = false,
                ShowStep = showStep
            };
            PlotModelFactory factory =
                CreateFactory(measurement, noise, impulseOptions: options);

            var model = factory.CreateImpulseResponse(includeCurves: true);

            Assert.Contains(model.Axes, axis => axis.Key == PlotModelFactory.ImpulseAxisKey);
            Assert.Contains(
                model.Axes, axis => axis.Key == PlotModelFactory.ImpulseStepAxisKey);
        }
    }

    [Fact]
    public void ImpulseResponse_LeavesTheLevelAxisFreeToWidenForALouderOverlay()
    {
        // Overlays join the model after it is built. A snapshot from a louder record
        // re-frames above the live curve on purpose, and a pinned absolute bound would
        // clip exactly the difference the shared normalization exists to show.
        (ExpSweepMeasurement measurement, NoiseMeasurement noise) = BandedCabin(250);
        using (measurement)
        using (noise)
        {
            PlotModelFactory factory = CreateFactory(
                measurement, noise, impulseOptions: new ImpulseResponseOptions());

            var model = factory.CreateImpulseResponse(includeCurves: true);
            var valueAxis = model.Axes.First(axis =>
                axis.Key == PlotModelFactory.ImpulseAxisKey);
            var timeAxis = model.Axes.First(axis =>
                axis.Position == OxyPlot.Axes.AxisPosition.Bottom);

            Assert.Equal(double.MaxValue, valueAxis.AbsoluteMaximum);
            Assert.Equal(double.MinValue, valueAxis.AbsoluteMinimum);
            // Time stays bounded by the record, which is a real limit.
            Assert.True(double.IsFinite(timeAxis.AbsoluteMaximum));
        }
    }

    [Fact]
    public void ImpulseResponse_DrawsTheWholeRecordNotJustTheDefaultView()
    {
        var ir = new Complex[8192];
        int peak = 1024;
        ir[peak] = Complex.One;
        // A late reflection well past peak + Length: it must exist in the curve, or no
        // amount of zooming out could ever bring it on screen.
        ir[7000] = new Complex(0.2, 0);

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        using var noiseMeasurement = new NoiseMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000, sampleRate: 44_100, bits: 24, sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: ir, sweepDeconvolutionPeakIndex: peak,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: ir, transferPeakIndex: peak);

        var options = new ImpulseResponseOptions
        {
            Length = 2048,
            TimeUnit = ImpulseTimeUnit.Samples,
            ShowImpulse = true
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noiseMeasurement, impulseOptions: options);

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
        // Curves sit near 40–110 dB, so the axis must reach well above the
        // relative one's ceiling or the plot would be blank.
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Equal(PlotModelStyle.SplDecibelAbsoluteMaximum, dbAxis.AbsoluteMaximum);
    }

    [Fact]
    public void CreateFrequencyResponse_SplWithoutCalibration_IsViewOnly()
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());
        var splOptions = new FrequencyResponseOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        PlotModelFactory factory = CreateFactory(
            measurement, noise, frequencyResponseOptions: splOptions);
        OxyPlot.PlotModel model = factory.CreateFrequencyResponse(includeCurves: true);

        // SPL was requested without a calibration. The axis used to fall back to
        // dBr/dBc, which made SPL unreachable before the first calibrated run; now
        // it stays SPL so overlays captured in dB SPL can at least be viewed...
        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        // ...and the overlay gate follows the axis, or those overlays stay hidden...
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel,
            factory.EffectiveFrequencyResponseScale);
        // ...while the measurement's own curves are omitted — their dBr shapes would
        // read as absolute levels — replaced by the explanatory annotation.
        Assert.Empty(model.Series);
        var note = Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("overlays only", note.Text, StringComparison.OrdinalIgnoreCase);
    }

    // The Compare curve used to vanish the moment the plot went to dB SPL. It has
    // its own K (own loopback level, own anchor), so it belongs on the absolute
    // axis — placed by that K, not by the main measurement's.
    [Fact]
    public void CreateFrequencyResponse_InSplMode_DrawsCompareWithItsOwnOffset()
    {
        const int peakSample = 64;
        ExpSweepMeasurement measurement = CreateSplTransferMeasurement(
            peakSample, loopbackPeakDbFs: -6, referenceLevelDbSpl: 94, measuredLevelDbFs: -20);
        // K_main = -6 + (94 - -20) = 108 dB at 0 dBr.
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        PlotModelFactory factory = CreateFactory(
            measurement,
            noise,
            frequencyResponseOptions: new FrequencyResponseOptions
            {
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            });
        // The same impulse on the Compare side, so the two dBr shapes are identical
        // and the only thing separating the curves on screen is K_compare - K_main.
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

    // Without an anchor of its own the compared measurement has no absolute level;
    // drawing its dBr shape on a dB SPL axis would be a lie, so it stays out — but
    // the plot has to name it, or the curve just silently disappears (which is how
    // this was reported).
    [Fact]
    public void CreateFrequencyResponse_InSplMode_OmitsAnUncalibratedCompareAndSaysSo()
    {
        ExpSweepMeasurement measurement = CreateSplTransferMeasurement(
            peakSample: 64, loopbackPeakDbFs: -6, referenceLevelDbSpl: 94,
            measuredLevelDbFs: -20);
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        PlotModelFactory factory = CreateFactory(
            measurement,
            noise,
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

    // Mirrors the overlay rule: an SPL-capable source stays visible even when the
    // measurement itself cannot supply SPL, so the view-only plot is not empty when
    // there is something honest to show.
    [Fact]
    public void CreateFrequencyResponse_SplViewOnly_StillDrawsACalibratedCompare()
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        PlotModelFactory factory = CreateFactory(
            measurement,
            noise,
            frequencyResponseOptions: new FrequencyResponseOptions
            {
                MagnitudeScale = MagnitudeScale.SoundPressureLevel
            });
        var compareImpulse = new Complex[2048];
        compareImpulse[64] = Complex.One;
        factory.SetCompareSourceProvider(() => new CompareAnalysisSource(
            "calibrated.json", 44_100, compareImpulse, 64, SplOffsetDb: 100.0));

        OxyPlot.PlotModel model = factory.CreateFrequencyResponse(includeCurves: true);

        // The measurement's own curves stay out, with the notice explaining why...
        Assert.DoesNotContain(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag { Source: CurveSource.Main });
        OverlayTextAnnotation note =
            Assert.Single(model.Annotations.OfType<OverlayTextAnnotation>());
        Assert.Contains("overlays only", note.Text, StringComparison.OrdinalIgnoreCase);
        // ...while the compared measurement, which does carry an anchor, is drawn.
        LineSeries compare = Assert.Single(
            model.Series.OfType<LineSeries>(),
            item => item.Tag is CurveTag { Source: CurveSource.Compare });
        // A unit impulse is 0 dBr at every frequency, so the trace sits at K.
        Assert.All(
            compare.Points,
            point => Assert.Equal(100.0, point.Y, tolerance: 1e-6));
    }

    // Unchecking the primary used to take HD2/HD3/THD/noise with it on the absolute
    // axis: the lift reads the fundamental's level out of the curve set, so with the
    // primary never computed the harmonics silently stayed in dBc — tens of dB below
    // the SPL window, i.e. gone. The reference is computed either way now; only the
    // drawing follows the checkbox.
    [Fact]
    public void CreateFrequencyResponse_InSplMode_LiftsHarmonicsWithThePrimaryHidden()
    {
        using ExpSweepMeasurement measurement = CreateSplSweepWithSecondHarmonic();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        LineSeries SecondHarmonic(bool showPrimary)
        {
            OxyPlot.PlotModel model = CreateFactory(
                    measurement,
                    noise,
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

            // Computed as the anchor is not drawn as a curve: hidden stays hidden.
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
                // Above Nyquist/2 the harmonic is unobservable in both plots.
                Assert.True(double.IsNaN(hidden.Points[i].Y));
                continue;
            }

            anyFinite = true;
            Assert.Equal(shown.Points[i].Y, hidden.Points[i].Y, tolerance: 1e-9);
            // The unit impulse makes the primary 0 dBr, so the lifted HD2 sits at
            // K + its dBc value: well inside the SPL window, where the bare dBc
            // value it used to keep (about -34 dB here) is far below the floor.
            Assert.True(
                hidden.Points[i].Y > PlotModelStyle.SplDecibelMinimum,
                $"HD2 at {hidden.Points[i].X:0.#} Hz reads {hidden.Points[i].Y:0.0}, " +
                "which is not an absolute level");
        }

        Assert.True(anyFinite, "the synthetic HD2 packet produced no usable points");
    }

    [Fact]
    public void CreateFrequencyResponse_InRelativeMode_LeavesRoomForAPaddedLoopback()
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        OxyPlot.PlotModel model = CreateFactory(measurement, noise)
            .CreateFrequencyResponse(includeCurves: true);

        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dBr/dBc", dbAxis.Title);
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, dbAxis.Maximum);
        // The relative axis is a ratio to the reference, so attenuating the
        // loopback — which is the recommended fix for an overdriven reference
        // input — lifts the whole curve by the pad. The clamp has to leave room
        // for that instead of pinning the view just above unity.
        Assert.True(
            dbAxis.AbsoluteMaximum >= 40,
            $"the dBr ceiling of {dbAxis.AbsoluteMaximum} dB cannot show a padded loopback");
    }

    // Raising the CLAMP made a padded response pannable; the default view must
    // also open on it. A transfer IR with 20 dB of gain (a 20 dB pad in the
    // loopback) draws near +20 dBr — the initial window has to rise to show
    // it, while a normal response keeps the familiar -90..0 view.
    [Fact]
    public void CreateFrequencyResponse_OpensTheViewOnAPaddedResponse()
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[64] = new Complex(10.0, 0.0);
        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
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
            transferPeakIndex: 64);
        using var noise = new NoiseMeasurement(new FakeAudioSessionFactory());

        OxyPlot.PlotModel padded = CreateFactory(measurement, noise)
            .CreateFrequencyResponse(includeCurves: true);
        var paddedAxis = (OxyPlot.Axes.LinearAxis)padded.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        // ~+20 dBr data raised to the next grid line with at least 5 dB of
        // headroom.
        Assert.Equal(30, paddedAxis.Maximum);

        OxyPlot.PlotModel normal = CreateFactory(CreateTransferMeasurement(), noise)
            .CreateFrequencyResponse(includeCurves: true);
        var normalAxis = (OxyPlot.Axes.LinearAxis)normal.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        // A unity response opens on the familiar window, exactly as before.
        Assert.Equal(PlotModelStyle.RelativeDecibelMaximum, normalAxis.Maximum);
    }

    // The same headroom on the live analyzer's native dB axis: it is the same
    // loopback-referenced ratio, measured on the same wiring.
    [Fact]
    public void CreateLiveSpectrum_RelativeAxis_ClearsAPaddedLoopback()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        PlotModelFactory factory = CreateFactory(measurement, noise);

        var dbAxis = (OxyPlot.Axes.LinearAxis)factory.CreateLiveSpectrum().Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);

        Assert.True(
            dbAxis.AbsoluteMaximum >= 40,
            $"the live dB ceiling of {dbAxis.AbsoluteMaximum} dB cannot show a padded loopback");
    }

    private static ExpSweepMeasurement CreateSweepOnlyMeasurement()
    {
        var sweep = new Complex[2048];
        sweep[64] = Complex.One;

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: sweep,
            sweepDeconvolutionPeakIndex: 64);
        return measurement;
    }

    private static ExpSweepMeasurement CreateTransferMeasurement(int peakSample = 64)
    {
        var transferImpulse = new Complex[2048];
        transferImpulse[peakSample] = Complex.One;

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: transferImpulse,
            sweepDeconvolutionPeakIndex: peakSample,
            measurementMode: SweepMeasurementMode.LoopbackTransfer,
            transferImpulseResponse: transferImpulse,
            transferPeakIndex: peakSample);
        return measurement;
    }

    // A transfer measurement carrying the whole SPL recipe: an anchor frozen onto
    // the result and pinned to the input it ran on, plus a captured loopback level.
    // K = loopbackPeakDbFs + (referenceLevelDbSpl - measuredLevelDbFs).
    private static ExpSweepMeasurement CreateSplTransferMeasurement(
        int peakSample,
        double loopbackPeakDbFs,
        double referenceLevelDbSpl,
        double measuredLevelDbFs)
    {
        ExpSweepMeasurement measurement = CreateTransferMeasurement(peakSample);
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
        measurement.MeasurementSplCalibration = anchor;
        measurement.MeasurementInput = anchor.CaptureIdentity;
        measurement.RestoreLevelSnapshot(new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(
                true, loopbackPeakDbFs, loopbackPeakDbFs - 3, false, false)));
        return measurement;
    }

    // A calibrated result carrying a REAL second-harmonic packet: the delta at the
    // sweep peak makes |H1| flat, the delta at the H2 packet position makes HD2 a
    // flat -34 dBc, and the separate transfer impulse makes the primary 0 dBr — so
    // every level on the SPL plot is K (= -6 + 94 + 20 = 108 dB) plus the curve's
    // own dB value.
    private static ExpSweepMeasurement CreateSplSweepWithSecondHarmonic()
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

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
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
            achievedHighFrequencyHz: sweep.EndFrequencyHz);

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
        measurement.MeasurementSplCalibration = anchor;
        measurement.MeasurementInput = anchor.CaptureIdentity;
        measurement.RestoreLevelSnapshot(new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(true, -6, -9, false, false)));
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
            new PlotPresentationOptions(
                FrequencyResponse: frequencyResponseOptions ?? new FrequencyResponseOptions(),
                PhaseResponse: phaseResponseOptions ?? new FrequencyResponseOptions(),
                GroupDelay: groupDelayOptions ?? new FrequencyResponseOptions(),
                FrequencyResponseVisibility: frequencyResponseVisibility ?? new CurveVisibilityOptions(),
                PhaseResponseVisibility: phaseResponseVisibility ?? new CurveVisibilityOptions(),
                GroupDelayVisibility: groupDelayVisibility ?? new CurveVisibilityOptions(),
                ImpulseResponse: impulseOptions ?? new ImpulseResponseOptions(),
                LiveSpectrum: liveSpectrumOptions ?? new LiveSpectrumOptions(),
                Waterfall: new WaterfallGenerateOptions(),
                BurstDecay: new WaterfallGenerateOptions()));
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
            AnalysisMode = LiveAnalysisMode.Rta,
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
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        // No configured calibration: the offset is unavailable, but the scale (and so
        // the axis and the overlay gate) follows the SELECTION — the view-only state
        // that lets overlays captured in dB SPL be seen. The controller suppresses
        // live curves there, and the record button resets the scale before a run.
        Assert.Null(factory.LiveSplOffsetDb);
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel, factory.EffectiveLiveSpectrumScale);
        var dbAxis = (OxyPlot.Axes.LinearAxis)factory.CreateLiveSpectrum().Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);

        // A calibration captured on a different digital input (sample rate) does not
        // apply to this live input either: still no offset, still view-only.
        SplCalibration mismatched = LiveAnchorMatching(noise, 94, -16);
        mismatched.SampleRate = 48_000;
        measurement.SplCalibration = mismatched;
        Assert.Null(factory.LiveSplOffsetDb);
        Assert.Equal(
            MagnitudeScale.SoundPressureLevel, factory.EffectiveLiveSpectrumScale);
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
            AnalysisMode = LiveAnalysisMode.Rta,
            CalibrationId = null,
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
            AnalysisMode = LiveAnalysisMode.Rta,
            CalibrationId = null,
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

    [Fact]
    public void LiveRtaTilt_FlattensPinkOnTheRelativeAxis()
    {
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        // Periodic pink: the one colour whose synthesis is an exact power law, so
        // its model is the mirrored straight line and the cancellation is exact.
        // (Random pink models the Kellett bank instead — pinned in the dsp tests.)
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.PinkPeriodic,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        // An exactly pink spectrum (amplitude ∝ 1/√f) through a flat system: with
        // the compensation on, the relative RTA must read flat — the per-bin line
        // mirrors the noise exactly.
        var magnitude = new double[(noise.SequenceLength / 2) + 1];
        for (int k = 1; k < magnitude.Length; k++)
        {
            magnitude[k] = 0.1 / Math.Sqrt(k);
        }

        LineSeries compensated = factory.BuildInputMagnitudeSeries(magnitude);
        Assert.NotEmpty(compensated.Points);
        double reference = compensated.Points[0].Y;
        Assert.All(
            compensated.Points,
            point => Assert.Equal(reference, point.Y, precision: 6));

        // The same input with the compensation off keeps the noise's own slope, so
        // the checkbox demonstrably does something: -3.01 dB per octave.
        options.CompensateNoiseTilt = false;
        LineSeries plain = factory.BuildInputMagnitudeSeries(magnitude);
        OxyPlot.DataPoint low = PointNear(plain, 1000.0);
        OxyPlot.DataPoint high = PointNear(plain, 4000.0);
        // The grid points sit near, not exactly at, the probe frequencies, so the
        // expected drop follows the actual span: amplitude ~ 1/sqrt(f).
        // precision 2: the resample interpolates linearly between bins, which for a
        // 1/sqrt(f) curve deviates from the analytic value by a few thousandths of a dB.
        Assert.Equal(-10.0 * Math.Log10(high.X / low.X), high.Y - low.Y, precision: 2);
    }

    [Fact]
    public void LiveRtaTilt_IsInertInTransferMode()
    {
        // The transfer function divides the excitation out, so the compensation must
        // not touch the RTA overlay drawn inside Transfer mode even when its
        // checkbox is stored on.
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            NoiseColor = NoiseColor.Pink,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        Assert.Null(factory.LiveTiltModel);

        var magnitude = new double[(noise.SequenceLength / 2) + 1];
        for (int k = 1; k < magnitude.Length; k++)
        {
            magnitude[k] = 0.1 / Math.Sqrt(k);
        }

        LineSeries series = factory.BuildInputMagnitudeSeries(magnitude);
        OxyPlot.DataPoint low = PointNear(series, 1000.0);
        OxyPlot.DataPoint high = PointNear(series, 4000.0);
        // precision 2: the resample interpolates linearly between bins, which for a
        // 1/sqrt(f) curve deviates from the analytic value by a few thousandths of a dB.
        Assert.Equal(-10.0 * Math.Log10(high.X / low.X), high.Y - low.Y, precision: 2);
    }

    [Fact]
    public void LiveSplTilt_FlattensWhiteOnTheBandAxis()
    {
        // The band-power (SPL) display tilts even a flat white PSD by +3 dB/octave
        // (band power grows with bandwidth), so the compensation there must follow
        // the BAND law, not the per-bin line — a zero PSD slope still compensates.
        using ExpSweepMeasurement measurement = CreateTransferMeasurement();
        using NoiseMeasurement noise = CreateLiveAnalyzer();
        measurement.SplCalibration = LiveAnchorMatching(noise, 94, -16);
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.White,
            CompensateNoiseTilt = true,
            CalibrationId = null,
            SmoothingInverseOctaves = 0
        };
        PlotModelFactory factory =
            CreateFactory(measurement, noise, liveSpectrumOptions: options);

        var magnitude = new double[(noise.SequenceLength / 2) + 1];
        Array.Fill(magnitude, 0.1);

        LineSeries compensated = factory.BuildInputMagnitudeSeries(magnitude);
        Assert.NotEmpty(compensated.Points);
        // The compensation renders the same flat spectrum through the same band
        // resampler and subtracts, so the cancellation is exact per point.
        double reference = compensated.Points[0].Y;
        Assert.All(
            compensated.Points,
            point => Assert.Equal(reference, point.Y, precision: 6));

        options.CompensateNoiseTilt = false;
        LineSeries plain = factory.BuildInputMagnitudeSeries(magnitude);
        double at2K = PointNear(plain, 2000.0).Y;
        double at8K = PointNear(plain, 8000.0).Y;
        // Uncompensated white climbs ~3.01 dB per octave on the band axis.
        Assert.Equal(2.0 * 10.0 * Math.Log10(2.0), at8K - at2K, precision: 1);
    }

    private static OxyPlot.DataPoint PointNear(LineSeries series, double frequency)
    {
        OxyPlot.DataPoint nearest = series.Points[0];
        foreach (OxyPlot.DataPoint point in series.Points)
        {
            if (Math.Abs(Math.Log2(point.X / frequency)) <
                Math.Abs(Math.Log2(nearest.X / frequency)))
            {
                nearest = point;
            }
        }

        return nearest;
    }
}
