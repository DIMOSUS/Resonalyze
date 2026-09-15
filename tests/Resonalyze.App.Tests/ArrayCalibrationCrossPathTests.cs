using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Compares the document path against the frequency-response path; a document checked against itself is a tautology.</summary>
public sealed class ArrayCalibrationCrossPathTests
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

    // Positions 6 dB apart, files 5 dB apart: everything that could disagree does.
    private static ArrayMicrophoneCurve[] MixedArray() =>
    [
        Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
        Microphone(76.0, measurement: false, channel: 2, Calibration(3.0))
    ];

    [Fact]
    public void AnUncalibratedMixedArrayReadsTheSameInBothTools()
    {
        ArrayMicrophoneCurve[] microphones = MixedArray();

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            microphones, useCalibration: false, smoothingInverseOctaves: 0.0);
        Assert.NotNull(display.Average);

        LiveCaptureDocument document =
            ArrayCaptureDocument.TryCreate(microphones, 48_000, null)!;
        List<SignalPoint> hybrid = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Off,
            [Grid[100], Grid[500], Grid[900]],
            smoothingCode: 0)!;

        // Calibration Off changes what is drawn, not the trims, which are computed once on corrected curves.
        int[] bands = [100, 500, 900];
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(display.Average!.Points[bands[i]].Y, hybrid[i].Y, 6);
        }
    }

    [Fact]
    public void ACalibratedMixedArrayReadsTheSameInBothTools()
    {
        ArrayMicrophoneCurve[] microphones = MixedArray();

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            microphones, useCalibration: true, smoothingInverseOctaves: 0.0);
        LiveCaptureDocument document =
            ArrayCaptureDocument.TryCreate(microphones, 48_000, null)!;
        List<SignalPoint> hybrid = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Own,
            [Grid[100], Grid[500], Grid[900]],
            smoothingCode: 0)!;

        int[] bands = [100, 500, 900];
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(display.Average!.Points[bands[i]].Y, hybrid[i].Y, 6);
        }
    }

    [Fact]
    public void OwnReadsAnAttachedCaptureThroughItsOwnCalibration()
    {
        // A capture is its own measurement: "Own" must read it through its own correction, not the IR's.
        var capture = new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "attached",
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = Grid.Select(_ => 70.0).ToArray(),
            CalibrationCorrectionDb = Grid.Select(_ => 3.0).ToArray(),
            Calibration = Calibration(3.0),
            GridStartHz = Grid[0],
            GridStopHz = Grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };

        List<SignalPoint> own = SpatialAverageHybrid.BuildChannelCurve(
            capture,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Own,
            [Grid[500]],
            smoothingCode: 0)!;
        Assert.Equal(70.0, own[0].Y, 6);

        List<SignalPoint> off = SpatialAverageHybrid.BuildChannelCurve(
            capture,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Off,
            [Grid[500]],
            smoothingCode: 0)!;
        Assert.Equal(73.0, off[0].Y, 6);
    }

    [Fact]
    public void OwnHoldsWhenTheMeasurementMicrophoneItselfIsUncalibrated()
    {
        ArrayMicrophoneCurve[] microphones =
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: null),
            Microphone(76.0, measurement: false, channel: 2, Calibration(3.0))
        ];
        LiveCaptureDocument document =
            ArrayCaptureDocument.TryCreate(microphones, 48_000, null)!;
        Assert.True(document.CalibrationIsAggregate);

        List<SignalPoint> own = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            SpatialAverageCalibration.Own,
            [Grid[500]],
            smoothingCode: 0)!;

        Assert.Equal(document.CurveDb[500], own[0].Y, 6);
        Assert.NotEqual(
            document.CurveDb[500] + document.CalibrationCorrectionDb[500],
            own[0].Y,
            6);
    }
}
