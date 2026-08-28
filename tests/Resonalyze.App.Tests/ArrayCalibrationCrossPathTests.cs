using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// One array, two tools, one answer — for every setting of the calibration, not
/// only the one that happens to cancel.
/// </summary>
/// <remarks>
/// These compare the PATHS against each other rather than a document against
/// itself. A document checked against its own declared correction is a tautology:
/// it proves the two halves of one construction agree, which they must, and says
/// nothing about the other construction the frequency response performs from the
/// same stored curves. That was the hole the first fix left.
/// </remarks>
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

    // The reviewer's own example: two positions six decibels apart, corrected by
    // files five decibels apart. Everything that could disagree, disagrees.
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

        // Turning the view's calibration off changes what is DRAWN. It does not
        // re-measure where the microphones sat: a trim is a placement, computed
        // once, on the curves that make a level difference a level difference
        // rather than a difference between capsules.
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
        // A moving-microphone capture is a MEASUREMENT of its own, taken on its own
        // day through its own correction, and only attached to this channel. Reading
        // it through the calibration of the impulse response beside it is the one
        // thing "Own (as measured)" must never do — and the error is the whole
        // difference between the two files.
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

        // Off still undoes it exactly, and a curve the user names is still applied —
        // that is what makes the selector mean something for a capture that CAN say
        // what one correction it carries.
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
        // A mixture the other way round: the anchor carries no file and a further
        // position does. The array is still an aggregate — some of it was corrected —
        // and "read it as it was measured" still means the curve the document holds,
        // not the nothing the anchor's file names.
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
