namespace Resonalyze.App.Tests;

public sealed class SignalFadeTests
{
    [Fact]
    public void ApplyFadeInOut_SilencesEdgesAndKeepsTheMiddle()
    {
        var samples = new float[100];
        Array.Fill(samples, 1.0f);

        SignalFade.ApplyFadeInOut(samples, fadeSamples: 10);

        Assert.Equal(0.0f, samples[0]);
        Assert.Equal(0.0f, samples[^1]);
        Assert.True(samples[5] > 0.0f && samples[5] < 1.0f);
        Assert.Equal(1.0f, samples[50]);
        // Symmetric ramp: the fade-out mirrors the fade-in.
        Assert.Equal(samples[3], samples[^4], precision: 6);
    }

    [Fact]
    public void ApplyFadeInOut_ClampsFadeToHalfTheBuffer()
    {
        var samples = new float[4];
        Array.Fill(samples, 1.0f);

        SignalFade.ApplyFadeInOut(samples, fadeSamples: 1000);

        Assert.Equal(0.0f, samples[0]);
        Assert.Equal(0.0f, samples[^1]);
    }

    [Fact]
    public void ApplyFadeInOut_IgnoresDegenerateInput()
    {
        var single = new float[] { 1.0f };
        SignalFade.ApplyFadeInOut(single, fadeSamples: 8);
        Assert.Equal(1.0f, single[0]);

        var samples = new float[] { 1.0f, 1.0f };
        SignalFade.ApplyFadeInOut(samples, fadeSamples: 0);
        Assert.Equal(new[] { 1.0f, 1.0f }, samples);
    }
}
