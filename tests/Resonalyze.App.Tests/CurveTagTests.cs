using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class CurveTagTests
{
    [Fact]
    public void Key_IsStableAndIndependentOfCompareFileName()
    {
        var main = new CurveTag(Mode.PhaseResponse, AnalysisCurveKind.Primary, CurveSource.Main);
        var compare = new CurveTag(
            Mode.PhaseResponse,
            AnalysisCurveKind.Primary,
            CurveSource.Compare);

        Assert.Equal("PhaseResponse:Primary:Main", main.Key);
        Assert.Equal("PhaseResponse:Primary:Compare", compare.Key);

        // The phase-wrap flag is carried for difference math but must not affect the
        // binding key (so a wrap toggle keeps a linked slot pointing at the same curve).
        var wrapped = compare with { PhaseUnwrapped = false };
        Assert.Equal(compare.Key, wrapped.Key);
    }

    [Theory]
    [InlineData(Mode.FrequencyResponse, AnalysisCurveKind.Primary, "Magnitude")]
    [InlineData(Mode.FrequencyResponse, AnalysisCurveKind.SecondHarmonic, "2nd harmonic")]
    [InlineData(Mode.FrequencyResponse, AnalysisCurveKind.ThdPlusNoise, "THD+N")]
    [InlineData(Mode.PhaseResponse, AnalysisCurveKind.Primary, "Measured phase")]
    [InlineData(Mode.PhaseResponse, AnalysisCurveKind.MinimumPhase, "Minimum phase")]
    [InlineData(Mode.GroupDelay, AnalysisCurveKind.Primary, "Group delay")]
    [InlineData(Mode.ImpulseResponse, AnalysisCurveKind.Primary, "Impulse")]
    public void Label_DescribesMainCurve(Mode mode, AnalysisCurveKind kind, string expected)
    {
        Assert.Equal(expected, new CurveTag(mode, kind, CurveSource.Main).Label);
    }

    [Fact]
    public void Label_MarksCompareCurves()
    {
        var tag = new CurveTag(Mode.GroupDelay, AnalysisCurveKind.Primary, CurveSource.Compare);
        Assert.Equal("Group delay — Compare", tag.Label);
    }
}
