using System.Numerics;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// Locks the mapping from <see cref="SpectrumCurves"/> to the set of analysis
/// curves <see cref="DataHelper.GetSpectrum"/> produces. This gating used to be
/// driven by presentation-layer Show* flags read off the options object and was
/// exercised by no test; now the caller passes an explicit set, so the contract
/// is "the returned curve kinds equal exactly the requested set". The synthetic
/// measurement's IR content is irrelevant here — only which curves appear.
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
    public void Harmonics_ProducesTheFourHarmonicCurvesWithoutPrimary()
    {
        Assert.Equal(
            new[]
            {
                AnalysisCurveKind.SecondHarmonic,
                AnalysisCurveKind.ThirdHarmonic,
                AnalysisCurveKind.FourthHarmonic,
                AnalysisCurveKind.ThdPlusNoise
            },
            Kinds(SpectrumCurves.Harmonics));
    }

    [Fact]
    public void All_ProducesPrimaryAndEveryHarmonicCurve()
    {
        Assert.Equal(
            new[]
            {
                AnalysisCurveKind.Primary,
                AnalysisCurveKind.SecondHarmonic,
                AnalysisCurveKind.ThirdHarmonic,
                AnalysisCurveKind.FourthHarmonic,
                AnalysisCurveKind.ThdPlusNoise
            },
            Kinds(SpectrumCurves.All));
    }

    [Fact]
    public void SingleHarmonic_ProducesOnlyThatCurve()
    {
        Assert.Equal(
            new[] { AnalysisCurveKind.ThirdHarmonic },
            Kinds(SpectrumCurves.ThirdHarmonic));
    }

    [Fact]
    public void PrimaryPlusThd_ProducesExactlyThoseTwo()
    {
        Assert.Equal(
            new[] { AnalysisCurveKind.Primary, AnalysisCurveKind.ThdPlusNoise },
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

    // A peak well inside the buffer plus a linear harmonic-offset map (harmonic h
    // sits h*300 samples before the peak), so every HDn/THD isolation window lands
    // in range with a positive length. The IR is otherwise arbitrary.
    private static SyntheticMeasurement CreateMeasurement()
    {
        var ir = new Complex[Length];
        ir[PeakIndex] = Complex.One;
        for (int i = 0; i < Length; i++)
        {
            ir[i] += new Complex(0.001 * Math.Sin(i * 0.1), 0.0);
        }

        return new SyntheticMeasurement(
            ir,
            SampleRate,
            maxMagnitudeIndex: PeakIndex,
            harmonicOffset: harmonic => harmonic * 300.0);
    }
}
