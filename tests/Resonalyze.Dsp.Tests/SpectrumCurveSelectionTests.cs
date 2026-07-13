using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Locks the mapping from <see cref="SpectrumCurves"/> to the curves
/// <see cref="DataHelper.GetSpectrum"/> produces. GetSpectrum now owns only the
/// primary (linear) response; harmonic and THD curves moved to
/// <see cref="EssDistortion"/> (which needs the sweep metadata to normalize every
/// order against the same linear packet). So GetSpectrum honours the Primary flag
/// and ignores the harmonic flags.
/// </summary>
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
