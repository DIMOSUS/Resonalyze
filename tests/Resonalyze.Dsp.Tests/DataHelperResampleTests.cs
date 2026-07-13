namespace Resonalyze.Dsp.Tests;

public sealed class DataHelperResampleTests
{
    [Fact]
    public void LogarithmicResample_PreservesConstantLevel()
    {
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 2400,
            decibels: 5.0);

        List<SignalPoint> output = DataHelper.LogarithmicResample(input, 20, 20_000, 256);

        Assert.All(output, point => Assert.Equal(5.0, point.Y, precision: 3));
    }

    [Fact]
    public void LogarithmicResample_HoldsLastValueBeyondInputRange()
    {
        // The input spectrum ends at 10 kHz (e.g. a low sample rate), while the
        // output grid runs to 20 kHz. The kernel window above 10 kHz contains no
        // input samples, so the weight sum degenerates; the point must hold the
        // nearest sample instead of collapsing to the -160 dB floor.
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 1000,
            decibels: 5.0);

        List<SignalPoint> output = DataHelper.LogarithmicResample(input, 20, 20_000, 256);

        Assert.Equal(5.0, output[^1].Y, precision: 3);
    }

    [Fact]
    public void LogarithmicResample_PlacesAnIsolatedFeatureAtTheCorrectOutputFrequency()
    {
        // A single distinct bin at 1 kHz against a flat 0 dB floor. With a very small
        // smoothing width the Lanczos window collapses to +/-2 input bins (10 Hz
        // apart), and the kernel is exactly zero at those integer offsets, so the
        // 1 kHz output point reads ONLY the 1 kHz bin. This pins LogPositionToFrequency,
        // the BinarySearchX centre, and the kernel weighting together: a frequency-axis
        // inversion or an off-by-one centre would read a 0 dB neighbour instead of -6.
        List<SignalPoint> input = BuildLinearGrid(startHz: 10, stepHz: 10, count: 2000, decibels: 0.0);
        input[99] = new SignalPoint(1_000.0, -6.0); // bin index 99 -> 1000 Hz

        // steps = 3 over [100, 10000] puts output[1] at the geometric mean = 1000 Hz.
        List<SignalPoint> output = DataHelper.LogarithmicResample(
            input, start: 100, stop: 10_000, steps: 3, smoothingOctaves: 0.01);

        Assert.Equal(1_000.0, output[1].X, precision: 6);
        Assert.Equal(-6.0, output[1].Y, precision: 6);
        // The flat-floor endpoints stay at 0 dB, confirming the feature did not leak.
        Assert.Equal(0.0, output[0].Y, precision: 6);
        Assert.Equal(0.0, output[2].Y, precision: 6);
    }

    [Fact]
    public void SmoothLinear_PreservesConstantLevel()
    {
        List<SignalPoint> input = BuildLinearGrid(
            startHz: 10,
            stepHz: 10,
            count: 2000,
            decibels: -3.0);

        List<SignalPoint> output = DataHelper.SmoothLinear(input);

        Assert.Equal(input.Count, output.Count);
        Assert.All(output, point => Assert.Equal(-3.0, point.Y, precision: 6));
    }

    [Fact]
    public void SmoothLinear_PreservesNaNSegmentBreakWithoutBlendingAcrossIt()
    {
        var input = new List<SignalPoint>();
        for (int i = 1; i <= 80; i++)
        {
            double value = i is >= 35 and <= 45
                ? double.NaN
                : i < 35 ? 10.0 : 100.0;
            input.Add(new SignalPoint(i * 10.0, value));
        }

        List<SignalPoint> output = DataHelper.SmoothLinear(input, 1.0 / 3.0);

        Assert.All(output.Skip(34).Take(11), point => Assert.True(double.IsNaN(point.Y)));
        Assert.Equal(10.0, output[33].Y, tolerance: 1e-9);
        Assert.Equal(100.0, output[45].Y, tolerance: 1e-9);
    }

    private static List<SignalPoint> BuildLinearGrid(
        double startHz,
        double stepHz,
        int count,
        double decibels)
    {
        List<SignalPoint> points = new(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new SignalPoint(startHz + i * stepHz, decibels));
        }

        return points;
    }
}
