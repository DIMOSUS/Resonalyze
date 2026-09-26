using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverGroupDelayViewTests
{
    private const int SampleRate = 48_000;
    private const int FirstArrival = 480;
    private const int SecondArrival = 960;
    private const double SecondAmplitude = 0.5;

    [Fact]
    [Trait("Category", "Slow")]
    public void TwoDelays_ReadTheirArrivals_AndTheSumSitsBetweenThemByEnergy()
    {
        VirtualCrossoverSession session = new();
        VirtualCrossoverProjectFile project = session.Project;
        project.PhaseWindowMode = PhaseWindowMode.FrequencyDependent;
        project.PhaseFdwCycles = 8;
        List<ProcessedChannel> processed = Processed(session);

        List<AcousticCurve> curves = Build(session, processed, showSum: true);

        Assert.Equal(3, curves.Count);
        AcousticCurve first = curves[0];
        AcousticCurve second = curves[1];
        AcousticCurve sum = curves[2];
        Assert.Equal("Sum", sum.Title);
        Assert.Equal(2.4, sum.Thickness);

        double firstMs = FirstArrival * 1_000.0 / SampleRate;
        double secondMs = SecondArrival * 1_000.0 / SampleRate;
        AssertFlat(first, 200, 10_000, firstMs, 0.05);
        AssertFlat(second, 200, 10_000, secondMs, 0.05);

        // Smoothing spans the ripple (10 ms = 100 Hz period vs FDW-8's f/16 floor), so the Sum reads the energy-weighted mean.
        double e2 = SecondAmplitude * SecondAmplitude;
        double expected = (firstMs + e2 * secondMs) / (1.0 + e2);
        AssertFlat(sum, 4_000, 10_000, expected, 0.1);
    }

    [Fact]
    public void PsychoacousticSmoothing_ReadsAsTheGroupDelayModesDefault()
    {
        // Psycho width on a time curve means GD mode's 1/12 oct, not Normalize's 1/6; set via the project setter since the stored width is 1/6.
        VirtualCrossoverSession session = new();
        VirtualCrossoverProjectFile project = session.Project;
        project.PhaseWindowMode = PhaseWindowMode.Fixed;
        var channel = new VirtualCrossoverChannel("A");
        channel.Pair.ShowProcessedCurve = true;
        Complex[] reflected = Delta(FirstArrival, 1.0);
        reflected[FirstArrival + 144] = new Complex(0.5, 0.0);
        List<ProcessedChannel> processed =
        [
            new ProcessedChannel(channel, reflected, FirstArrival, SampleRate, OxyColors.Red)
        ];

        project.SetSmoothingCode(SpectrumSmoothing.PsychoacousticCode);
        Assert.Equal(SpectrumSmoothing.PsychoacousticBaseInverseOctaves, project.SmoothingInverseOctaves);
        List<AcousticCurve> psychoacoustic = Build(session, processed, showSum: false);
        project.SetSmoothingCode(12);
        List<AcousticCurve> twelfth = Build(session, processed, showSum: false);
        project.SetSmoothingCode(SpectrumSmoothing.PsychoacousticBaseInverseOctaves);
        List<AcousticCurve> sixth = Build(session, processed, showSum: false);

        Assert.Equal(twelfth[0].Points, psychoacoustic[0].Points);
        Assert.NotEqual(sixth[0].Points, psychoacoustic[0].Points);
    }

    [Fact]
    public void HidingAChannel_LeavesTheOthersWindowWhereItWas()
    {
        VirtualCrossoverSession session = new();
        VirtualCrossoverProjectFile project = session.Project;
        project.PhaseWindowMode = PhaseWindowMode.FrequencyDependent;
        project.PhaseFdwCycles = 8;
        List<ProcessedChannel> processed = Processed(session);

        List<AcousticCurve> both = Build(session, processed, showSum: false);
        Assert.Equal(2, both.Count);

        processed[1].Channel.Pair.ShowProcessedCurve = false;
        List<AcousticCurve> alone = Build(session, processed, showSum: false);
        Assert.Single(alone);

        Assert.Equal(both[0].Points.Count, alone[0].Points.Count);
        for (int i = 0; i < both[0].Points.Count; i++)
        {
            Assert.Equal(both[0].Points[i].X, alone[0].Points[i].X);
            Assert.Equal(both[0].Points[i].Y, alone[0].Points[i].Y);
        }
    }

    private static void AssertFlat(
        AcousticCurve curve, double lowHz, double highHz, double expectedMs, double toleranceMs)
    {
        List<SignalPoint> band = curve.Points
            .Where(point => point.X >= lowHz && point.X <= highHz)
            .ToList();
        Assert.NotEmpty(band);
        Assert.All(band, point => Assert.InRange(
            point.Y, expectedMs - toleranceMs, expectedMs + toleranceMs));
    }

    private static List<AcousticCurve> Build(
        VirtualCrossoverSession session, List<ProcessedChannel> processed, bool showSum) =>
        new AcousticViewBuilder(session, new VirtualCrossoverHybrid(session))
            .GroupDelayCurves(processed, processed, showSum);

    private static List<ProcessedChannel> Processed(VirtualCrossoverSession session)
    {
        session.Channels.AddRange([new VirtualCrossoverChannel("A"), new VirtualCrossoverChannel("B")]);
        List<VirtualCrossoverChannel> channels = session.Channels;
        channels[0].Pair.ShowProcessedCurve = true;
        channels[1].Pair.ShowProcessedCurve = true;
        return
        [
            new ProcessedChannel(
                channels[0], Delta(FirstArrival, 1.0), FirstArrival, SampleRate, OxyColors.Red),
            new ProcessedChannel(
                channels[1], Delta(SecondArrival, SecondAmplitude), SecondArrival, SampleRate,
                OxyColors.Blue)
        ];
    }

    private static Complex[] Delta(int sample, double amplitude)
    {
        var impulse = new Complex[8_192];
        impulse[sample] = new Complex(amplitude, 0.0);
        return impulse;
    }
}
