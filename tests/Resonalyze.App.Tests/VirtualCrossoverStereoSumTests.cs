using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverStereoSumTests
{
    private const int SampleRate = 48_000;
    private const int Arrival = 200;

    // Short enough for the 8,192-sample responses: a gate reaching past a response's end reads zeros.
    private static readonly MagnitudeGateSnapshot Gate = MagnitudeGateSnapshot.Initial with
    {
        Template = MagnitudeGateSnapshot.Initial.Template with { PlateauMs = 50, RightMs = 20 },
        SmoothingInverseOctaves = 0
    };

    private static ProcessedChannel Channel(
        VirtualCrossoverChannel channel, double amplitude, int sampleRate = SampleRate)
    {
        var impulse = new Complex[8_192];
        impulse[Arrival] = amplitude;
        return new ProcessedChannel(
            channel, impulse, Arrival, sampleRate, OxyColors.White, MeasuredBand: MeasuredBand.Everything);
    }

    private static VirtualCrossoverChannel Driver(string name, VirtualCrossoverZone zone, bool mono = false) =>
        new(name) { Pair = { Zone = zone, Mono = mono } };

    private static double At1kHz(AnalysisCurve curve) =>
        curve.Points.MinBy(point => Math.Abs(Math.Log(point.X / 1_000.0)))!.Y;

    // Front L and R at one arrival with a mono sub: the sub is one response in both sides' lists.
    private static (List<ProcessedChannel> Shown, VirtualCrossoverSideSum Opposite) Stage(
        double frontRightAmplitude, int oppositeRate = SampleRate)
    {
        ProcessedChannel sub = Channel(Driver("Sub", VirtualCrossoverZone.Sub, mono: true), 1.0);
        ProcessedChannel frontLeft = Channel(Driver("Front L", VirtualCrossoverZone.Front), 1.0);
        ProcessedChannel frontRight = Channel(
            Driver("Front R", VirtualCrossoverZone.Front), frontRightAmplitude, oppositeRate);
        var opposite = new VirtualCrossoverSideSum([], Arrival, oppositeRate, [frontRight, sub]);
        return ([frontLeft, sub], opposite);
    }

    private static AnalysisCurve? Build(
        StereoSumMode mode, (List<ProcessedChannel> Shown, VirtualCrossoverSideSum Opposite) stage) =>
        VirtualCrossoverStereoSum.Build(
            mode, stage.Shown, stage.Opposite, Gate, _ => null);

    private static double OneDriverDb()
    {
        ProcessedChannel alone = Channel(Driver("Front L", VirtualCrossoverZone.Front), 1.0);
        return At1kHz(Gate.MeasuredSum([alone], Arrival, Arrival * 1_000.0 / SampleRate, _ => null).Display);
    }

    [Theory]
    // In phase: 1 + 1 + sub 1 = 3; by power 1 + 1 + 1 = 3.
    [InlineData(1.0, 9.542, 4.771)]
    // Front R inverted: the fronts cancel as vectors, leaving the sub; by power nothing cancels.
    [InlineData(-1.0, 0.0, 4.771)]
    public void TheSidesAddWithTheMonoSubCountedOnce(double frontRight, double vectorDb, double energyDb)
    {
        double reference = OneDriverDb();

        Assert.Equal(vectorDb, At1kHz(Build(StereoSumMode.Vector, Stage(frontRight))!) - reference, 2);
        Assert.Equal(energyDb, At1kHz(Build(StereoSumMode.Energy, Stage(frontRight))!) - reference, 2);
    }

    [Theory]
    // Opposite polarity at one level: one phasor sum cancels, two groups add by power (+3.01 dB).
    [InlineData(false, double.NegativeInfinity)]
    [InlineData(true, -20 + 3.0103)]
    public void TheHybridSum_AddsItsGroupsByPower(bool grouped, double expectedDb)
    {
        List<SignalPoint> capture = [.. Enumerable.Range(0, 128)
            .Select(i => new SignalPoint(20 * Math.Pow(10, 3.0 * i / 127), -20.0))];
        ProcessedChannel up = Channel(Driver("Front L", VirtualCrossoverZone.Front), 1.0);
        ProcessedChannel down = Channel(Driver("Front R", VirtualCrossoverZone.Front), -1.0);

        List<SignalPoint> sum = VirtualCrossoverHybrid.Sum(
            [capture, capture], offsetDb: 0, [up, down], Arrival, Gate, Arrival * 1_000.0 / SampleRate,
            [capture, capture], grouped ? [0, 1] : null)!;

        double level = sum.MinBy(point => Math.Abs(Math.Log(point.X / 1_000.0)))!.Y;
        if (double.IsNegativeInfinity(expectedDb))
        {
            Assert.True(level < -100, $"the opposite phasors left {level:0.0} dB");
        }
        else
        {
            Assert.Equal(expectedDb, level, 2);
        }
    }

    [Fact]
    public void NothingIsDrawnWhenOff_OrWhenTheSidesWereMeasuredAtDifferentRates()
    {
        Assert.Null(Build(StereoSumMode.Off, Stage(1.0)));
        Assert.Null(Build(StereoSumMode.Vector, Stage(1.0, oppositeRate: 96_000)));
    }
}
