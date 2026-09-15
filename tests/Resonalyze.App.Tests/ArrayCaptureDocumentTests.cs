using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ArrayCaptureDocumentTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static ArrayMicrophoneCurve Microphone(
        double levelDb,
        bool measurement = false,
        int channel = 2,
        VirtualCrossoverCalibrationSettings? calibration = null) =>
        new(
            channel,
            measurement,
            Enumerable.Repeat(levelDb, Grid.Count).ToArray(),
            AcceptedRuns: 1)
        {
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
            $"flat {correctionDb:0.#}",
            null);

    // Two positions: a lone microphone is not a spatial average.
    private static IReadOnlyList<ArrayMicrophoneCurve> Pair(double levelDb) =>
    [
        Microphone(levelDb, measurement: true, channel: 0),
        Microphone(levelDb, channel: 2)
    ];

    private static LiveCaptureDocument Create(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        ProtectiveHighPassConfiguration? filter = null)
    {
        LiveCaptureDocument? document =
            ArrayCaptureDocument.TryCreate(microphones, 48_000, filter);
        Assert.NotNull(document);
        return document!;
    }

    [Fact]
    public void AMeasurementWithoutAnArrayProducesNoDocument() =>
        Assert.Null(ArrayCaptureDocument.TryCreate([], 48_000, null));

    [Fact]
    public void TheDocumentSaysWhichMethodMadeIt()
    {
        LiveCaptureDocument document = Create(Pair(70.0));

        Assert.Equal(SpatialAverageMethod.MicArray, document.Method);
        Assert.Equal("Array of 2 microphones", document.Title);
    }

    [Fact]
    public void TheCurveIsTheSpatialAverageOnTheMeasurementMicrophonesLevel()
    {
        LiveCaptureDocument document = Create(
        [
            Microphone(70.0, measurement: true, channel: 0),
            Microphone(82.0, channel: 2),
            Microphone(82.0, channel: 3)
        ]);

        Assert.Equal(Grid.Count, document.CurveDb.Length);
        Assert.All(document.CurveDb, level => Assert.Equal(70.0, level, 6));
    }

    [Fact]
    public void EveryMicrophoneIsCorrectedByItsOwnCalibration()
    {
        LiveCaptureDocument document = Create(
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: Calibration(-2.0)),
            Microphone(70.0, channel: 2, calibration: Calibration(3.0))
        ]);

        Assert.All(document.CurveDb, level => Assert.Equal(72.0, level, 6));
    }

    [Fact]
    public void OneSharedCalibrationIsNamed_AMixedOneIsNot()
    {
        LiveCaptureDocument shared = Create(
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: Calibration(-2.0)),
            Microphone(70.0, channel: 2, calibration: Calibration(-2.0))
        ]);
        Assert.Equal("flat -2", shared.Calibration!.Name);

        // No single curve describes a mixed correction, so none is claimed.
        LiveCaptureDocument mixed = Create(
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: Calibration(-2.0)),
            Microphone(70.0, channel: 2, calibration: Calibration(3.0))
        ]);
        Assert.Null(mixed.Calibration);
    }

    [Fact]
    public void TheRecipeCarriesTheProtectiveHighPassAndClaimsNoAnalyzer()
    {
        LiveCaptureDocument document = Create(
            Pair(70.0),
            new ProtectiveHighPassConfiguration(ProtectiveHighPassKind.Butterworth, 2_000, 24));

        Assert.Equal(ProtectiveHighPassKind.Butterworth, document.Recipe.ProtectiveHighPassKind);
        Assert.Equal(2_000, document.Recipe.ProtectiveHighPassFrequencyHz);
        Assert.Equal(24, document.Recipe.ProtectiveHighPassSlopeDbPerOctave);

        Assert.Equal(MagnitudeScale.Relative, document.Recipe.MagnitudeScale);
        Assert.False(document.Recipe.SlopeCompensation);
        Assert.Equal(0, document.Recipe.SmoothingCode);
        Assert.Equal(48_000, document.Recipe.SampleRateHz);
    }

    [Fact]
    public void TheRecipeRecordsHowManyMicrophonesMadeTheAverage()
    {
        LiveCaptureDocument document = Create(
        [
            Microphone(70.0, measurement: true, channel: 0),
            Microphone(70.0, channel: 2),
            Microphone(70.0, channel: 3)
        ]);

        Assert.Equal(3, document.Recipe.MicrophoneCount);
        Assert.Equal("Array of 3 microphones", document.Title);
    }

    [Fact]
    public void ASetOfArraysNeedsNoMatchingAnalyzerRecipe()
    {
        // The loopback holds array levels together, so separate sessions need no SPL anchor.
        LiveCaptureDocument first = Create(Pair(70.0));
        LiveCaptureDocument second = Create(Pair(64.0));
        Assert.NotEqual(first.CaptureSessionId, second.CaptureSessionId);
        Assert.Null(first.Recipe.SplAnchorOffsetDb);

        Assert.True(LiveCaptureDocument.JudgeSet([first, second]).Coherent);
    }

    [Fact]
    public void ASetOfArraysAcceptsChannelsFilteredDifferently()
    {
        // The protective high-pass is per-channel hardware, divided out per position, so differing filters are allowed.
        LiveCaptureDocument plain = Create(Pair(70.0));
        LiveCaptureDocument filtered = Create(
            Pair(70.0),
            new ProtectiveHighPassConfiguration(ProtectiveHighPassKind.Butterworth, 2_000, 24));

        Assert.True(LiveCaptureDocument.JudgeSet([plain, filtered]).Coherent);
    }

    [Fact]
    public void ASetMayNotMixTheTwoMethods()
    {
        LiveCaptureDocument array = Create(Pair(70.0));
        var movingMic = new LiveCaptureDocument
        {
            Method = SpatialAverageMethod.MovingMic,
            CurveDb = Enumerable.Repeat(70.0, Grid.Count).ToArray(),
            GridStartHz = Grid[0],
            GridStopHz = Grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };

        LiveCaptureSetVerdict verdict = LiveCaptureDocument.JudgeSet([array, movingMic]);
        Assert.False(verdict.Coherent);
        Assert.Contains("one set cannot hold both", verdict.Reason);
    }

    [Fact]
    public void ACurveFromAnotherGridIsRefusedRatherThanShifted()
    {
        Assert.Null(ArrayCaptureDocument.TryCreate(
            [new ArrayMicrophoneCurve(0, true, new double[16], 1)],
            48_000,
            null));
    }
}
