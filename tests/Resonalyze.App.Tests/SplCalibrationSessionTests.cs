using Resonalyze.Dsp;
using Resonalyze.Options;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

public sealed class SplCalibrationSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SplCalibration Existing(double level) => new() { ReferenceLevelDbSpl = level, MeasuredLevelDbFs = -30 };

    private static SplCalibrationSession Session(SplCalibration? existing = null, AudioSessionRequest? request = null) =>
        new(request ?? SplRequest(), existing, () => Now, TimeSpan.FromSeconds(1.5));

    private static FakeAudioSessionFactory Hearing(Func<IAudioStreamingSession> stream) =>
        new(streamingFactory: _ => stream());

    private static double[] Frames(double amplitude, int count = 8) => Enumerable.Repeat(amplitude, count).ToArray();

    [Theory]
    [InlineData(null, 0)]
    [InlineData(104.0, 1)]
    [InlineData(114.0, 2)]
    [InlineData(100.0, 0)]
    public void TheExistingLevelIsPickedWhenItIsAStandardOne(double? level, int index)
    {
        SplCalibrationSession session = Session(level is double value ? Existing(value) : null);

        Assert.Equal(index, session.ReferenceIndex);
        Assert.Equal(SplCalibration.StandardReferenceLevelsDb[index], session.ReferenceLevelDbSpl);
        Assert.Null(session.Status);
        Assert.False(session.Running);
    }

    [Theory]
    [InlineData(-1, 94.0)]
    [InlineData(7, 114.0)]
    public void AnIndexOffTheListReadsItsNearestLevel(int index, double level)
    {
        SplCalibrationSession session = Session();

        session.SelectReference(index);

        Assert.Equal(level, session.ReferenceLevelDbSpl);
    }

    [Fact]
    public void ACleanToneMakesAnAnchorPinnedToTheInput() => StaTest.Run(() =>
    {
        AudioSessionRequest request = SplRequest(AudioBackend.WasapiShared) with
        {
            SampleRate = 96_000,
            BitsPerSample = 32,
            Routing = new AudioCaptureRouting(0, null)
        };
        SplCalibrationSession session = Session(request: request);
        session.SelectReference(1);

        StaTest.Settle(session.RunAsync(Hearing(() => new ToneStream(1_000, Frames(0.1)))));

        SplCalibration result = Assert.IsType<SplCalibration>(session.Result);
        Assert.Equal(104.0, result.ReferenceLevelDbSpl);
        Assert.Equal(-20.0, result.MeasuredLevelDbFs, 1);
        Assert.Equal(1_000.0, result.ReferenceFrequencyHz);
        Assert.InRange(result.MeasuredFrequencyHz, 990.0, 1_010.0);
        Assert.Equal(Now, result.CapturedAtUtc);
        Assert.Equal(AudioBackend.WasapiShared, result.Backend);
        Assert.Equal(96_000, result.SampleRate);
        Assert.Equal(32, result.Bits);
        Assert.Equal(0, result.MicrophoneChannelOffset);
        Assert.Null(result.InputDeviceNumber);
        Assert.Equal("{capture}", result.WasapiCaptureEndpointId);
        Assert.Null(result.AsioDriverName);
        Assert.Equal(SplStatusTone.Success, session.Status!.Tone);
        Assert.False(session.Running);
    });

    [Fact]
    public void AnMmeAnchorKeepsItsInputDevice()
    {
        SplCalibration calibration = SplCalibrationReport.Calibration(
            SplRequest(AudioBackend.Wave),
            new SplCalibrationCaptureResult(new SplToneReading(1_000, -20, 30, true, true), -19, false, 0.01, 6, false),
            94,
            SplToneCriteria.Default,
            Now);

        Assert.Equal(1, calibration.InputDeviceNumber);
        Assert.Equal(114.0, calibration.OffsetDb, 9);
    }

    [Fact]
    public void TheLevelIsReadWhenTheListenStarts() => StaTest.Run(() =>
    {
        SplCalibrationSession session = Session();
        session.SelectReference(2);

        Task run = session.RunAsync(Hearing(() => new ToneStream(1_000, Frames(0.1))));
        session.SelectReference(0);
        StaTest.Settle(run);

        Assert.Equal(114.0, session.Result!.ReferenceLevelDbSpl);
    });

    [Fact]
    public void AFailedListenExplainsItselfAndLeavesNoResult() => StaTest.Run(() =>
    {
        using var culture = new InvariantCultureScope();
        SplCalibrationSession session = Session();

        StaTest.Settle(session.RunAsync(Hearing(() => new ToneStream(800, Frames(0.1)))));

        Assert.Null(session.Result);
        Assert.Equal(SplStatusTone.Error, session.Status!.Tone);
        Assert.Matches(@"^The loudest tone was at 80\d Hz, not 1000 Hz\. Check", session.Status.Text);
    });

    [Fact]
    public void AnInputThatWillNotOpenIsSaid() => StaTest.Run(() =>
    {
        SplCalibrationSession session = Session();

        StaTest.Settle(session.RunAsync(Hearing(() => throw new InvalidOperationException("The device is gone."))));

        Assert.Equal(
            new SplStatus("Could not open the input for calibration:\r\nThe device is gone.", SplStatusTone.Error),
            session.Status);
        Assert.False(session.Running);
    });

    [Fact]
    public void ANewListenDropsThePreviousResult() => StaTest.Run(() =>
    {
        SplCalibrationSession session = Session();
        StaTest.Settle(session.RunAsync(Hearing(() => new ToneStream(1_000, Frames(0.1)))));
        Assert.NotNull(session.Result);
        var stream = new ToneStream(1_000, Frames(0.1));

        Task run = session.RunAsync(Hearing(() => stream));

        Assert.True(session.Running);
        Assert.Null(session.Result);
        Assert.Equal(SplCalibrationReport.Listening, session.Status);
        Assert.Equal(0, session.ProgressPercent);
        StaTest.Settle(stream.Emitted.Task);
        session.Stop();
        StaTest.Settle(run);
        Assert.Null(session.Result);
        Assert.Equal(SplCalibrationReport.Cancelled, session.Status);
    });

    [Fact]
    public void ProgressIsReportedWhileListening() => StaTest.Run(() =>
    {
        using var culture = new InvariantCultureScope();
        SplCalibrationSession session = Session();
        var stream = new ToneStream(1_000, Frames(0.1));
        var heard = new List<string>();

        Task run = session.RunAsync(Hearing(() => stream), () => heard.Add(session.Status!.Text));
        StaTest.Settle(stream.Emitted.Task);
        Wait(() => heard.Count == 6);
        session.Stop();
        StaTest.Settle(run);

        Assert.All(heard, text => Assert.Matches(
            @"^Listening…   input peak -20\.0 dBFS\r\nLoudest tone: 100\d Hz at -20\.0 dBFS\r\nProminence: \d+\.\d dB$",
            text));
    });

    [Fact]
    public void AListenAlreadyRunningIsNotStartedTwice() => StaTest.Run(() =>
    {
        SplCalibrationSession session = Session();
        var stream = new ToneStream(1_000, Frames(0.1));
        var factory = Hearing(() => stream);

        Task run = session.RunAsync(factory);
        Task second = session.RunAsync(factory);

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, factory.StreamingOpenCount);
        session.Stop();
        StaTest.Settle(run);
    });

    [Fact]
    public void AClosingDialogWaitsForTheListenToStop() => StaTest.Run(() =>
    {
        SplCalibrationSession session = Session();
        Assert.True(session.RequestClose());
        var stream = new ToneStream(1_000, Frames(0.1));

        Task run = session.RunAsync(Hearing(() => stream));
        StaTest.Settle(stream.Emitted.Task);

        Assert.False(session.RequestClose());
        Assert.True(session.CloseRequested);
        StaTest.Settle(run);
        Assert.Equal(SplCalibrationReport.Cancelled, session.Status);
        Assert.True(session.RequestClose());
    });

    [Theory]
    [InlineData(SplCalibrationFailure.TooFewFrames, "The capture did not run long enough. Check the input device and try again.")]
    [InlineData(SplCalibrationFailure.Clipped, "The input clipped (peak -0.1 dBFS). Lower the input gain and calibrate again.")]
    [InlineData(SplCalibrationFailure.OffFrequency, "The loudest tone was at 950 Hz, not 1000 Hz. Check the calibrator is set to 1 kHz and seated on the capsule.")]
    [InlineData(SplCalibrationFailure.NoClearPeak, "No clear 1000 Hz tone stood out from the noise (prominence 4.5 dB). Seat the calibrator firmly and reduce ambient noise.")]
    [InlineData(SplCalibrationFailure.Unstable, "The level was unsteady (2.3 dB variation). Make sure the calibrator is fully seated and held still.")]
    [InlineData(SplCalibrationFailure.CaptureOverrun, "The capture dropped frames (processing overload), so the result cannot be trusted. Close other work and calibrate again.")]
    [InlineData(SplCalibrationFailure.None, "Calibration failed.")]
    public void EachFailureSaysWhatToDo(SplCalibrationFailure failure, string text)
    {
        using var culture = new InvariantCultureScope();
        var result = new SplCalibrationCaptureResult(new SplToneReading(950, -20, 4.5, false, false), -0.1, true, 2.3, 6, true);

        Assert.Equal(new SplStatus(text, SplStatusTone.Error), SplCalibrationReport.Failed(failure, result, SplToneCriteria.Default));
    }

    [Fact]
    public void ClippingIsFlaggedWhileListening()
    {
        using var culture = new InvariantCultureScope();
        var reading = new SplToneReading(0, -80, 0.4, false, false);

        SplStatus status = SplCalibrationReport.Progress(new SplCalibrationProgress(reading, 0.0, true, 3, 1.5));

        Assert.Equal(
            new SplStatus(
                "Listening…   input peak 0.0 dBFS\r\nLoudest tone: —\r\nProminence: 0.4 dB   ⚠ CLIPPING — lower the input gain",
                SplStatusTone.Error),
            status);
        Assert.Equal(37, SplCalibrationReport.Percent(new SplCalibrationProgress(reading, 0, false, 3, 1.5), TimeSpan.FromSeconds(4)));
        Assert.Equal(100, SplCalibrationReport.Percent(new SplCalibrationProgress(reading, 0, false, 3, 9), TimeSpan.FromSeconds(4)));
        Assert.Equal(0, SplCalibrationReport.Percent(new SplCalibrationProgress(reading, 0, false, 3, -1), TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void ASuccessStatesTheOffset()
    {
        using var culture = new InvariantCultureScope();
        var result = new SplCalibrationCaptureResult(new SplToneReading(1_001, -21.04, 40, true, true), -20, false, 0.01, 6, false);
        var calibration = new SplCalibration { ReferenceLevelDbSpl = 94, MeasuredLevelDbFs = -21.04 };

        Assert.Equal(
            new SplStatus(
                "Calibration successful.\r\n1001 Hz measured at -21.0 dBFS.\r\nOffset +115.0 dB at 94 dB SPL reference.",
                SplStatusTone.Success),
            SplCalibrationReport.Succeeded(result, calibration));
        Assert.Equal("104 dB SPL", SplCalibrationReport.ReferenceLabel(104));
    }
}
