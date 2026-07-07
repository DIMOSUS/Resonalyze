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
