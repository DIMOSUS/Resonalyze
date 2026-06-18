namespace Resonalyze.Dsp.Tests;

public sealed class TransferFunctionTests
{
    [Fact]
    public void ComputeRelativeIr_IdenticalSignalsProducesUnitImpulse()
    {
        double[] reference = CreateImpulse(128);

        double[] ir = TransferFunction.ComputeRelativeIr(reference, reference);

        Assert.Equal(256, ir.Length);
        Assert.Equal(1.0, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
    }

    [Fact]
    public void ComputeRelativeIr_RecoversRelativeDelay()
    {
        const int delay = 17;
        double[] reference = CreateImpulse(128);
        double[] target = Delay(reference, delay);

        double[] ir = TransferFunction.ComputeRelativeIr(reference, target);

        int peakIndex = FindPeakIndex(ir);
        Assert.Equal(delay, peakIndex);
        Assert.Equal(1.0, ir[delay], precision: 9);
    }

    [Fact]
    public void ComputeRelativeIr_RecoversRelativeGain()
    {
        const double gain = 0.375;
        double[] reference = CreateImpulse(128);
        double[] target = reference.Select(sample => sample * gain).ToArray();

        double[] ir = TransferFunction.ComputeRelativeIr(reference, target);

        Assert.Equal(gain, ir[0], precision: 9);
        Assert.All(
            ir.Skip(1).Take(127),
            sample => Assert.Equal(0.0, sample, precision: 8));
    }

    [Fact]
    public void ComputeRelativeIr_RejectsMismatchedLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            TransferFunction.ComputeRelativeIr([1.0, 2.0], [1.0]));
    }

    private static double[] CreateImpulse(int length)
    {
        var impulse = new double[length];
        impulse[0] = 1.0;
        return impulse;
    }

    private static double[] Delay(double[] input, int delay)
    {
        var output = new double[input.Length];
        for (int i = 0; i + delay < input.Length; i++)
        {
            output[i + delay] = input[i];
        }

        return output;
    }

    private static int FindPeakIndex(IReadOnlyList<double> samples)
    {
        int peakIndex = 0;
        double peakMagnitude = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return peakIndex;
    }
}
