using System;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.Dsp.Tests;

public sealed class EssSweepRateEstimatorTests
{
    private const int SampleRate = 96_000;
    private const int Length = 262_144;
    private const int PeakIndex = 96_000;
    private const double SecondsPerNeper = 0.1995;
    private const double NoiseSigma = 2e-5;

    [Fact]
    public void SecondAndThirdHarmonic_GiveTheRate()
    {
        double[] response = Deconvolution((2, 4e-3), (3, 2e-3));

        EssSweepRateEstimate? estimate = EssSweepRateEstimator.Estimate(response, PeakIndex, SampleRate, 1.0);

        Assert.NotNull(estimate);
        Assert.Equal([2, 3], estimate.Orders);
        Assert.Equal(SecondsPerNeper, estimate.SecondsPerNeper, 1.5 / SampleRate);
    }

    [Fact]
    public void ASecondHarmonicAlone_IsTakenAsTheSecond()
    {
        double[] response = Deconvolution((2, 4e-3));

        EssSweepRateEstimate? estimate = EssSweepRateEstimator.Estimate(response, PeakIndex, SampleRate, 1.0);

        Assert.Equal([2], estimate!.Orders);
        Assert.Equal(SecondsPerNeper, estimate.SecondsPerNeper, 1.5 / SampleRate);
    }

    [Fact]
    public void OddHarmonicsWithoutASecond_AreNotTakenForTheSecond()
    {
        // A symmetric nonlinearity: the strongest packet is the third, and calling it the second would be off by ln3/ln2.
        double[] response = Deconvolution((3, 5e-3), (5, 1e-3));

        EssSweepRateEstimate? estimate = EssSweepRateEstimator.Estimate(response, PeakIndex, SampleRate, 1.0);

        Assert.Equal([3, 5], estimate!.Orders);
        Assert.Equal(SecondsPerNeper, estimate.SecondsPerNeper, 1.5 / SampleRate);
    }

    [Fact]
    public void NoiseAlone_GivesNoRate()
    {
        Assert.Null(EssSweepRateEstimator.Estimate(Deconvolution(), PeakIndex, SampleRate, 1.0));
    }

    [Fact]
    public void RingingBeside_TheLinearPeak_IsNotAHarmonic()
    {
        double[] response = Deconvolution();
        response[PeakIndex - (int)(0.003 * SampleRate)] = 0.05;

        Assert.Null(EssSweepRateEstimator.Estimate(response, PeakIndex, SampleRate, 1.0));
    }

    [Fact]
    public void TheEstimate_PlacesPacketsWhereTheAnalysisLooksForThem()
    {
        double[] response = Deconvolution((2, 4e-3), (3, 2e-3));
        EssSweepRateEstimate estimate = EssSweepRateEstimator.Estimate(response, PeakIndex, SampleRate, 1.0)!;

        // Any band works with a duration of rate * ln(f2/f1); REW's own start frequency is not known.
        const double startHz = 0.366, endHz = 20_000;
        double duration = estimate.SecondsPerNeper * Math.Log(endHz / startHz);
        var sweep = new EssSweepMetadata(
            startHz, endHz, duration, SampleRate, (int)Math.Round(duration * SampleRate), PeakIndex);

        Assert.InRange(
            EssHarmonicAnalysis.HarmonicOffsetSamples(sweep, 2),
            Offset(2) - 2,
            Offset(2) + 2);
    }

    private static int Offset(int order) => (int)Math.Round(SecondsPerNeper * Math.Log(order) * SampleRate);

    private static double[] Deconvolution(params (int Order, double Amplitude)[] harmonics)
    {
        var random = new Random(20260915);
        var response = new double[Length];
        for (int i = 0; i < Length; i++)
        {
            double gaussian = -6.0;
            for (int k = 0; k < 12; k++)
            {
                gaussian += random.NextDouble();
            }

            response[i] = NoiseSigma * gaussian;
        }

        response[PeakIndex] = 1.0;
        foreach ((int order, double amplitude) in harmonics)
        {
            int index = PeakIndex - Offset(order);
            response[index] += amplitude;
            response[index - 1] -= amplitude / 2;
            response[index + 1] -= amplitude / 2;
        }

        return response;
    }
}
