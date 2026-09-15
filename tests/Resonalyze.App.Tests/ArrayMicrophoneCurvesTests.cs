using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ArrayMicrophoneCurvesTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private const double NoSmoothing = 0;

    private static AnalysisCurve NotNull(AnalysisCurve? curve)
    {
        Assert.NotNull(curve);
        return curve!;
    }

    private static int BandOf(double frequencyHz)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < Grid.Count; i++)
        {
            double distance = Math.Abs(Math.Log2(Grid[i] / frequencyHz));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static ArrayMicrophoneCurve Microphone(
        double levelDb,
        bool measurement = false,
        int channel = 2,
        string? note = null,
        VirtualCrossoverCalibrationSettings? calibration = null) =>
        new(
            channel,
            measurement,
            Enumerable.Repeat(levelDb, Grid.Count).ToArray(),
            AcceptedRuns: 1)
        {
            Note = note,
            Calibration = calibration
        };

    private static VirtualCrossoverCalibrationSettings Calibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [
                    new CalibrationPoint(20.0, correctionDb),
                    new CalibrationPoint(20_000.0, correctionDb)
                ],
                "flat"),
            "flat",
            null);

    [Fact]
    public void AMeasurementWithoutAnArrayDrawsNothing()
    {
        ArrayMicrophoneDisplay display =
            ArrayMicrophoneCurves.Build([], useCalibration: true, NoSmoothing);

        Assert.Null(display.Average);
        Assert.Empty(display.Microphones);
        Assert.Null(display.Spread);
    }

    [Fact]
    public void EveryPositionIsLevelledOntoTheMeasurementMicrophone()
    {
        // The measurement mic's level is tied to SPL calibration and the IR, so the average sits on it.
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                Microphone(70.0, measurement: true, channel: 0),
                Microphone(82.0),
                Microphone(82.0, channel: 3)
            ],
            useCalibration: false,
            NoSmoothing);

        int band = BandOf(1_000);
        Assert.Equal(70.0, NotNull(display.Average).Points[band].Y, 6);
        Assert.Equal(3, display.Microphones.Count);
        Assert.All(
            display.Microphones,
            microphone => Assert.Equal(70.0, microphone.Points[band].Y, 6));
    }

    [Fact]
    public void EachMicrophoneIsCorrectedByItsOwnCalibration()
    {
        // The pipeline subtracts a mic correction from a level; the array must do the same.
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                Microphone(70.0, measurement: true, channel: 0, calibration: Calibration(-2.0)),
                Microphone(70.0, calibration: Calibration(3.0))
            ],
            useCalibration: true,
            NoSmoothing);

        int band = BandOf(1_000);
        Assert.Equal(72.0, NotNull(display.Average).Points[band].Y, 6);
    }

    [Fact]
    public void TurningCalibrationOffLeavesEveryArrayCurveRaw()
    {
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [Microphone(70.0, measurement: true, channel: 0, calibration: Calibration(-6.0))],
            useCalibration: false,
            NoSmoothing);

        Assert.Equal(70.0, NotNull(display.Average).Points[BandOf(1_000)].Y, 6);
    }

    [Fact]
    public void TheAverageIsSmoothedAfterAveragingAndNotBefore()
    {
        // Power mean across positions and cubic smoothing do not commute; smoothing first read 0.11 dB (mid) / 0.39 dB (tweeter) high.
        double[] rough = Enumerable
            .Range(0, Grid.Count)
            .Select(band => 70.0 + (band % 2 == 0 ? 9.0 : -9.0))
            .ToArray();
        double[] flat = Enumerable.Repeat(70.0, Grid.Count).ToArray();

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                new ArrayMicrophoneCurve(0, true, rough, 1),
                new ArrayMicrophoneCurve(2, false, flat, 1)
            ],
            useCalibration: false,
            SpectrumSmoothing.PsychoacousticCode);

        IReadOnlyList<double> expected = SpatialAverage.RmsAverageDb([rough, flat]);
        List<SignalPoint> expectedSmoothed = DataHelper.SmoothBandLevels(
            expected.Select((value, band) => new SignalPoint(Grid[band], value)).ToList(),
            SpectrumSmoothing.SmoothingOctaves(SpectrumSmoothing.PsychoacousticCode),
            psychoacoustic: true);

        AnalysisCurve average = NotNull(display.Average);
        int band = BandOf(1_000);
        Assert.Equal(expectedSmoothed[band].Y, average.Points[band].Y, 6);
    }

    [Fact]
    public void OneMicrophoneHasNoSpreadRatherThanASpreadOfZero()
    {
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [Microphone(70.0, measurement: true, channel: 0)],
            useCalibration: false,
            NoSmoothing);

        Assert.Null(display.Spread);
        Assert.NotNull(display.Average);
    }

    [Fact]
    public void TheSpreadIsTheRangeBetweenThePlacedPositions()
    {
        var hot = Enumerable.Repeat(70.0, Grid.Count).ToArray();
        var cold = Enumerable.Repeat(70.0, Grid.Count).ToArray();
        int band = BandOf(2_000);
        cold[band] = 55.0;

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                new ArrayMicrophoneCurve(0, true, hot, 1),
                new ArrayMicrophoneCurve(2, false, cold, 1)
            ],
            useCalibration: false,
            NoSmoothing);

        AnalysisCurve spread = NotNull(display.Spread);
        Assert.Equal(AnalysisCurveKind.ArraySpread, spread.Kind);
        Assert.Equal(15.0, spread.Points[band].Y, 6);
        Assert.Equal(0.0, spread.Points[band - 5].Y, 6);
    }

    [Fact]
    public void EachPositionIsNamedByItsNoteOrItsInput()
    {
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                Microphone(70.0, measurement: true, channel: 0),
                Microphone(70.0, channel: 4, note: "left ear"),
                Microphone(70.0, channel: 5)
            ],
            useCalibration: false,
            NoSmoothing);

        Assert.Equal("Input 1 (measurement)", display.Microphones[0].Name);
        Assert.Equal("left ear", display.Microphones[1].Name);
        Assert.Equal("Input 6", display.Microphones[2].Name);
    }

    [Fact]
    public void ACurveFromAnotherGridIsRefusedRatherThanDrawnShifted()
    {
        // A stored grid this build does not use would shift every level in frequency.
        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [new ArrayMicrophoneCurve(0, true, new double[16], 1)],
            useCalibration: false,
            NoSmoothing);

        Assert.Null(display.Average);
        Assert.Empty(display.Microphones);
    }

    [Fact]
    public void AMicrophoneThatCannotBePlacedIsLeftOut()
    {
        double[] dead = Enumerable.Repeat(double.NaN, Grid.Count).ToArray();

        ArrayMicrophoneDisplay display = ArrayMicrophoneCurves.Build(
            [
                Microphone(70.0, measurement: true, channel: 0),
                new ArrayMicrophoneCurve(2, false, dead, 1)
            ],
            useCalibration: false,
            NoSmoothing);

        Assert.Single(display.Microphones);
        Assert.Equal(70.0, NotNull(display.Average).Points[BandOf(1_000)].Y, 6);
        Assert.Null(display.Spread);
    }
}
