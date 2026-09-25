using System.Numerics;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class RecordedSweepImportTests
{
    private const int SampleRate = 48_000;

    // 10 ms by convention: the recorder's start offset is not a measured time.
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

    private static RecordedSweepImport Import(float[] recording) =>
        ExpSweepMeasurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

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

    [Theory]
    [InlineData(2_400)]
    [InlineData(40_000)]
    public void ImportPlacesTheArrivalAtItsOwnReference(int startOffset)
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, startOffset);

        MeasurementResult result = Import(recording).Result;

        Assert.Equal(SweepMeasurementMode.LoopbackTransfer, result.MeasurementMode);
        Assert.NotNull(result.Transfer);
        Assert.Equal(Arrival, result.Transfer!.PeakIndex);
        Assert.Equal(1, result.AcceptedAverageRunCount);
    }

    [Fact]
    public void ProtectiveHighPassCompensationKeepsTheImportedArrivalAtItsOwnReference()
    {
        var protectiveHighPass = new ProtectiveHighPassConfiguration(
            ProtectiveHighPassKind.LinkwitzRiley,
            250,
            48);
        SweepMeasurementConfiguration configuration = Configuration() with
        {
            ProtectiveHighPass = protectiveHighPass
        };
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(configuration);
        float[] recording = RecordSweep(measurement, startOffset: 2_400);
        ApplyHighPass(recording, protectiveHighPass.ToEdge());

        MeasurementResult result =
            ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, SampleRate).Result;

        Assert.Equal(TimingReference.RecordedSweep, result.TimingReference);
        Assert.Equal(Arrival, result.Transfer!.PeakIndex);
        Assert.Equal(
            Arrival,
            Array.IndexOf(
                result.Transfer.ImpulseResponse,
                result.Transfer.ImpulseResponse.MaxBy(sample => sample.Magnitude)));
    }

    [Fact]
    public void AnImportStatesTheProtectiveHighPassItDividedOut()
    {
        var protectiveHighPass = new ProtectiveHighPassConfiguration(ProtectiveHighPassKind.LinkwitzRiley, 250, 48);
        SweepMeasurementConfiguration filtered = Configuration() with { ProtectiveHighPass = protectiveHighPass };
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] plain = RecordSweep(measurement, startOffset: 2_400);
        float[] throughTheFilter = RecordSweep(measurement, startOffset: 2_400);
        ApplyHighPass(throughTheFilter, protectiveHighPass.ToEdge());

        Assert.Equal(
            ProtectiveHighPassConfiguration.Normalize(protectiveHighPass),
            ExpSweepMeasurement.ImportRecordedSweep(filtered, throughTheFilter, SampleRate).Result.ProtectiveHighPass);
        Assert.Equal(ProtectiveHighPassConfiguration.Off, Import(plain).Result.ProtectiveHighPass);
    }

    // A dead input is rarely silent: hum on it can out-measure a quiet microphone.
    [Fact]
    public void ImportMeasuresTheChannelThatMatchesRatherThanTheLoudest()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] sweepChannel = RecordSweep(measurement, 1_500, gain: 0.02f);
        var humChannel = new float[sweepChannel.Length];
        for (int i = 0; i < humChannel.Length; i++)
        {
            humChannel[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 50.0 * i / SampleRate));
        }

        RecordedSweepImport import = ExpSweepMeasurement.ImportRecordedSweep(
            Configuration(), [humChannel, sweepChannel], SampleRate);

        Assert.Equal(1, import.Channel);
        Assert.Equal(Arrival, import.Result.Transfer!.PeakIndex);
    }

    [Fact]
    public void ADeadInputIsNoCompetitionForTheChannelHoldingTheTake()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] sweepChannel = RecordSweep(measurement, 1_500, gain: 0.02f);
        var humChannel = new float[sweepChannel.Length];
        for (int i = 0; i < humChannel.Length; i++)
        {
            humChannel[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 50.0 * i / SampleRate));
        }

        double[] qualities =
            RecordedSweepChannels.Rank(Configuration(), [humChannel, sweepChannel]);

        Assert.Equal(1, RecordedSweepChannels.Best(qualities));
        Assert.False(
            RecordedSweepChannels.IsAmbiguous(qualities),
            $"hum {qualities[0]:0.000} against the take {qualities[1]:0.000}");
    }

    // A DAW reference track is a copy of the excitation: it matches best and would measure flat, so the import must not choose.
    [Fact]
    public void AReferenceTrackBesideTheMicrophoneIsNotTheImportsChoiceToMake()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] reference = RecordSweep(measurement, 1_500, gain: 0.5f);
        float[] sweep = measurement.Sweep!.SweepData;
        var microphone = new float[reference.Length];
        var random = new Random(77);
        for (int i = 0; i < microphone.Length; i++)
        {
            microphone[i] = (float)((random.NextDouble() - 0.5) * 1e-3);
        }
        int[] taps = [1_500 + 144, 1_500 + 346, 1_500 + 677];
        float[] gains = [0.35f, -0.18f, 0.11f];
        for (int tap = 0; tap < taps.Length; tap++)
        {
            for (int i = 0; i < sweep.Length && taps[tap] + i < microphone.Length; i++)
            {
                microphone[taps[tap] + i] += sweep[i] * gains[tap];
            }
        }

        double[] qualities =
            RecordedSweepChannels.Rank(Configuration(), [reference, microphone]);

        Assert.Equal(0, RecordedSweepChannels.Best(qualities));
        Assert.True(
            RecordedSweepChannels.IsAmbiguous(qualities),
            $"reference {qualities[0]:0.000} against the microphone {qualities[1]:0.000}");
        RecordedSweepImport import = ExpSweepMeasurement.ImportRecordedSweep(
            Configuration(), [reference, microphone], SampleRate, channel: 1);
        Assert.Equal(1, import.Channel);
        Assert.Equal(Arrival, import.Result.Transfer!.PeakIndex);
    }

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

        MeasurementImpulseResponse imported = Import(recording).Result.Transfer!;

        Complex[] transfer = imported.ImpulseResponse;
        int peak = imported.PeakIndex;
        Assert.Equal(Arrival, peak);
        double directLevel = Math.Abs(transfer[peak].Real);
        double reflectionLevel = Math.Abs(transfer[peak + reflectionDelay].Real);
        Assert.Equal(reflectionGain, reflectionLevel / directLevel, tolerance: 0.02);
    }

    // A second excitation in the analyzed stretch reads as a huge reflection of the first; each take has its own reflection.
    [Fact]
    public void ASecondLouderAttemptDoesNotCostTheCompleteTake()
    {
        const int lead = 2_000;
        const int firstReflection = 300;
        const int secondReflection = 700;
        const float reflectionGain = 0.5f;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] sweep = measurement.Sweep!.SweepData;
        int second = lead + sweep.Length + (SampleRate / 4);
        var recording = new float[second + (int)(sweep.Length * 0.85)];

        for (int i = 0; i < sweep.Length; i++)
        {
            recording[lead + i] += sweep[i] * 0.14f;
            recording[lead + firstReflection + i] += sweep[i] * 0.14f * reflectionGain;
        }
        for (int i = 0; i < sweep.Length && second + i < recording.Length; i++)
        {
            recording[second + i] += sweep[i];
        }
        for (int i = 0; i < sweep.Length && second + secondReflection + i < recording.Length; i++)
        {
            recording[second + secondReflection + i] += sweep[i] * reflectionGain;
        }

        MeasurementImpulseResponse imported = Import(recording).Result.Transfer!;

        Complex[] transfer = imported.ImpulseResponse;
        int peak = imported.PeakIndex;
        Assert.Equal(Arrival, peak);
        double direct = Math.Abs(transfer[peak].Real);
        Assert.Equal(
            reflectionGain,
            Math.Abs(transfer[peak + firstReflection].Real) / direct,
            tolerance: 0.05);
        Assert.True(
            Math.Abs(transfer[peak + secondReflection].Real) / direct < 0.1,
            $"the second take's reflection came through at {Math.Abs(transfer[peak + secondReflection].Real) / direct:0.000}");
    }

    // Minutes of silence must not reach the FFTs (they would size every spectrum and the stored IR).
    [Fact]
    public void ALongTakeIsAnalyzedAroundTheExcitationOnly()
    {
        const int lead = 45 * SampleRate;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, lead, tail: 45 * SampleRate);

        MeasurementResult result = Import(recording).Result;

        // The analyzed span is the sweep plus a 0.5 s lead-in and a 2 s tail; the stored IRs are sized by it.
        int stored = result.SweepDeconvolution.ImpulseResponse.Length;
        Assert.True(stored < 10 * SampleRate, $"stored {stored} samples of a {recording.Length}-sample take");
        Assert.Equal(Arrival, result.Transfer!.PeakIndex);
    }

    // The interference is longer than the sweep, so it ranks first; the import must fall through to the next candidate.
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

        MeasurementResult result = Import(recording).Result;

        Assert.Equal(Arrival, result.Transfer!.PeakIndex);
    }

    // The configuration describes the sweep in the file; the engine keeps describing the next run.
    [Fact]
    public void AnImportLeavesTheEngineAsItWas()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        ExponentialSineSweep configured = measurement.Sweep!;
        float[] recording = RecordSweep(measurement, 800);

        Assert.Throws<InvalidOperationException>(() => Import(Noise(measurement)));
        _ = ExpSweepMeasurement.ImportRecordedSweep(Configuration(), recording, SampleRate);

        Assert.Same(configured, measurement.Sweep);
        Assert.Equal(200, measurement.LowFrequencyHz);
        Assert.False(measurement.InProgress);
    }

    // Separate crystals or an inexact per-octave duration smear the deconvolution, and no gate refuses it.
    [Theory]
    [InlineData(-500.0)]
    [InlineData(-100.0)]
    [InlineData(-50.0)]
    [InlineData(50.0)]
    [InlineData(500.0)]
    public void ImportFindsAndCorrectsARecordingOutOfScale(double ppm)
    {
        // 50 ppm of a 2 s sweep is five samples.
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

        RecordedSweepImport import =
            ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, SampleRate);

        Assert.NotNull(import.TimeScalePpm);
        // The lattice is 12.5 ppm but the objective is flat near the optimum; measured accuracy is +-40 ppm.
        Assert.Equal(ppm, import.TimeScalePpm!.Value, tolerance: 50.0);
        double sharpness = TransferIrDiagnostics.MeasureArrivalSharpnessDb(
            import.Result.Transfer!.ImpulseResponse, SampleRate) ?? double.NaN;
        Assert.True(sharpness >= 20.0, $"sharpness after correction was {sharpness:0.0} dB");
    }

    // Drift built by an independent sinc resampler: generating and correcting with FillStretched proves only self-inversion.
    [Theory]
    [InlineData(-300.0)]
    [InlineData(150.0)]
    public void ImportFindsAScaleBuiltByAResampler(double ppm)
    {
        // Kept clear of Nyquist, where a 32-tap interpolator is honest.
        var configuration = new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(20, 15_000, SampleRate, 24, 2.0, PlaybackChannel.Mono),
            Configuration().Audio,
            Configuration().Averaging);
        using var played = new ExponentialSineSweep();
        played.FillData(20, 15_000, 2.0, 24, SampleRate);
        float[] excitation = Resample(played.SweepData, 1.0 + ppm * 1e-6);
        var recording = new float[SampleRate / 2 + excitation.Length + SampleRate];
        for (int i = 0; i < excitation.Length; i++)
        {
            recording[SampleRate / 2 + i] = excitation[i] * 0.35f;
        }

        RecordedSweepImport import =
            ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, SampleRate);

        Assert.NotNull(import.TimeScalePpm);
        Assert.Equal(ppm, import.TimeScalePpm!.Value, tolerance: 50.0);
    }

    private static float[] Resample(float[] source, double scale)
    {
        const int half = 16;
        int count = (int)Math.Round(source.Length * scale);
        var result = new float[count];
        for (int n = 0; n < count; n++)
        {
            double position = n / scale;
            int centre = (int)Math.Floor(position);
            double fraction = position - centre;
            double sum = 0;
            for (int k = -half + 1; k <= half; k++)
            {
                int index = centre + k;
                if ((uint)index >= (uint)source.Length)
                {
                    continue;
                }

                double x = fraction - k;
                double sinc = Math.Abs(x) < 1e-12
                    ? 1.0
                    : Math.Sin(Math.PI * x) / (Math.PI * x);
                double window = 0.5 * (1.0 + Math.Cos(Math.PI * x / half));
                sum += source[index] * sinc * window;
            }

            result[n] = (float)sum;
        }

        return result;
    }

    private static void ApplyHighPass(float[] samples, CrossoverEdge edge)
    {
        foreach (BiquadCoefficients section in CrossoverFilter.BuildSections(
            edge,
            highPass: true,
            SampleRate))
        {
            double x1 = 0;
            double x2 = 0;
            double y1 = 0;
            double y2 = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double x = samples[i];
                double y = section.B0 * x + section.B1 * x1 + section.B2 * x2 +
                    section.A1 * y1 + section.A2 * y2;
                samples[i] = (float)y;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = y;
            }
        }
    }

    [Fact]
    public void ImportReportsNoScaleCorrectionWhenNoneIsNeeded()
    {
        SweepMeasurementConfiguration configuration = LongConfiguration();
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(configuration);
        float[] recording = RecordSweep(measurement, SampleRate / 2);

        Assert.Null(ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, SampleRate).TimeScalePpm);
    }

    private static SweepMeasurementConfiguration LongConfiguration() =>
        new(new SweepSignalConfiguration(20, 20_000, SampleRate, 24, 2.0, PlaybackChannel.Mono),
            Configuration().Audio,
            Configuration().Averaging);

    // The sweep file is bit-identical to the reference, which the live path treats as a duplicated mono input.
    [Fact]
    public void ImportOfTheSweepItselfMeasuresAnUndelayedPath()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] exported = measurement.Sweep!.SweepData.ToArray();

        Assert.Equal(Arrival, Import(exported).Result.Transfer!.PeakIndex);
    }

    [Fact]
    public void ImportMetersTheRecordingButClaimsNoLoopback()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();

        MeasurementResult result = Import(RecordSweep(measurement, 1_000)).Result;

        Assert.True(result.Levels.Microphone.Available);
        Assert.False(result.Levels.Loopback.Available);
    }

    // The reference is generated, so the recording gain multiplies the whole transfer (unlike live H1).
    [Fact]
    public void ImportTimingIsLevelIndependentButItsMagnitudeIsNot()
    {
        using ExpSweepMeasurement loud = CreateMeasurement();
        using ExpSweepMeasurement quiet = CreateMeasurement();

        MeasurementImpulseResponse loudTransfer = Import(RecordSweep(loud, 1_500, gain: 0.5f)).Result.Transfer!;
        MeasurementImpulseResponse quietTransfer = Import(RecordSweep(quiet, 1_500, gain: 0.05f)).Result.Transfer!;

        Assert.Equal(quietTransfer.PeakIndex, loudTransfer.PeakIndex);
        double loudPeak = Math.Abs(loudTransfer.ImpulseResponse[loudTransfer.PeakIndex].Real);
        double quietPeak = Math.Abs(quietTransfer.ImpulseResponse[quietTransfer.PeakIndex].Real);
        Assert.Equal(10.0, loudPeak / quietPeak, tolerance: 0.1);
    }

    // The timing origin must survive the round trip: it guards against comparing delays across clocks.
    [Fact]
    public async Task ImportedMeasurementSurvivesASaveAndLoadRoundTrip()
    {
        const int startOffset = 900;
        using ExpSweepMeasurement measurement = CreateMeasurement();
        MeasurementResult result = Import(RecordSweep(measurement, startOffset)).Result;
        Assert.Equal(TimingReference.RecordedSweep, result.TimingReference);
        string path = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-imported-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            await ImpulseResponseFile.From(result).SaveAsync(path);
            ImpulseResponseFile reloaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.Equal(SweepMeasurementMode.LoopbackTransfer, reloaded.MeasurementMode);
            Assert.Equal(TimingReference.RecordedSweep, reloaded.TimingReference);
            Assert.Equal(SampleRate, reloaded.SampleRate);
            Assert.Equal(Arrival, reloaded.TransferPeakIndex!.Value);
            Assert.Equal(TimingReference.RecordedSweep, reloaded.ToResult().TimingReference);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The file and the analyzed span are longer than the sweep; only counting from the excitation start catches it.
    [Fact]
    public void ImportRefusesATakeThatRunsOutMidSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        int sweepSamples = measurement.Sweep!.SweepSamples;
        float[] full = RecordSweep(measurement, SampleRate / 2, tail: 0);
        float[] truncated = full[..(SampleRate / 2 + (int)(sweepSamples * 0.85))];
        Assert.True(truncated.Length > sweepSamples);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ExpSweepMeasurement.ImportRecordedSweep(Configuration(), truncated, SampleRate));

        Assert.Contains("cut short", exception.Message);
    }

    // A clipped sweep still deconvolves compactly, so the shape gate cannot catch it.
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
            ExpSweepMeasurement.ImportRecordedSweep(Configuration(), recording, SampleRate));

        Assert.Contains("clipped", exception.Message);
    }

    [Fact]
    public void ImportRefusesARecordingAtAnotherSampleRate()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] recording = RecordSweep(measurement, 1_000);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ExpSweepMeasurement.ImportRecordedSweep(Configuration(), recording, 44_100));

        Assert.Contains("44100 Hz", exception.Message);
        Assert.Contains("48000 Hz", exception.Message);
    }

    [Fact]
    public void ImportRefusesARecordingShorterThanTheSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();
        float[] truncated = RecordSweep(measurement, 0)[..(measurement.Sweep!.SweepSamples / 2)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ExpSweepMeasurement.ImportRecordedSweep(Configuration(), truncated, SampleRate));

        Assert.Contains("cannot hold the whole excitation", exception.Message);
    }

    // On field takes a 5 % pace mismatch scored as high for compactness as the correct one.
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
            ExpSweepMeasurement.ImportRecordedSweep(mismatched, recording, SampleRate));

        // Either gate may speak: here the shape gate fails too; on field takes only arrival sharpness did.
        Assert.Contains("per-octave time", exception.Message);
    }

    [Fact]
    public void ImportRefusesARecordingThatIsNotThisSweep()
    {
        using ExpSweepMeasurement measurement = CreateMeasurement();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ExpSweepMeasurement.ImportRecordedSweep(Configuration(), Noise(measurement), SampleRate));

        Assert.Contains("not a recording of this sweep", exception.Message);
    }
}
