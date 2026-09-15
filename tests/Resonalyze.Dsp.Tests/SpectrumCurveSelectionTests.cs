using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary><see cref="DataHelper.GetSpectrum"/> owns only the primary; harmonic/THD curves live in <see cref="EssDistortion"/>.</summary>
public sealed class SpectrumCurveSelectionTests
{
    private const int SampleRate = 48_000;
    private const int Length = 8192;
    private const int PeakIndex = 2000;

    [Fact]
    public void None_ProducesNoCurves()
    {
        Assert.Empty(Kinds(SpectrumCurves.None));
    }

    [Fact]
    public void Primary_ProducesOnlyThePrimaryCurve()
    {
        Assert.Equal(new[] { AnalysisCurveKind.Primary }, Kinds(SpectrumCurves.Primary));
    }

    [Fact]
    public void HarmonicFlagsAreNotHandledByGetSpectrum()
    {
        Assert.Empty(Kinds(SpectrumCurves.Harmonics));
        Assert.Empty(Kinds(SpectrumCurves.ThirdHarmonic));
    }

    [Fact]
    public void All_ProducesOnlyThePrimaryCurveFromGetSpectrum()
    {
        Assert.Equal(new[] { AnalysisCurveKind.Primary }, Kinds(SpectrumCurves.All));
    }

    [Fact]
    public void PrimaryPlusThd_ProducesOnlyThePrimaryFromGetSpectrum()
    {
        Assert.Equal(
            new[] { AnalysisCurveKind.Primary },
            Kinds(SpectrumCurves.Primary | SpectrumCurves.ThdPlusNoise));
    }

    private static AnalysisCurveKind[] Kinds(SpectrumCurves curves) =>
        DataHelper.GetSpectrum(
                CreateMeasurement(),
                new FrequencyResponseOptions
                {
                    Window = 1024,
                    LeftTukeyWindow = 256,
                    RightTukeyWindow = 256,
                    SmoothingInverseOctaves = 6
                },
                calibration: null,
                curves)
            .Select(curve => curve.Kind)
            .ToArray();

    private static SyntheticMeasurement CreateMeasurement()
    {
        var ir = new Complex[Length];
        ir[PeakIndex] = Complex.One;
        for (int i = 0; i < Length; i++)
        {
            ir[i] += new Complex(0.001 * Math.Sin(i * 0.1), 0.0);
        }

        return new SyntheticMeasurement(ir, SampleRate, maxMagnitudeIndex: PeakIndex);
    }
}
