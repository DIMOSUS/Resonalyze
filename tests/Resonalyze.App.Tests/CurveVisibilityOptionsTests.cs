using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Flag-to-<see cref="SpectrumCurves"/> mapping; with the DSP-side SpectrumCurveSelectionTests it covers flag to computed curve.</summary>
public sealed class CurveVisibilityOptionsTests
{
    [Fact]
    public void ToSpectrumCurves_AllFlagsOn_IsAll()
    {
        Assert.Equal(SpectrumCurves.All, new CurveVisibilityOptions().ToSpectrumCurves());
    }

    [Fact]
    public void ToSpectrumCurves_AllFlagsOff_IsNone()
    {
        Assert.Equal(SpectrumCurves.None, AllOff().ToSpectrumCurves());
    }

    [Theory]
    [InlineData(nameof(CurveVisibilityOptions.ShowPrimary), SpectrumCurves.Primary)]
    [InlineData(nameof(CurveVisibilityOptions.ShowHd2), SpectrumCurves.SecondHarmonic)]
    [InlineData(nameof(CurveVisibilityOptions.ShowHd3), SpectrumCurves.ThirdHarmonic)]
    [InlineData(nameof(CurveVisibilityOptions.ShowHd4), SpectrumCurves.FourthHarmonic)]
    [InlineData(nameof(CurveVisibilityOptions.ShowThdPlusNoise), SpectrumCurves.ThdPlusNoise)]
    [InlineData(nameof(CurveVisibilityOptions.ShowNoiseFloor), SpectrumCurves.NoiseFloor)]
    public void ToSpectrumCurves_EachSpectrumFlagMapsToItsBit(string flag, SpectrumCurves expected)
    {
        CurveVisibilityOptions visibility = AllOff();
        Set(visibility, flag, true);

        Assert.Equal(expected, visibility.ToSpectrumCurves());
    }

    [Fact]
    public void ToSpectrumCurves_IgnoresPhaseGroupDelayAndCoherenceFlags()
    {
        CurveVisibilityOptions visibility = AllOff();
        visibility.ShowMeasuredPhase = true;
        visibility.ShowMinimumPhase = true;
        visibility.ShowExcessPhase = true;
        visibility.ShowGroupDelay = true;
        visibility.ShowMinimumPhaseGroupDelay = true;
        visibility.ShowExcessGroupDelay = true;
        visibility.ShowCoherence = true;

        Assert.Equal(SpectrumCurves.None, visibility.ToSpectrumCurves());
    }

    private static CurveVisibilityOptions AllOff() => new()
    {
        ShowPrimary = false,
        ShowHd2 = false,
        ShowHd3 = false,
        ShowHd4 = false,
        ShowThdPlusNoise = false,
        ShowNoiseFloor = false,
        ShowMeasuredPhase = false,
        ShowMinimumPhase = false,
        ShowExcessPhase = false,
        ShowGroupDelay = false,
        ShowMinimumPhaseGroupDelay = false,
        ShowExcessGroupDelay = false,
        ShowCoherence = false
    };

    private static void Set(CurveVisibilityOptions visibility, string flag, bool value) =>
        typeof(CurveVisibilityOptions).GetProperty(flag)!.SetValue(visibility, value);
}
