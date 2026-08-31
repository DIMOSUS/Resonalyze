using System.Numerics;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What a measurement file carries about its array, and about the calibration
/// the microphone was read through. Both are additive sections: an older build
/// must still open a file this one writes, so the format version does NOT move.
/// </summary>
public sealed class ArrayMicrophoneFileTests
{
    private const int SampleRate = 44_100;

    private static VirtualCrossoverCalibrationSettings Calibration(
        string name,
        double correctionDb) =>
        new()
        {
            Name = name,
            FileName = name + ".txt",
            Points =
            [
                [20.0, correctionDb],
                [1_000.0, correctionDb],
                [20_000.0, correctionDb]
            ]
        };

    private static ExpSweepMeasurement CreateMeasurement(
        IReadOnlyList<int>? arrayChannels = null,
        IReadOnlyList<ArrayMicrophoneMetadata>? metadata = null,
        VirtualCrossoverCalibrationSettings? microphoneCalibration = null)
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal,
                (_, s, tail, _) => Task.FromResult(
                    SyntheticCapture.WithArray(s, tail, 0.25f, 0.125f))));
        var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(20, 20_000, SampleRate, 24, 0.2, PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1,
                WaveArrayInputChannelOffsets: arrayChannels),
            new SweepAveragingConfiguration(1)));
        measurement.MicrophoneCalibration = microphoneCalibration;
        measurement.ArrayMicrophoneMetadata = metadata ?? [];
        return measurement;
    }

    [Fact]
    public async Task TheArrayMakesTheRoundTrip()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement(
            arrayChannels: [2, 3],
            metadata:
            [
                new ArrayMicrophoneMetadata(2, "left ear", Calibration("ecm-a", -1.5)),
                new ArrayMicrophoneMetadata(3, null, null)
            ],
            microphoneCalibration: Calibration("main", -0.5));
        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            await file.SaveAsync(path);
            ImpulseResponseFile reloaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.NotNull(reloaded.ArrayMicrophones);
            Assert.Equal(3, reloaded.ArrayMicrophones!.Microphones.Count);
            Assert.Equal(SpatialAverage.GridStartHz, reloaded.ArrayMicrophones.GridStartHz, 6);
            Assert.Equal(SpatialAverage.GridStopHz, reloaded.ArrayMicrophones.GridStopHz, 6);

            ImpulseResponseFile.ArrayMicrophoneFileEntry main =
                reloaded.ArrayMicrophones.Microphones[0];
            Assert.True(main.IsMeasurementMicrophone);
            Assert.Equal("main", main.Calibration!.Name);

            ImpulseResponseFile.ArrayMicrophoneFileEntry first =
                reloaded.ArrayMicrophones.Microphones[1];
            Assert.Equal(2, first.ChannelOffset);
            Assert.Equal("left ear", first.Note);
            Assert.Equal("ecm-a", first.Calibration!.Name);
            Assert.Equal(1, first.AcceptedRunCount);
            Assert.Equal(SpatialAverage.GridBandCount, first.LevelsDb.Length);

            // An uncalibrated microphone is a legitimate entry, not a defect: a
            // position still says something true without a calibration file.
            Assert.Null(reloaded.ArrayMicrophones.Microphones[2].Calibration);

            // The curves survive to the decibel.
            for (int band = 0; band < first.LevelsDb.Length; band++)
            {
                Assert.Equal(
                    measurement.ArrayMicrophones[1].LevelsDb[band],
                    first.LevelsDb[band],
                    9);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheCalibrationIsCarriedAsACurveAndNotAsAnId()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement(
            microphoneCalibration: Calibration("ECM8000 0°", -2.0));
        measurement.RestoreImpulseResponse(
            20, 20_000, SampleRate, 24, 1.0, PlaybackChannel.Mono,
            [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        measurement.MeasurementMicrophoneCalibration = Calibration("ECM8000 0°", -2.0);

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        // Two machines mint their own calibration ids, so an id alone identifies
        // nothing on the recipient's side. The points are what decide.
        Assert.NotNull(file.MicrophoneCalibration);
        Assert.Equal("ECM8000 0°", file.MicrophoneCalibration!.Name);
        Assert.Equal(3, file.MicrophoneCalibration.Points.Count);
        Assert.Equal(-2.0, file.MicrophoneCalibration.Points[1][1]);

        CalibrationFile restored = file.MicrophoneCalibration.ToCalibrationFile();
        Assert.Equal(-2.0, restored.GetDecibelCorrection(1_000.0), 6);
    }

    [Fact]
    public async Task AMeasurementWithoutAnArrayWritesNeitherSection()
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(20, 20_000, SampleRate, 24, 0.2, PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(1)));
        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.Null(file.ArrayMicrophones);
        Assert.Null(file.MicrophoneCalibration);
    }

    [Fact]
    public async Task TheFormatVersionDoesNotMoveForTheseSections()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement(
            arrayChannels: [2],
            metadata: [new ArrayMicrophoneMetadata(2, null, null)],
            microphoneCalibration: Calibration("main", 0.0));
        Assert.True(await measurement.RunAsync(), measurement.LastError?.ToString());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        // Both sections are additive and optional. Bumping the version would make
        // every file this build writes unreadable to an older one, over metadata
        // that older one would have ignored. (Version 8 was minted later by the
        // base64 float32 sample representation — a change to existing fields, so
        // it DID have to bump — not by these sections.)
        Assert.Equal(8, file.Version);
        Assert.Equal(8, ImpulseResponseFile.CurrentVersion);
    }

    [Fact]
    public async Task AFileWrittenWithoutAnArrayStillLoads()
    {
        // The compatibility direction that matters in practice: yesterday's file
        // opened by today's build.
        using ExpSweepMeasurement measurement = CreateMeasurement();
        measurement.RestoreImpulseResponse(
            20, 20_000, SampleRate, 24, 1.0, PlaybackChannel.Mono,
            [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            await file.SaveAsync(path);
            string json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("arrayMicrophones", json);
            Assert.DoesNotContain("microphoneCalibration", json);

            ImpulseResponseFile reloaded = await ImpulseResponseFile.LoadAsync(path);
            Assert.Null(reloaded.ArrayMicrophones);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnEmptyArraySectionIsRefused()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        measurement.RestoreImpulseResponse(
            20, 20_000, SampleRate, 24, 1.0, PlaybackChannel.Mono,
            [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);
        file.ArrayMicrophones = new ImpulseResponseFile.ArrayMicrophonesFileEntry
        {
            GridStartHz = 20,
            GridStopHz = 20_000
        };
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        await Assert.ThrowsAsync<InvalidDataException>(() => file.SaveAsync(path));
    }
}
