using System.Numerics;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Importing a sweep recorded outside Resonalyze: the file's loudest channel is
/// what gets measured, and the analysis runs against the configured sweep
/// standing in for the loopback reference.
/// </summary>
public sealed class RecordedSweepImportTests
{
    private const int SampleRate = 48_000;

    // Where an import puts its arrival: 10 ms, by convention, because the
    // recorder's start offset is not a time anybody measured.
    private const int Arrival = SampleRate / 100;

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

    // Wherever the excitation sat in the file, the published arrival lands on the
    // convention: the recorder's start offset is not a time anybody measured, and
    // leaving it in place put 730 ms of "group delay" on an axis that spans tens.
    [Theory]
    [InlineData(2_400)]
    [InlineData(40_000)]
    public void ImportPlacesTheArrivalAtItsOwnReference(int startOffset)
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, startOffset);

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(SweepMeasurementMode.LoopbackTransfer, measurement.MeasurementMode);
        Assert.NotNull(measurement.Transfer);
        Assert.Equal(Arrival, measurement.Transfer!.PeakIndex);
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
        // The shift that places the arrival is rigid, so the reflection is still
        // exactly its own delay behind the direct sound.
        Assert.Equal(Arrival, peak);
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
        Assert.Equal(Arrival, measurement.Transfer!.PeakIndex);
    }

    // Ranking candidate stretches by level cannot be certain: a burst of speech or
    // handling noise before the sweep is loud and sustained, and here it is longer
    // than the sweep, so it ranks FIRST. The import has to fall through to the
    // next candidate rather than refuse a usable recording.
    [Fact]
    public void ALouderInterferenceBeforeTheSweepDoesNotCostTheImport()
    {
        const int lead = 40 * SampleRate;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, lead, tail: 10 * SampleRate);
        var random = new Random(1234);
        for (int i = 0; i < 4 * SampleRate; i++)
        {
            recording[SampleRate + i] += (float)((random.NextDouble() - 0.5) * 0.6);
        }

        measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(Arrival, measurement.Transfer!.PeakIndex);
    }

    // The busy state has to span the DECODE too, which happens before any samples
    // exist to import: a claim taken first keeps the record button out for the
    // whole operation, and the import must run inside it rather than refuse it.
    [Fact]
    public void AClaimCoversTheDecodeAndTheImportTogether()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 900);

        using (measurement.Claim())
        {
            Assert.True(measurement.InProgress);
            // Nothing else may start a measurement while the claim is held.
            Assert.Throws<InvalidOperationException>(() => measurement.Init(Configuration()));
            // The import is what the claim was taken for, so it proceeds.
            measurement.ImportRecordedSweep(Configuration(), recording, SampleRate);
            Assert.True(measurement.HasImpulseResponse);
            Assert.True(measurement.InProgress);
        }

        Assert.False(measurement.InProgress);
        Assert.Throws<InvalidOperationException>(() =>
        {
            using IDisposable first = measurement.Claim();
            using IDisposable second = measurement.Claim();
        });
    }

    // Every other way to reconfigure the measurement gates on InProgress, so the
    // import has to hold it for its whole run — and give it back either way.
    [Fact]
    public void ImportHoldsTheMeasurementBusyAndReleasesIt()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();

        measurement.ImportRecordedSweep(
            Configuration(), RecordSweep(measurement, 800), SampleRate);
        Assert.False(measurement.InProgress);

        Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), Noise(measurement), SampleRate));
        Assert.False(measurement.InProgress);
    }

    // A recording out of scale with the sweep it is analyzed against — separate
    // crystals in the player and the recorder, or a duration the per-octave field
    // cannot express exactly. Either way the deconvolution smears, and neither the
    // shape gate nor the sharpness gate refuses it: the result just quietly loses
    // its timing and its top-end phase. The import has to find the stretch.
    [Theory]
    [InlineData(-500.0)]
    [InlineData(-100.0)]
    [InlineData(-50.0)]
    [InlineData(50.0)]
    [InlineData(500.0)]
    public void ImportFindsAndCorrectsARecordingOutOfScale(double ppm)
    {
        // Long enough for the stretch to be worth samples: 50 ppm of a two-second
        // sweep is five samples, of a fifth of a second it is half of one.
        SweepMeasurementConfiguration configuration = LongConfiguration();
        using var played = new ExponentialSineSweep();
        played.FillData(20, 20_000, configuration.Signal.RequestedDurationSeconds, 24, SampleRate);
        using var stretched = new ExponentialSineSweep();
        stretched.FillStretched(played.Spec, 1.0 + ppm * 1e-6);
        float[] excitation = stretched.SweepData;
        var recording = new float[SampleRate / 2 + excitation.Length + SampleRate];
        for (int i = 0; i < excitation.Length; i++)
        {
            recording[SampleRate / 2 + i] = excitation[i] * 0.35f;
        }

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(configuration);
        measurement.ImportRecordedSweep(configuration, recording, SampleRate);

        Assert.NotNull(measurement.ImportedTimeScalePpm);
        Assert.Equal(ppm, measurement.ImportedTimeScalePpm!.Value, tolerance: 25.0);
        // And the correction is what makes the arrival readable again.
        double sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            measurement.TransferImpulseResponse!, SampleRate) ?? double.NaN;
        Assert.True(sharpness >= 20.0, $"sharpness after correction was {sharpness:0.0} dB");
    }

    // And it does not invent one: a recording of the very sweep the settings
    // describe needs no stretch, and saying it did would be a claim about the
    // user's two devices that the data does not support.
    [Fact]
    public void ImportReportsNoScaleCorrectionWhenNoneIsNeeded()
    {
        SweepMeasurementConfiguration configuration = LongConfiguration();
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(configuration);
        float[] recording = RecordSweep(measurement, SampleRate / 2);

        measurement.ImportRecordedSweep(configuration, recording, SampleRate);

        Assert.Null(measurement.ImportedTimeScalePpm);
    }

    private static SweepMeasurementConfiguration LongConfiguration() =>
        new(new SweepSignalConfiguration(20, 20_000, SampleRate, 24, 2.0, PlaybackChannel.Mono),
            Configuration().Audio,
            Configuration().Averaging);

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
        Assert.Equal(Arrival, measurement.Transfer!.PeakIndex);
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

    // An imported measurement is NOT scale-invariant, and the difference from a
    // live run is worth pinning. There, the loopback carries the recording gain
    // too and H1 divides it out; here the reference is generated, so the gain
    // rides entirely on the target and multiplies the whole transfer. WHEN the
    // arrival happens is unaffected; WHERE the magnitude curve sits vertically is
    // the recorder's business, and two takes at different gains do not line up.
    [Fact]
    public void ImportTimingIsLevelIndependentButItsMagnitudeIsNot()
    {
        using ExpSweepMeasurement loud = CreateMeasurement();
        using ExpSweepMeasurement quiet = CreateMeasurement();

        loud.ImportRecordedSweep(Configuration(), RecordSweep(loud, 1_500, gain: 0.5f), SampleRate);
        quiet.ImportRecordedSweep(Configuration(), RecordSweep(quiet, 1_500, gain: 0.05f), SampleRate);

        Assert.Equal(quiet.Transfer!.PeakIndex, loud.Transfer!.PeakIndex);
        double loudPeak = Math.Abs(loud.TransferImpulseResponse![loud.Transfer.PeakIndex].Real);
        double quietPeak = Math.Abs(quiet.TransferImpulseResponse![quiet.Transfer.PeakIndex].Real);
        Assert.Equal(10.0, loudPeak / quietPeak, tolerance: 0.1);
    }

    // An import is a measurement like any other, so Save has to accept it: the
    // capture must validate and come back through the normal restore path. What it
    // must NOT lose on the way is where its timing came from — that is the whole
    // defence against a delay being compared against another measurement's.
    [Fact]
    public async Task ImportedMeasurementSurvivesASaveAndLoadRoundTrip()
    {
        const int startOffset = 900;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        measurement.ImportRecordedSweep(Configuration(), RecordSweep(measurement, startOffset), SampleRate);
        Assert.Equal(TimingReference.RecordedSweep, measurement.TimingReference);
        string path = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-imported-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            await ImpulseResponseFile.Capture(measurement).SaveAsync(path);
            ImpulseResponseFile reloaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.Equal(SweepMeasurementMode.LoopbackTransfer, reloaded.MeasurementMode);
            Assert.Equal(TimingReference.RecordedSweep, reloaded.TimingReference);
            Assert.Equal(SampleRate, reloaded.SampleRate);
            Assert.Equal(Arrival, reloaded.TransferPeakIndex!.Value);

            using var restored = new ExpSweepMeasurement(new FakeAudioSessionFactory());
            (double lowHz, double highHz) = reloaded.ResolveSweepBand();
            restored.RestoreImpulseResponse(
                lowHz,
                highHz,
                reloaded.SampleRate,
                reloaded.Bits,
                reloaded.SweepDurationSeconds,
                reloaded.PlayChannel,
                reloaded.GetSweepDeconvolutionImpulseResponse(),
                reloaded.SweepDeconvolutionPeakIndex,
                reloaded.MeasurementMode,
                reloaded.GetTransferImpulseResponse(),
                reloaded.TransferPeakIndex,
                reloaded.TransferCoherence,
                reloaded.AverageRunCount,
                reloaded.AcceptedAverageRunCount,
                timingReference: reloaded.TimingReference);
            Assert.Equal(TimingReference.RecordedSweep, restored.TimingReference);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A measured sweep keeps the meaning it always had, and starting a new
    // measurement clears the imported one rather than inheriting it.
    [Fact]
    public void AMeasuredSweepIsReferencedToItsOwnLoopback()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        Assert.Equal(TimingReference.SynchronizedLoopback, measurement.TimingReference);

        measurement.ImportRecordedSweep(
            Configuration(), RecordSweep(measurement, 700), SampleRate);
        Assert.Equal(TimingReference.RecordedSweep, measurement.TimingReference);

        measurement.Init(Configuration());
        Assert.Equal(TimingReference.SynchronizedLoopback, measurement.TimingReference);
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

    // The take runs out mid-sweep after a pre-roll: the FILE is longer than the
    // sweep, and so is the analyzed span, but the excitation inside it is not.
    // Only counting from where the excitation begins catches this.
    [Fact]
    public void ImportRefusesATakeThatRunsOutMidSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        int sweepSamples = measurement.Sweep!.SweepSamples;
        float[] full = RecordSweep(measurement, SampleRate / 2, tail: 0);
        // Keeps the half-second pre-roll and 85 % of the excitation, so the file
        // still holds more samples than the sweep does.
        float[] truncated = full[..(SampleRate / 2 + (int)(sweepSamples * 0.85))];
        Assert.True(truncated.Length > sweepSamples);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), truncated, SampleRate));

        Assert.Contains("cut short", exception.Message);
    }

    // Clipping is an unambiguous capture failure the live path refuses a run for,
    // and a clipped sweep still deconvolves into a compact impulse response — full
    // of harmonic products — so the shape gate cannot be what catches it.
    [Fact]
    public void ImportRefusesAClippedRecording()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 1_000, gain: 3.0f);
        for (int i = 0; i < recording.Length; i++)
        {
            recording[i] = Math.Clamp(recording[i], -1.0f, 1.0f);
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(Configuration(), recording, SampleRate));

        Assert.Contains("clipped", exception.Message);
        Assert.False(measurement.HasImpulseResponse);
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

    // A recording of a REAL sweep, analyzed against a sweep 5 % longer. It is the
    // likeliest mistake a user makes — the per-octave time is a free-text field —
    // and the shape gate alone does not catch it: on both field takes a mismatched
    // pace scored AS HIGH as the correct one for compactness while its arrival was
    // smeared over thousands of samples.
    [Fact]
    public void ImportRefusesASweepTheSettingsDoNotDescribe()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 1_200);
        SweepSignalConfiguration signal = Configuration().Signal;
        var mismatched = new SweepMeasurementConfiguration(
            signal with { RequestedDurationSeconds = signal.RequestedDurationSeconds * 1.05 },
            Configuration().Audio,
            Configuration().Averaging);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            measurement.ImportRecordedSweep(mismatched, recording, SampleRate));

        // Either gate may be the one that speaks: on this ideal synthetic take the
        // shape gate fails too, while on the field takes it passed — sometimes
        // scoring the mismatch higher than the truth — and only the arrival's
        // sharpness told them apart. Both name the setting to go and check.
        Assert.Contains("per-octave time", exception.Message);
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
