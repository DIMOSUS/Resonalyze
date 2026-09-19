using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The live plot's frame and curves, from a read of the analyzer built by hand: axis and anchor, tilt, band power and
/// the capture document. No audio runs here; <see cref="LiveSpectrumSessionTests"/> covers the read itself.
/// </summary>
public sealed class LiveSpectrumCurvesTests
{
    [Fact]
    public void TheRelativeAxisClearsAPaddedLoopback()
    {
        var dbAxis = (OxyPlot.Axes.LinearAxis)LiveSpectrumPlotFactory
            .CreateModel(Display(new LiveSpectrumOptions()))
            .Axes.First(axis => axis.Key == PlotModelFactory.DecibelAxisKey);

        Assert.True(
            dbAxis.AbsoluteMaximum >= 40,
            $"the live dB ceiling of {dbAxis.AbsoluteMaximum} dB cannot show a padded loopback");
    }

    [Fact]
    public void AnAnchoredRtaInSplUsesTheSplAxis()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        LiveSpectrumDisplay display = Display(options, anchor: Anchor(94, -16));

        Assert.Equal(MagnitudeScale.SoundPressureLevel, display.Scale);
        Assert.Equal(94 - (-16), display.SplOffsetDb!.Value, precision: 9);
        OxyPlot.PlotModel model = LiveSpectrumPlotFactory.CreateModel(display);
        var dbAxis = (OxyPlot.Axes.LinearAxis)model.Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);
        Assert.Equal(PlotModelStyle.SplDecibelMaximum, dbAxis.Maximum);
        Assert.Contains("SPL", model.Title);
    }

    [Fact]
    public void AnAnchorCountsOnlyWhenTakenOnTheLiveInput()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };

        LiveSpectrumDisplay unanchored = Display(options);
        Assert.Null(unanchored.SplOffsetDb);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, unanchored.Scale);
        Assert.True(unanchored.SplViewOnly);
        var dbAxis = (OxyPlot.Axes.LinearAxis)LiveSpectrumPlotFactory.CreateModel(unanchored).Axes.First(
            axis => axis.Key == PlotModelFactory.DecibelAxisKey);
        Assert.Equal("dB SPL", dbAxis.Title);

        SplCalibration mismatched = Anchor(94, -16);
        mismatched.SampleRate = 48_000;
        LiveSpectrumDisplay elsewhere = Display(options, anchor: mismatched);
        Assert.Null(elsewhere.SplOffsetDb);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, elsewhere.Scale);
        Assert.True(elsewhere.SplViewOnly);
    }

    [Fact]
    public void SplPeakHoldHoldsBandPowerNotTheSumOfPerBinMaxima()
    {
        // Two frames in different bins of one band must not peak-hold to the sum of bin maxima (+3 dB).
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            CalibrationId = null,
            SmoothingInverseOctaves = 6,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        LiveCaptureSetup setup = Setup();
        LiveSpectrumDisplay display = Display(options, setup, Anchor(94, -16));
        var curves = new LiveSpectrumCurves();

        int binCount = setup.SequenceLength / 2;
        // 44100/2048 ≈ 21.5 Hz/bin, ~5 bins per 1/6 oct near 1 kHz.
        const int binA = 47;
        const int binB = 48;
        var frameA = new double[binCount];
        var frameB = new double[binCount];
        var frameBoth = new double[binCount];
        frameA[binA] = 1.0;
        frameB[binB] = 1.0;
        frameBoth[binA] = 1.0;
        frameBoth[binB] = 1.0;

        List<SignalPoint> bandA = curves.MainDisplayPoints(display, frameA, rtaOnly: true);
        List<SignalPoint> bandB = curves.MainDisplayPoints(display, frameB, rtaOnly: true);
        List<SignalPoint> bandBoth = curves.MainDisplayPoints(display, frameBoth, rtaOnly: true);

        int peak = 0;
        for (int i = 1; i < bandBoth.Count; i++)
        {
            if (bandBoth[i].Y > bandBoth[peak].Y)
            {
                peak = i;
            }
        }

        double held = Math.Max(bandA[peak].Y, bandB[peak].Y);
        Assert.True(
            bandBoth[peak].Y - held > 2.0,
            $"peak hold {held:0.00} dB reached the summed band {bandBoth[peak].Y:0.00} dB");
    }

    [Fact]
    public void WithoutALoopbackTheTransferShowsAsAnRtaWithNoCoherenceAxis()
    {
        var options = new LiveSpectrumOptions { ShowCoherence = true };

        OxyPlot.PlotModel model = LiveSpectrumPlotFactory.CreateModel(
            Display(options, Setup(micOnly: true)));

        Assert.Contains("RTA", model.Title);
        Assert.DoesNotContain(model.Axes, axis => axis.Key == PlotModelFactory.CoherenceAxisKey);
    }

    [Fact]
    public void AnSplRtaIsLiftedByTheCalibrationOffset()
    {
        // The SPL RTA is power-integrated, so only the offset difference is compared.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            CalibrationId = null,
            SmoothingInverseOctaves = 0,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel
        };
        LiveCaptureSetup setup = Setup();
        var curves = new LiveSpectrumCurves();
        var magnitude = new double[setup.SequenceLength / 2];
        Array.Fill(magnitude, 0.1);

        List<SignalPoint> lower = curves.Rta(Display(options, setup, Anchor(94, -16)), magnitude);
        List<SignalPoint> higher = curves.Rta(Display(options, setup, Anchor(104, -16)), magnitude);

        Assert.Equal(lower.Count, higher.Count);
        Assert.NotEmpty(lower);
        for (int i = 0; i < lower.Count; i++)
        {
            Assert.Equal(lower[i].X, higher[i].X, precision: 9);
            Assert.Equal(lower[i].Y + 10.0, higher[i].Y, precision: 6);
        }
    }

    [Fact]
    public void TiltCompensationFlattensPinkOnTheRelativeAxis()
    {
        // Periodic pink is an exact power law, so the cancellation is exact (random pink models the Kellett bank).
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.PinkPeriodic,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0
        };
        LiveCaptureSetup setup = Setup();
        var curves = new LiveSpectrumCurves();
        var magnitude = new double[(setup.SequenceLength / 2) + 1];
        for (int k = 1; k < magnitude.Length; k++)
        {
            magnitude[k] = 0.1 / Math.Sqrt(k);
        }

        List<SignalPoint> compensated = curves.Rta(Display(options, setup), magnitude);
        Assert.NotEmpty(compensated);
        double reference = compensated[0].Y;
        Assert.All(compensated, point => Assert.Equal(reference, point.Y, precision: 6));

        options.CompensateNoiseTilt = false;
        List<SignalPoint> plain = curves.Rta(Display(options, setup), magnitude);
        SignalPoint low = PointNear(plain, 1000.0);
        SignalPoint high = PointNear(plain, 4000.0);
        // precision 2: linear interpolation of a 1/sqrt(f) curve deviates by a few thousandths of a dB.
        Assert.Equal(-10.0 * Math.Log10(high.X / low.X), high.Y - low.Y, precision: 2);
    }

    [Fact]
    public void TiltCompensationIsInertInTransferMode()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            NoiseColor = NoiseColor.Pink,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0
        };
        LiveCaptureSetup setup = Setup();
        LiveSpectrumDisplay display = Display(options, setup);

        Assert.Null(display.TiltModel);

        var magnitude = new double[(setup.SequenceLength / 2) + 1];
        for (int k = 1; k < magnitude.Length; k++)
        {
            magnitude[k] = 0.1 / Math.Sqrt(k);
        }

        List<SignalPoint> rta = new LiveSpectrumCurves().Rta(display, magnitude);
        SignalPoint low = PointNear(rta, 1000.0);
        SignalPoint high = PointNear(rta, 4000.0);
        Assert.Equal(-10.0 * Math.Log10(high.X / low.X), high.Y - low.Y, precision: 2);
    }

    [Fact]
    public void SplTiltCompensationFlattensWhiteOnTheBandAxis()
    {
        // Band power tilts flat white by +3 dB/oct, so the compensation follows the band law.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.White,
            CompensateNoiseTilt = true,
            CalibrationId = null,
            SmoothingInverseOctaves = 0
        };
        LiveCaptureSetup setup = Setup();
        var curves = new LiveSpectrumCurves();
        var magnitude = new double[(setup.SequenceLength / 2) + 1];
        Array.Fill(magnitude, 0.1);

        List<SignalPoint> compensated = curves.Rta(Display(options, setup, Anchor(94, -16)), magnitude);
        Assert.NotEmpty(compensated);
        double reference = compensated[0].Y;
        Assert.All(compensated, point => Assert.Equal(reference, point.Y, precision: 6));

        options.CompensateNoiseTilt = false;
        List<SignalPoint> plain = curves.Rta(Display(options, setup, Anchor(94, -16)), magnitude);
        double at2K = PointNear(plain, 2000.0).Y;
        double at8K = PointNear(plain, 8000.0).Y;
        Assert.Equal(2.0 * 10.0 * Math.Log10(2.0), at8K - at2K, precision: 1);
    }

    /// <summary>Bins are re-rendered on redraw and Save; the rig's selection is not what the capture was taken through.</summary>
    [Fact]
    public void AnMmmCaptureKeepsTheMicrophoneItsRunWasTakenThrough()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0,
            CalibrationId = "cal-a"
        };
        CalibrationFile a = CalibrationFile.FromPoints(
            [new CalibrationPoint(20, 6.0), new CalibrationPoint(20_000, 6.0)]);
        LiveCaptureSetup setup = Setup() with
        {
            MicrophoneCalibration = a,
            MicrophoneCalibrationName = "90° capsule 2"
        };
        var curves = new LiveSpectrumCurves();
        var magnitude = new double[(setup.SequenceLength / 2) + 1];
        Array.Fill(magnitude, 0.1);

        LiveCaptureDocument? taken = curves.CaptureDocument(
            Display(options, setup), magnitude, frameCount: 8, title: "walked through A");
        Assert.NotNull(taken);
        double[] curveThroughA = taken.CurveDb;

        options.CalibrationId = "cal-b";

        LiveCaptureDocument? saved = curves.CaptureDocument(
            Display(options, setup), magnitude, frameCount: 8, title: "saved after the rig moved");
        Assert.NotNull(saved);
        Assert.Equal("90° capsule 2", saved.Calibration?.Name);
        Assert.Equal(curveThroughA, saved.CurveDb);
        Assert.All(
            saved.CalibrationCorrectionDb,
            correction => Assert.Equal(6.0, correction, precision: 6));
    }

    /// <summary>Curve and recipe read the high-pass frozen when the run began.</summary>
    [Fact]
    public void AnMmmCaptureDividesOutTheFilterItsRunWasTakenThrough()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic,
            CompensateNoiseTilt = true,
            SmoothingInverseOctaves = 0
        };
        LiveCaptureSetup unfilteredSetup = Setup();
        LiveCaptureSetup filteredSetup = unfilteredSetup with
        {
            ProtectiveHighPass = new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Butterworth, 2_000, 24)
        };
        var curves = new LiveSpectrumCurves();
        var magnitude = new double[(unfilteredSetup.SequenceLength / 2) + 1];
        Array.Fill(magnitude, 0.1);

        List<SignalPoint> none = curves.Rta(Display(options, unfilteredSetup), magnitude);
        LiveCaptureDocument? unfiltered = curves.CaptureDocument(
            Display(options, unfilteredSetup), magnitude, frameCount: 8, title: "no filter");
        Assert.NotNull(unfiltered);
        Assert.Equal(ProtectiveHighPassKind.Off, unfiltered.Recipe.ProtectiveHighPassKind);
        Assert.Empty(unfiltered.ProtectiveHighPassCorrectionDb);

        List<SignalPoint> filtered = curves.Rta(Display(options, filteredSetup), magnitude);
        LiveCaptureDocument? compensated = curves.CaptureDocument(
            Display(options, filteredSetup), magnitude, frameCount: 8, title: "filtered");
        Assert.NotNull(compensated);

        Assert.Equal(
            ProtectiveHighPassKind.Butterworth,
            compensated.Recipe.ProtectiveHighPassKind);
        Assert.Equal(2_000, compensated.Recipe.ProtectiveHighPassFrequencyHz);
        Assert.NotEmpty(compensated.ProtectiveHighPassCorrectionDb);

        Assert.Equal(compensated.CurveDb.Length, compensated.ProtectiveHighPassCorrectionDb.Length);
        double before = PointNear(none, 900.0).Y;
        double after = PointNear(filtered, 900.0).Y;
        Assert.True(
            after - before > 10,
            $"the filter was not divided out: {after - before:0.0} dB at 900 Hz");
    }

    [Fact]
    public void MmmWithoutAnAnchorKeepsBandPowerOnARelativeAxis()
    {
        // The pipeline and the absolute axis are independent: an unanchored capture must not land at raw dBFS on the SPL axis.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic,
            CompensateNoiseTilt = true,
            CalibrationId = null,
            SmoothingInverseOctaves = 0
        };
        LiveCaptureSetup setup = Setup();
        var curves = new LiveSpectrumCurves();

        LiveSpectrumDisplay unanchored = Display(options, setup);
        Assert.True(unanchored.UsesBandPower);
        Assert.Equal(MagnitudeScale.Relative, unanchored.Scale);
        Assert.False(unanchored.SplViewOnly);

        var magnitude = new double[(setup.SequenceLength / 2) + 1];
        Array.Fill(magnitude, 0.1);
        List<SignalPoint> relative = curves.Rta(unanchored, magnitude);
        Assert.NotEmpty(relative);
        Assert.All(relative, point => Assert.True(double.IsFinite(point.Y)));

        LiveSpectrumDisplay anchored = Display(options, setup, Anchor(94, -16));
        Assert.True(anchored.UsesBandPower);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, anchored.Scale);

        List<SignalPoint> absolute = curves.Rta(anchored, magnitude);
        double offset = PointNear(absolute, 1000.0).Y - PointNear(relative, 1000.0).Y;
        Assert.Equal(relative.Count, absolute.Count);
        for (int i = 0; i < relative.Count; i++)
        {
            Assert.Equal(relative[i].X, absolute[i].X, precision: 6);
            Assert.Equal(relative[i].Y + offset, absolute[i].Y, precision: 6);
        }
    }

    // What the analyzer reports at 44.1 kHz and 2048 points under its default periodic pink: a rectangular window.
    internal static LiveCaptureSetup Setup(bool micOnly = false) => new(
        SampleRate: 44_100,
        SequenceLength: 2048,
        HopSize: 2048,
        WindowType: WindowType.Rectangular,
        WindowEnbwBins: Windowing.EquivalentNoiseBandwidthBins(WindowType.Rectangular, 2048),
        WindowMainLobeBins: Windowing.MainLobeWidthBins(WindowType.Rectangular),
        IsMicOnly: micOnly,
        Input: new MeasurementInputIdentity(AudioBackend.Wave, 44_100, 24, 0, -1, null, null),
        CaptureSessionId: Guid.Empty,
        ProtectiveHighPass: ProtectiveHighPassConfiguration.Off,
        MicrophoneCalibration: null,
        MicrophoneCalibrationName: string.Empty);

    private static LiveSpectrumDisplay Display(
        LiveSpectrumOptions options,
        LiveCaptureSetup? setup = null,
        SplCalibration? anchor = null) =>
        LiveSpectrumDisplay.Of(options, setup ?? Setup(), anchor);

    // An SPL calibration taken on the input Setup() describes.
    internal static SplCalibration Anchor(double referenceLevelDbSpl, double measuredLevelDbFs)
    {
        MeasurementInputIdentity id = Setup().Input;
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

    private static SignalPoint PointNear(List<SignalPoint> points, double frequency)
    {
        SignalPoint nearest = points[0];
        foreach (SignalPoint point in points)
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
