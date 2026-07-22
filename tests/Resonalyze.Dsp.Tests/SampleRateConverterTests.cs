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
    public void Resample_KeepsTheTopOfTheAudibleBand()
    {
        // 48 → 44.1: a 20 kHz tone sits inside the design's passband (91% of
        // the 22.05 kHz Nyquist ≈ 20.07 kHz) and must survive at full level.
        // A single-cutoff kernel with its −6 dB point AT Nyquist was ~2 dB
        // down here — audibly shaving the top of real material.
        const int FromRate = 48_000;
        const int ToRate = 44_100;
        const double Frequency = 20_000.0;
        int length = FromRate / 2;
        var input = new float[length];
        for (int i = 0; i < length; i++)
        {
            input[i] = (float)Math.Sin(2.0 * Math.PI * Frequency * i / FromRate);
        }

        float[] output = SampleRateConverter.Resample(input, FromRate, ToRate);

        int margin = ToRate / 20;
        double sumSquares = 0;
        int count = 0;
        for (int i = margin; i < output.Length - margin; i++)
        {
            sumSquares += (double)output[i] * output[i];
            count++;
        }

        // A unit sine's RMS is 1/√2; allow ±0.26 dB for passband ripple and
        // the finite measurement window.
        double rms = Math.Sqrt(sumSquares / count);
        Assert.InRange(rms, 0.97 / Math.Sqrt(2.0), 1.03 / Math.Sqrt(2.0));
    }

    [Theory]
    [InlineData(22_500.0)]
    [InlineData(23_000.0)]
    public void Resample_SuppressesContentJustAboveTheTargetNyquist(double frequency)
    {
        // 48 → 44.1: content just past the 22.05 kHz target Nyquist is exactly
        // what folds back into the audible band, so the full stopband
        // attenuation must hold THERE — not only deep inside the stopband.
        // The previous design left 22.5 kHz only ~9 dB down (aliased to
        // 21.6 kHz).
        const int FromRate = 48_000;
        const int ToRate = 44_100;
        int length = FromRate / 2;
        var input = new float[length];
        for (int i = 0; i < length; i++)
        {
            input[i] = (float)Math.Sin(2.0 * Math.PI * frequency * i / FromRate);
        }

        float[] output = SampleRateConverter.Resample(input, FromRate, ToRate);

        int margin = ToRate / 20;
        double peak = 0;
        for (int i = margin; i < output.Length - margin; i++)
        {
            peak = Math.Max(peak, Math.Abs(output[i]));
        }

        Assert.True(peak < 1e-3, $"Alias residue at {frequency} Hz: {peak:E2}");
    }

    [Fact]
    public void Resample_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            SampleRateConverter.Resample(
                new float[48_000], 48_000, 44_100,
                progress: null, cancellation.Token));
    }

    [Fact]
    public void Resample_ReportsForwardOnlyProgressEndingAtOne()
    {
        var reports = new List<double>();
        var progress = new SynchronousProgress<double>(reports.Add);

        SampleRateConverter.Resample(
            new float[400_000], 44_100, 48_000, progress);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
        for (int i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i] >= reports[i - 1], "Progress went backwards");
        }
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
