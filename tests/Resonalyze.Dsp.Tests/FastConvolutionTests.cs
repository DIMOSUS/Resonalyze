namespace Resonalyze.Dsp.Tests;

public sealed class FastConvolutionTests
{
    [Fact]
    public void ConvolveDoubles_MatchesTheDirectSum()
    {
        var random = new Random(11);
        var first = new double[600];
        var second = new double[145];
        for (int i = 0; i < first.Length; i++)
        {
            first[i] = random.NextDouble() * 2.0 - 1.0;
        }
        for (int i = 0; i < second.Length; i++)
        {
            second[i] = random.NextDouble() * 2.0 - 1.0;
        }

        double[] fast = FastConvolution.Convolve(first, second);

        Assert.Equal(first.Length + second.Length - 1, fast.Length);
        for (int n = 0; n < fast.Length; n++)
        {
            double direct = 0;
            for (int k = Math.Max(0, n - first.Length + 1);
                k <= Math.Min(n, second.Length - 1);
                k++)
            {
                direct += second[k] * first[n - k];
            }

            Assert.True(
                Math.Abs(fast[n] - direct) < 1e-9,
                $"Mismatch at {n}: fast {fast[n]}, direct {direct}");
        }
    }

    [Fact]
    public void Convolve_MatchesTheDirectSum()
    {
        var random = new Random(7);
        var signal = new float[5_000];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        var kernel = new double[257];
        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] = random.NextDouble() * 2.0 - 1.0;
        }

        float[] fast = FastConvolution.Convolve(signal, kernel);

        Assert.Equal(signal.Length + kernel.Length - 1, fast.Length);
        // The WHOLE output against the O(N·K) definition — cheap at this size,
        // and it covers every overlap-add block boundary wherever the block
        // length lands, instead of guessing positions.
        for (int n = 0; n < fast.Length; n++)
        {
            double direct = 0;
            for (int k = Math.Max(0, n - signal.Length + 1);
                k <= Math.Min(n, kernel.Length - 1);
                k++)
            {
                direct += kernel[k] * signal[n - k];
            }

            Assert.True(
                Math.Abs(fast[n] - direct) < 1e-4,
                $"Mismatch at {n}: fast {fast[n]}, direct {direct}");
        }
    }

    [Fact]
    public void Convolve_IdentityKernelReturnsTheSignal()
    {
        float[] signal = [1f, -2f, 3f, 0.5f];
        double[] kernel = [1.0];

        float[] output = FastConvolution.Convolve(signal, kernel);

        Assert.Equal(signal.Length, output.Length);
        for (int i = 0; i < signal.Length; i++)
        {
            Assert.Equal(signal[i], output[i], 5);
        }
    }

    [Fact]
    public void Convolve_DelayKernelShiftsTheSignal()
    {
        var signal = new float[100];
        signal[10] = 1.0f;
        var kernel = new double[40];
        kernel[25] = 1.0;

        float[] output = FastConvolution.Convolve(signal, kernel);

        int peakIndex = 0;
        for (int i = 1; i < output.Length; i++)
        {
            if (Math.Abs(output[i]) > Math.Abs(output[peakIndex]))
            {
                peakIndex = i;
            }
        }

        Assert.Equal(35, peakIndex);
        Assert.Equal(1.0f, output[peakIndex], 5);
    }

    [Fact]
    public void Convolve_ReportsMonotonicProgressEndingAtOne()
    {
        var signal = new float[100_000];
        signal[0] = 1.0f;
        var kernel = new double[2_048];
        kernel[0] = 1.0;
        var reports = new List<double>();
        var progress = new SynchronousProgress<double>(reports.Add);

        FastConvolution.Convolve(signal, kernel, progress);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1], 10);
        for (int i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i] >= reports[i - 1]);
        }
    }

    [Fact]
    public void Convolve_CancellationStopsTheWork()
    {
        var signal = new float[500_000];
        var kernel = new double[8_192];
        kernel[0] = 1.0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FastConvolution.Convolve(signal, kernel, null, cancellation.Token));
    }
}
