using System.Numerics;
using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// Importing a sweep recorded outside Resonalyze: the file's loudest channel is
/// what gets measured, and the analysis runs against the configured sweep
/// standing in for the loopback reference.
/// </summary>
public sealed class RecordedSweepImportTests
{
    private const int SampleRate = 48_000;

    private static SweepMeasurementConfiguration Configuration() =>
        new(new SweepSignalConfiguration(
                200,
                5_000,
                SampleRate,
                24,
                0.2,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(1));

    private static ExpSweepMeasurement CreateMeasurement()
    {
        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(Configuration());
        return measurement;
    }

    // A recording of the sweep as a recorder would hold it: the excitation
    // starts once the recorder is already running, at some attenuation, and the
    // file keeps running after it stops.
    private static float[] RecordSweep(
        ExpSweepMeasurement measurement,
        int startOffset,
        float gain = 0.3f,
        int tail = 4_096)
    {
        float[] sweep = measurement.Sweep!.SweepData;
        var recording = new float[startOffset + sweep.Length + tail];
        for (int i = 0; i < sweep.Length; i++)
        {
            recording[startOffset + i] = sweep[i] * gain;
        }

        return recording;
    }

    // A file that is not a recording of this sweep at all.
    private static float[] Noise(ExpSweepMeasurement measurement)
    {
        var noise = new float[measurement.Sweep!.SweepSamples + 4_096];
        var random = new Random(20260810);
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = (float)(random.NextDouble() * 0.2 - 0.1);
        }

        return noise;
    }

