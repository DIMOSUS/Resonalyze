namespace Resonalyze.App.Tests;

public sealed class SignalGeneratorSamplesTests
{
    // 48 kHz × 10 s is 234.375 periods of 2048: the noise is shorter than the request, and reading past it threw.
    [Theory]
    [InlineData(44_100, 10)]
    [InlineData(48_000, 10)]
    [InlineData(48_000, 3)]
    [InlineData(96_000, 1)]
    public void PeriodicNoiseFillsTheRequestedLengthByRepeatingItsPeriod(int sampleRate, int seconds)
    {
        float[] samples = SignalGeneratorSamples.CreateMono(
            sampleRate, seconds, SignalGeneratorType.PinkPeriodicNoise, frequencyHz: 1000, level: 0.5);

        Assert.Equal(sampleRate * seconds, samples.Length);
        Assert.Contains(samples, sample => sample != 0f);
        for (int i = 2048; i < samples.Length; i++)
        {
            Assert.Equal(samples[i - 2048], samples[i]);
        }
    }

    [Theory]
    [InlineData((int)SignalGeneratorType.PinkNoise)]
    [InlineData((int)SignalGeneratorType.BrownNoise)]
    [InlineData((int)SignalGeneratorType.WhiteNoise)]
    [InlineData((int)SignalGeneratorType.Sine)]
    public void EverySignalIsTheRequestedLength(int type)
    {
        float[] samples = SignalGeneratorSamples.CreateMono(
            48_000, 2, (SignalGeneratorType)type, frequencyHz: 1000, level: 0.5);

        Assert.Equal(96_000, samples.Length);
    }
}
