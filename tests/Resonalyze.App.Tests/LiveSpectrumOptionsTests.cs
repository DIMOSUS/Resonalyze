namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumOptionsTests
{
    [Fact]
    public void SilentEnteringTransferMode_NormalizesToPeriodicPink()
    {
        // A transfer function has nothing to correlate without excitation, so Transfer mode swaps Silent out.
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = options.NormalizeSignalType();

        Assert.True(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsSilentInRtaMode()
    {
        var options = new LiveSpectrumOptions
        {
            AnalysisMode = LiveAnalysisMode.Rta,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = options.NormalizeSignalType();

        Assert.False(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Theory]
    [InlineData(LiveAnalysisMode.TransferFunction, NoiseColor.Pink)]
    [InlineData(LiveAnalysisMode.TransferFunction, NoiseColor.PinkPeriodic)]
    [InlineData(LiveAnalysisMode.Rta, NoiseColor.PinkPeriodic)]
    [InlineData(LiveAnalysisMode.Rta, NoiseColor.White)]
    public void NormalizeSignalType_LeavesRealExcitationsUntouched(
        LiveAnalysisMode mode,
        NoiseColor color)
    {
        var options = new LiveSpectrumOptions { AnalysisMode = mode, NoiseColor = color };

        bool changed = options.NormalizeSignalType();

        Assert.False(changed);
        Assert.Equal(color, options.NoiseColor);
    }
}