    [Fact]
    public void ImportProducesATransferIrAtTheRecordedArrival()
    {
        const int startOffset = 2_400;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, startOffset);

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(SweepMeasurementMode.LoopbackTransfer, measurement.MeasurementMode);
        Assert.NotNull(measurement.Transfer);
        // The whole path is a pure delay, so the transfer IR is one arrival at
        // the offset the excitation entered the recording at.
        Assert.InRange(measurement.Transfer!.PeakIndex, startOffset - 2, startOffset + 2);
        Assert.Equal(1, measurement.AcceptedAverageRunCount);
    }

    // The point of the transfer estimate: what comes back is the PATH the sweep
    // travelled, not just where it started. A recording of the sweep through a
    // direct arrival plus one reflection must come back as those two arrivals,
    // at their spacing and their relative strength.
    [Fact]
    public void ImportRecoversThePathTheSweepTravelled()
    {
        const int startOffset = 1_200;
        const int reflectionDelay = 300;
        const float reflectionGain = 0.5f;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] direct = RecordSweep(measurement, startOffset);
        float[] echoed = RecordSweep(measurement, startOffset + reflectionDelay);
        var recording = new float[direct.Length + reflectionDelay];
        for (int i = 0; i < direct.Length; i++)
        {
            recording[i] += direct[i];
        }
        for (int i = 0; i < echoed.Length; i++)
        {
            recording[i] += echoed[i] * reflectionGain;
        }

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Complex[] transfer = measurement.TransferImpulseResponse!;
        int peak = measurement.Transfer!.PeakIndex;
        Assert.InRange(peak, startOffset - 2, startOffset + 2);
        double directLevel = Math.Abs(transfer[peak].Real);
        double reflectionLevel = Math.Abs(transfer[peak + reflectionDelay].Real);
        Assert.Equal(reflectionGain, reflectionLevel / directLevel, tolerance: 0.02);
    }

    // A take made the way people actually make them: recorder started, walk to the
    // seat, play the sweep, walk back, stop. The minutes of silence must not reach
    // the FFTs — they would size every spectrum and the stored transfer IR with
    // them — and the path must still come back correctly.
    [Fact]
    public void ALongTakeIsAnalyzedAroundTheExcitationOnly()
    {
        const int lead = 45 * SampleRate;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        int sweepSamples = measurement.Sweep!.SweepSamples;
        float[] recording = RecordSweep(measurement, lead, tail: 45 * SampleRate);

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        // Bounded by the sweep plus the 0.5 s lead-in and 2 s tail the window
        // keeps, not by the 92 seconds of file it came from.
        Assert.True(
            measurement.MicrophoneRecordedSamples!.Length <= sweepSamples + (int)(2.5 * SampleRate),
            $"analyzed {measurement.MicrophoneRecordedSamples.Length} samples of {recording.Length}");
        // The excitation sits behind the kept lead-in, wherever it was in the file.
        Assert.InRange(measurement.Transfer!.PeakIndex, SampleRate / 2 - 480, SampleRate / 2 + 480);
    }

    // The obvious sanity check a user runs first: import the exported sweep file
    // itself. It is bit-identical to the reference, which the live path treats as
    // a duplicated mono input — an import must simply measure a flat, undelayed
    // path instead of refusing.
    [Fact]
    public void ImportOfTheSweepItselfMeasuresAnUndelayedPath()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] exported = measurement.Sweep!.SweepData.ToArray();

        measurement.ImportRecordedSweep(Configuration(), exported, SampleRate);

        Assert.True(measurement.HasImpulseResponse);
        Assert.InRange(measurement.Transfer!.PeakIndex, 0, 2);
    }

    [Fact]
    public void ImportKeepsTheRecordingButClaimsNoLoopback()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 1_000);

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Assert.Equal(recording, measurement.MicrophoneRecordedSamples);
        // The reference is generated, not captured: it must not be reported as a
        // recorded loopback channel or metered as an input.
        Assert.Null(measurement.LoopbackRecordedSamples);
        Assert.True(measurement.CurrentLevels.Microphone.Available);
        Assert.False(measurement.CurrentLevels.Loopback.Available);
    }

    // Absolute level is the recorder's business; the transfer estimate divides it
    // out, so two takes of the same room at different gains must agree.
    [Fact]
    public void ImportIsScaleInvariant()
    {
        using ExpSweepMeasurement loud = CreateMeasurement();
        using ExpSweepMeasurement quiet = CreateMeasurement();

        loud.ImportRecordedSweep(Configuration(), RecordSweep(loud, 1_500, gain: 0.5f), SampleRate);
        quiet.ImportRecordedSweep(Configuration(), RecordSweep(quiet, 1_500, gain: 0.02f), SampleRate);

        Assert.Equal(quiet.Transfer!.PeakIndex, loud.Transfer!.PeakIndex);
    }

    // An import is a measurement like any other, so Save has to accept it: the
    // capture must validate and come back through the normal restore path.
    [Fact]
    public async Task ImportedMeasurementSurvivesASaveAndLoadRoundTrip()
    {
        const int startOffset = 900;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        measurement.ImportRecordedSweep(Configuration(), RecordSweep(measurement, startOffset), SampleRate);
        string path = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-imported-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            await ImpulseResponseFile.Capture(measurement).SaveAsync(path);
            ImpulseResponseFile reloaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.Equal(SweepMeasurementMode.LoopbackTransfer, reloaded.MeasurementMode);
            Assert.Equal(SampleRate, reloaded.SampleRate);
            Assert.InRange(reloaded.TransferPeakIndex!.Value, startOffset - 2, startOffset + 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The same promise loading a file makes: a rejected import must not take the
    // measurement already on screen down with it.
    [Fact]
    public void ARejectedImportLeavesThePreviousResultAlone()
    {
        const int startOffset = 700;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        measurement.ImportRecordedSweep(
            Configuration(),
            RecordSweep(measurement, startOffset),
            SampleRate);
        int peakBefore = measurement.Transfer!.PeakIndex;
        Complex[] impulseResponseBefore = measurement.SweepDeconvolutionImpulseResponse!;

        Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), Noise(measurement), SampleRate));

        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(peakBefore, measurement.Transfer!.PeakIndex);
        Assert.Same(impulseResponseBefore, measurement.SweepDeconvolutionImpulseResponse);
    }

    [Fact]
    public void ImportRefusesARecordingAtAnotherSampleRate()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 1_000);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), recording, 44_100));

        Assert.Contains("44100 Hz", exception.Message);
        Assert.Contains("48000 Hz", exception.Message);
        Assert.False(measurement.HasImpulseResponse);
    }

    [Fact]
    public void ImportRefusesARecordingShorterThanTheSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] truncated = RecordSweep(measurement, 0)[..(measurement.Sweep!.SweepSamples / 2)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), truncated, SampleRate));

        Assert.Contains("cannot hold the whole excitation", exception.Message);
        Assert.False(measurement.HasImpulseResponse);
    }

    // The honest refusal for the wrong file: noise deconvolves into nothing that
    // looks like an impulse response, and the message has to say so instead of
    // publishing the garbage.
    [Fact]
    public void ImportRefusesARecordingThatIsNotThisSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), Noise(measurement), SampleRate));

        Assert.Contains("not a recording of this sweep", exception.Message);
        Assert.False(measurement.HasImpulseResponse);
    }
}
