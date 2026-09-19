using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ArrayCalibrationAndBandsTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static VirtualCrossoverCalibrationSettings Calibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [
                    new CalibrationPoint(20.0, correctionDb),
                    new CalibrationPoint(20_000.0, correctionDb)
                ],
                "flat"),
            $"flat {correctionDb:0.#}",
            null);

    private static ArrayMicrophoneCurve Microphone(
        double levelDb,
        bool measurement,
        int channel,
        VirtualCrossoverCalibrationSettings? calibration) =>
        new(
            channel,
            measurement,
            Enumerable.Repeat(levelDb, Grid.Count).ToArray(),
            AcceptedRuns: 1)
        {
            Calibration = calibration
        };

    [Fact]
    public void AMatchedArrayDeclaresTheCorrectionItSubtracted()
    {
        // An empty correction reads as uncalibrated, and the panel's calibration would be applied twice.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(70.0, measurement: false, channel: 2, Calibration(-2.0))
        ],
            48_000,
            null)!;

        Assert.Equal(document.CurveDb.Length, document.CalibrationCorrectionDb.Length);
        Assert.All(
            document.CalibrationCorrectionDb,
            correction => Assert.Equal(-2.0, correction, 6));

        for (int band = 0; band < document.CurveDb.Length; band++)
        {
            Assert.Equal(
                70.0,
                document.CurveDb[band] + document.CalibrationCorrectionDb[band],
                6);
        }
    }

    [Fact]
    public void AMixedArrayDeclaresWhatWasActuallySubtracted()
    {
        // The mixed-array correction is measured (calibrated minus raw average), so it is exact without a file.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(70.0, measurement: false, channel: 2, Calibration(3.0))
        ],
            48_000,
            null)!;

        Assert.Null(document.Calibration);
        Assert.Equal(document.CurveDb.Length, document.CalibrationCorrectionDb.Length);
        for (int band = 0; band < document.CurveDb.Length; band++)
        {
            Assert.True(
                double.IsFinite(document.CurveDb[band] + document.CalibrationCorrectionDb[band]),
                "undoing the correction must land on a measured level");
        }
    }

    [Fact]
    public void AMixedArrayReachesVirtualDspWithItsOwnCorrectionsIntact()
    {
        // Mixed calibrations cannot be swapped: calibrated keeps each position's file, uncalibrated undoes exactly.
        // Applying the measurement mic's file to all put VDSP a decibel off the FR view.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(76.0, measurement: false, channel: 2, Calibration(3.0))
        ],
            48_000,
            null)!;

        IReadOnlyList<double> frequencies = [Grid[100], Grid[500], Grid[900]];
        int[] bands = [100, 500, 900];

        List<SignalPoint> calibrated = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Specific(CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 5.0), new CalibrationPoint(20_000.0, 5.0)],
                "the panel's")),
            frequencies,
            smoothingCode: 0)!;
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(document.CurveDb[bands[i]], calibrated[i].Y, 6);
        }

        List<SignalPoint> raw = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Off,
            frequencies,
            smoothingCode: 0)!;
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(
                document.CurveDb[bands[i]] + document.CalibrationCorrectionDb[bands[i]],
                raw[i].Y,
                6);
        }
    }

    [Fact]
    public void AMatchedArrayIsStillRebasedOntoThePanelsCalibration()
    {
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(76.0, measurement: false, channel: 2, Calibration(-2.0))
        ],
            48_000,
            null)!;

        List<SignalPoint> curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Specific(CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 5.0), new CalibrationPoint(20_000.0, 5.0)],
                "the panel's")),
            [Grid[500]],
            smoothingCode: 0)!;

        Assert.Equal(
            document.CurveDb[500] + document.CalibrationCorrectionDb[500] - 5.0,
            curve[0].Y,
            6);
    }

    [Fact]
    public void AnUncalibratedArrayDeclaresNoCorrection()
    {
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: null),
            Microphone(70.0, measurement: false, channel: 2, calibration: null)
        ],
            48_000,
            null)!;

        Assert.All(
            document.CalibrationCorrectionDb,
            correction => Assert.Equal(0.0, correction, 9));
    }

    [Fact]
    public void AResultHandsItsArrayAndItsFilterToTheFile()
    {
        // History and disk loads must be the same measurement (the conversion must carry both fields).
        var result = new MeasurementResult
        {
            SampleRate = 48_000,
            Bits = 24,
            SweepDeconvolution = new MeasurementImpulseResponse(new System.Numerics.Complex[8], 0),
            ArrayMicrophones =
            [
                Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0))
            ],
            ProtectiveHighPass = new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Butterworth, 1_000, 48),
            MicrophoneCalibration = Calibration(-2.0)
        };

        ImpulseResponseFile file = ImpulseResponseFile.From(result);

        Assert.NotNull(file.ArrayMicrophones);
        Assert.Single(file.ArrayMicrophones!.Microphones);
        Assert.NotNull(file.ProtectiveHighPass);
        Assert.Equal(1_000, file.ProtectiveHighPass!.FrequencyHz, 6);
        Assert.NotNull(file.MicrophoneCalibration);
    }

    [Fact]
    public void ASumBreaksInTheHoleBetweenTwoDisjointSweeps()
    {
        // Between a woofer to 500 Hz and a tweeter from 1 kHz the sum is only the window; the hull cannot say that.
        var channels = new[]
        {
            Channel(new MeasuredBand(20, 500)),
            Channel(new MeasuredBand(1_000, 20_000))
        };
        SignalPoint[] curve =
            [new(100, -30), new(700, -30), new(2_000, -30), new(30_000, -30)];

        IReadOnlyList<SignalPoint> masked =
            ProcessedChannels.MeasuredBySomeChannel(curve, channels);

        Assert.True(double.IsFinite(masked[0].Y), "100 Hz is the woofer's");
        Assert.False(double.IsFinite(masked[1].Y), "700 Hz is nobody's");
        Assert.True(double.IsFinite(masked[2].Y), "2 kHz is the tweeter's");
        Assert.False(double.IsFinite(masked[3].Y), "30 kHz is past both");

        MeasuredBand hull = ProcessedChannels.UnionOfMeasuredBands(channels);
        Assert.Equal(20, hull.LowEdgeHz, 6);
        Assert.Equal(20_000, hull.HighEdgeHz, 6);
    }

    private static ProcessedChannel Channel(MeasuredBand band) =>
        new(
            new VirtualCrossoverChannel("channel"),
            new System.Numerics.Complex[8],
            PeakIndex: 0,
            SampleRate: 48_000,
            OxyPlot.OxyColors.White,
            default,
            band);

    [Fact]
    public void APositionThatCouldNotBePlacedDoesNotVoteOnTheCalibration()
    {
        // An unplaced position is absent from the curve, so its calibration must not vote on the correction.
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        var shared = VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 0.0), new CalibrationPoint(20_000.0, 0.0)],
                "shared"),
            "shared",
            null);
        var odd = VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 3.0), new CalibrationPoint(20_000.0, 3.0)],
                "odd"),
            "odd",
            null);

        var microphones = new List<ArrayMicrophoneCurve>();
        for (int i = 0; i < 6; i++)
        {
            microphones.Add(new ArrayMicrophoneCurve(
                i,
                IsMeasurementMicrophone: i == 0,
                Enumerable.Repeat(-30.0, grid.Count).ToArray(),
                AcceptedRuns: 1)
            {
                Calibration = shared
            });
        }

        microphones.Add(new ArrayMicrophoneCurve(
            6,
            IsMeasurementMicrophone: false,
            Enumerable.Repeat(double.NaN, grid.Count).ToArray(),
            AcceptedRuns: 1)
        {
            Calibration = odd
        });

        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
            microphones, 48_000, null)!;

        Assert.Equal(6, document.Recipe.MicrophoneCount);
        Assert.False(document.CalibrationIsAggregate);
        Assert.Equal("shared", document.Calibration?.Name);
    }
}
