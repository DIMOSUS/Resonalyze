namespace Resonalyze.App.Tests;

public sealed class RecordedChannelValidatorTests
{
    [Fact]
    public void SelectedMultichannelInputsIgnoreUnselectedDuplicatePair()
    {
        float[][] channels = Enumerable.Range(0, 8)
            .Select(_ => new float[32])
            .ToArray();
        channels[5] = Enumerable.Range(0, 32).Select(index => index / 32.0f).ToArray();
        channels[7] = Enumerable.Range(0, 32).Select(index => -index / 32.0f).ToArray();

        RecordedChannelValidator.EnsureDifferentSignals(
            channels,
            firstChannel: 5,
            secondChannel: 7,
            "WASAPI measurement");
    }

    [Fact]
    public void SelectedMultichannelInputsRejectTheirOwnDuplicateSignals()
    {
        float[][] channels = Enumerable.Range(0, 8)
            .Select(index => Enumerable.Repeat(index / 8.0f, 32).ToArray())
            .ToArray();
        channels[7] = channels[5].ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            RecordedChannelValidator.EnsureDifferentSignals(
                channels,
                firstChannel: 5,
                secondChannel: 7,
                "WASAPI measurement"));
    }
}
