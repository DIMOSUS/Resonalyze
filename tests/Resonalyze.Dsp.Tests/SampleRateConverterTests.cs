namespace Resonalyze.Dsp.Tests;

public sealed class SampleRateConverterTests
{
    [Fact]
    public void Resample_EqualRatesReturnsACopy()
    {
        float[] input = [0.1f, -0.5f, 0.9f];

        float[] output = SampleRateConverter.Resample(input, 48_000, 48_000);

        Assert.Equal(input, output);
        Assert.NotSame(input, output);
    }

    [Fact]
    public void Resample_EmptyInputStaysEmpty()
    {
        Assert.Empty(SampleRateConverter.Resample(
            Array.Empty<float>(), 44_100, 48_000));
    }

    [Theory]
    [InlineData(44_100, 48_000)]
    [InlineData(48_000, 44_100)]
    [InlineData(96_000, 48_000)]
    [InlineData(48_000, 96_000)]
    public void Resample_PreservesAMidbandTone(int fromRate, int toRate)
    {
        // A 1 kHz tone sits far below either Nyquist, so it must pass with its
        // amplitude and phase intact — the whole point of the converter being
        // time-aligned. The edges carry the kernel's ramp-in, so the check reads
        // the interior.
        const double Frequency = 1_000.0;
        int length = fromRate / 2;
        var input = new float[length];
        for (int i = 0; i < length; i++)
        {
            input[i] = (float)Math.Sin(2.0 * Math.PI * Frequency * i / fromRate);
        }

        float[] output = SampleRateConverter.Resample(input, fromRate, toRate);

        // ceil(length · to / from): one second of material stays one second.
        Assert.Equal(
            ((long)length * toRate + fromRate - 1) / fromRate,
            output.Length);
        int margin = toRate / 20;
        double maxError = 0;
        for (int i = margin; i < output.Length - margin; i++)
        {
            double expected = Math.Sin(2.0 * Math.PI * Frequency * i / toRate);
            maxError = Math.Max(maxError, Math.Abs(output[i] - expected));
        }

        // −60 dB against a full-scale tone: far beyond audibility and well
        // inside the kernel's design stopband.
        Assert.True(
            maxError < 1e-3,
            $"Tone error after {fromRate} -> {toRate}: {maxError:E2}");
    }

    [Fact]
    public void Resample_RejectsAToneAboveTheTargetNyquist()
    {
        // Downsampling 96 kHz material carrying a 30 kHz tone to 48 kHz must
        // suppress it (24 kHz Nyquist), not fold it into the audible band.
        const int FromRate = 96_000;
        const int ToRate = 48_000;
        const double Frequency = 30_000.0;
        int length = FromRate / 2;
        var input = new float[length];
        for (int i = 0; i < length; i++)
        {
            input[i] = (float)Math.Sin(2.0 * Math.PI * Frequency * i / FromRate);
        }

        float[] output = SampleRateConverter.Resample(input, FromRate, ToRate);

        int margin = ToRate / 20;
        double peak = 0;
        for (int i = margin; i < output.Length - margin; i++)
        {
            peak = Math.Max(peak, Math.Abs(output[i]));
        }

        Assert.True(peak < 1e-3, $"Alias residue: {peak:E2}");
    }

    [Fact]
    public void Resample_KeepsAnImpulseWhereItWas()
    {
        // The converter must not shift the material against the (untouched)
        // impulse responses it will be convolved with: an impulse at a known
        // time must come out at the same time in the new rate.
        const int FromRate = 44_100;
        const int ToRate = 48_000;
        var input = new float[FromRate / 4];
        int inputIndex = input.Length / 2;
        input[inputIndex] = 1.0f;

        float[] output = SampleRateConverter.Resample(input, FromRate, ToRate);

        int peakIndex = 0;
        for (int i = 1; i < output.Length; i++)
        {
            if (Math.Abs(output[i]) > Math.Abs(output[peakIndex]))
            {
                peakIndex = i;
            }
        }

        double expectedIndex = inputIndex * (double)ToRate / FromRate;
        Assert.InRange(peakIndex, expectedIndex - 1.0, expectedIndex + 1.0);
    }
}
