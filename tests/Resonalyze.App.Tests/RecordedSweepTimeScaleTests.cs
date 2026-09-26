using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

[Trait("Category", "Slow")]
public sealed class RecordedSweepTimeScaleTests
{
    private const int SampleRate = 48_000;

    // Separate crystals or an inexact per-octave duration smear the deconvolution, and no gate refuses it.
    [Theory]
    [InlineData(-500.0)]
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
        var recording = new float[SampleRate / 2 + excitation.Length + SampleRate / 2];
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
            RecordedSweepImportTests.Configuration().Audio,
            RecordedSweepImportTests.Configuration().Averaging);
        using var played = new ExponentialSineSweep();
        played.FillData(20, 15_000, 2.0, 24, SampleRate);
        float[] excitation = Resample(played.SweepData, 1.0 + ppm * 1e-6);
        var recording = new float[SampleRate / 2 + excitation.Length + SampleRate / 2];
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

    [Fact]
    public void ImportReportsNoScaleCorrectionWhenNoneIsNeeded()
    {
        SweepMeasurementConfiguration configuration = LongConfiguration();
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(configuration);
        float[] recording = RecordedSweepImportTests.RecordSweep(measurement, SampleRate / 2);

        Assert.Null(ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, SampleRate).TimeScalePpm);
    }

    private static SweepMeasurementConfiguration LongConfiguration() =>
        new(new SweepSignalConfiguration(20, 20_000, SampleRate, 24, 2.0, PlaybackChannel.Mono),
            RecordedSweepImportTests.Configuration().Audio,
            RecordedSweepImportTests.Configuration().Averaging);
}
