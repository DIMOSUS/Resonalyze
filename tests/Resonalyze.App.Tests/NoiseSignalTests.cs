using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class NoiseSignalTests
{
    [Fact]
    public void FillData_Silent_CreatesZeroPlaybackBuffer()
    {
        using var signal = new NoiseSignal();

        signal.FillData(
            requestedDuration: 2048.0 / 48_000,
            sampleRate: 48_000,
            noiseColor: NoiseColor.Silent,
            periodLength: 2048);

        Assert.Equal(2048, signal.FloatData.Length);
        Assert.All(signal.FloatData, sample => Assert.Equal(0.0f, sample));
    }
}
