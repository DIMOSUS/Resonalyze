using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverGroupDelayViewTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int SampleRate = 48_000;
    private const int FirstArrival = 480;
    private const int SecondArrival = 960;
    private const double SecondAmplitude = 0.5;

    [Fact]
    public void TwoDelays_ReadTheirArrivals_AndTheSumSitsBetweenThemByEnergy()
    {
        using VirtualCrossoverPanel panel = Loaded();
        VirtualCrossoverProjectFile project = Project(panel);
        project.PhaseWindowMode = PhaseWindowMode.FrequencyDependent;
        project.PhaseFdwCycles = 8;
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = true;
        List<ProcessedChannel> processed = Processed(panel);

        List<AcousticCurve> curves = Build(panel, processed);

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
        using VirtualCrossoverPanel panel = Loaded();
        VirtualCrossoverProjectFile project = Project(panel);
        project.PhaseWindowMode = PhaseWindowMode.Fixed;
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = false;
        List<VirtualCrossoverChannel> channels = Channels(panel);
        channels[0].Pair.ShowProcessedCurve = true;
        channels[1].Pair.ShowProcessedCurve = false;
        Complex[] reflected = Delta(FirstArrival, 1.0);
        reflected[FirstArrival + 144] = new Complex(0.5, 0.0);
        List<ProcessedChannel> processed =
        [
            new ProcessedChannel(channels[0], reflected, FirstArrival, SampleRate, OxyColors.Red)
        ];

        project.SetSmoothingCode(SpectrumSmoothing.PsychoacousticCode);
        Assert.Equal(SpectrumSmoothing.PsychoacousticBaseInverseOctaves, project.SmoothingInverseOctaves);
        List<AcousticCurve> psychoacoustic = Build(panel, processed);
        project.SetSmoothingCode(12);
        List<AcousticCurve> twelfth = Build(panel, processed);
        project.SetSmoothingCode(SpectrumSmoothing.PsychoacousticBaseInverseOctaves);
        List<AcousticCurve> sixth = Build(panel, processed);

        Assert.Equal(twelfth[0].Points, psychoacoustic[0].Points);
        Assert.NotEqual(sixth[0].Points, psychoacoustic[0].Points);
    }

    [Fact]
    public void HidingAChannel_LeavesTheOthersWindowWhereItWas()
    {
        using VirtualCrossoverPanel panel = Loaded();
        VirtualCrossoverProjectFile project = Project(panel);
        project.PhaseWindowMode = PhaseWindowMode.FrequencyDependent;
        project.PhaseFdwCycles = 8;
        ((CheckBox)Field(panel, "checkBoxShowSum")).Checked = false;
        List<ProcessedChannel> processed = Processed(panel);

        List<AcousticCurve> both = Build(panel, processed);
        Assert.Equal(2, both.Count);

        processed[1].Channel.Pair.ShowProcessedCurve = false;
        List<AcousticCurve> alone = Build(panel, processed);
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
        VirtualCrossoverPanel panel, List<ProcessedChannel> processed) =>
        (List<AcousticCurve>)panel.GetType()
            .GetMethod("BuildGroupDelayCurves", Hidden)!
            .Invoke(panel, [processed, null])!;

    private static List<ProcessedChannel> Processed(VirtualCrossoverPanel panel)
    {
        List<VirtualCrossoverChannel> channels = Channels(panel);
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

    private static VirtualCrossoverPanel Loaded()
    {
        var panel = new VirtualCrossoverPanel();
        List<VirtualCrossoverChannel> channels = Channels(panel);
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].Pair = Project(panel).Pairs[index];
        }

        return panel;
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Hidden)!.GetValue(target)!;

    private static List<VirtualCrossoverChannel> Channels(VirtualCrossoverPanel panel) =>
        (List<VirtualCrossoverChannel>)Field(panel, "channels");

    private static VirtualCrossoverProjectFile Project(VirtualCrossoverPanel panel) =>
        (VirtualCrossoverProjectFile)Field(panel, "project");
}
