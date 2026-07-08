using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// Locks the translation from the presentation-layer frequency-response
/// visibility flags to the DSP <see cref="SpectrumCurves"/> set. Combined with
/// the DSP-side <c>SpectrumCurveSelectionTests</c> (which pin SpectrumCurves to
/// the produced curves), this makes the full "flag → computed curve" chain
/// CI-verifiable after the flags moved off the DSP options type.
/// </summary>
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
        ShowMeasuredPhase = false,
        ShowMinimumPhase = false,
        ShowExcessPhase = false,
        ShowGroupDelay = false,
        ShowCoherence = false
    };

    private static void Set(CurveVisibilityOptions visibility, string flag, bool value) =>
        typeof(CurveVisibilityOptions).GetProperty(flag)!.SetValue(visibility, value);
}
