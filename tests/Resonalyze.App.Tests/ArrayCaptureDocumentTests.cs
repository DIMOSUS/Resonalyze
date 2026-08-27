using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A measurement's array handed to the consumers that already understand a
/// spatial average — and the rules a SET of them lives under, which are far
/// fewer than a moving microphone's because an array is tethered to the loopback
/// rather than to one analyzer session.
/// </summary>
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
            AcceptedRuns: 1,
            Issues: [])
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

    // The smallest array there is: two positions. Used wherever the subject is
    // something else entirely — the recipe, the method label, the rules a SET lives
    // under — so those tests do not quietly depend on a lone microphone being
    // accepted as a spatial average, which it is not.
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

        // The consumers stay blind to the method, but a SET is judged on it: the
        // two families are levelled differently and may not be mixed.
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
        // Unlike the frequency-response view, a consumer of this document wants the
        // driver's response rather than the microphones' colouring, so there is no
        // switch: the calibration is always applied, each microphone through its own.
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

        // A mixed array averages correctly all the same, but no single curve
        // describes what was applied — claiming one would let a reader "undo" a
        // correction that was never uniform.
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

        // A swept transfer magnitude is not an absolute level and must not claim to
        // be one, and it carries no analyzer settings to invent.
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

        // Changes nothing about the arithmetic — the consumers are blind to it —
        // but two channels averaged over different arrays are two different
        // questions asked of the listening volume, and the panel says so.
        Assert.Equal(3, document.Recipe.MicrophoneCount);
        Assert.Equal("Array of 3 microphones", document.Title);
    }

    [Fact]
    public void ASetOfArraysNeedsNoMatchingAnalyzerRecipe()
    {
        // Two channels measured minutes apart, each its own "session". For a moving
        // microphone that would demand an SPL anchor; for an array the loopback each
        // measurement carries has already held their levels together.
        LiveCaptureDocument first = Create(Pair(70.0));
        LiveCaptureDocument second = Create(Pair(64.0));
        Assert.NotEqual(first.CaptureSessionId, second.CaptureSessionId);
        Assert.Null(first.Recipe.SplAnchorOffsetDb);

        Assert.True(LiveCaptureDocument.JudgeSet([first, second]).Coherent);
    }

    [Fact]
    public void ASetOfArraysAcceptsChannelsFilteredDifferently()
    {
        // A protective high-pass describes the CHANNEL's own hardware path: a tweeter
        // has one and a subwoofer does not, which is the ordinary four-way car. Each
        // array has its own divided back out per position before anything is
        // averaged, so two channels filtered differently are on the same footing
        // afterwards — and where the compensation could not reach, the curve carries
        // NaN and the channel's measured band says so.
        //
        // This test used to assert the opposite, with a comment explaining why. The
        // rule it pinned refused an ordinary set for the one difference that was
        // physically correct, and the SAME lesson is written a hundred lines above it
        // for moving-microphone captures, where comparing the filter had already been
        // tried and reverted.
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
            [new ArrayMicrophoneCurve(0, true, new double[16], 1, [])],
            48_000,
            null));
    }
}
