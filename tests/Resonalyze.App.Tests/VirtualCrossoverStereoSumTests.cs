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
        VirtualCrossoverChannel channel, double amplitude, int sampleRate = SampleRate, int arrival = Arrival)
    {
        var impulse = new Complex[8_192];
        impulse[arrival] = amplitude;
        return new ProcessedChannel(
            channel, impulse, arrival, sampleRate, OxyColors.White, MeasuredBand: MeasuredBand.Everything);
    }

    private static double At(AnalysisCurve curve, double hz) =>
        curve.Points.MinBy(point => Math.Abs(Math.Log(point.X / hz)))!.Y;

    private static VirtualCrossoverChannel Driver(string name, VirtualCrossoverZone zone, bool mono = false) =>
        new(name) { Pair = { Zone = zone, Mono = mono } };

    private static double At1kHz(AnalysisCurve curve) => At(curve, 1_000.0);

    // Front L and R at one arrival with a mono sub measured from both inputs: each side plays it at half amplitude.
    private static (List<ProcessedChannel> Shown, VirtualCrossoverSideSum Opposite) Stage(
        double frontRightAmplitude, int oppositeRate = SampleRate)
    {
        ProcessedChannel sub = Channel(Driver("Sub", VirtualCrossoverZone.Sub, mono: true), 0.5);
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
    // In phase: (1 + ½) + (1 + ½) = 3; by power 1.5² + 1.5² = 4.5.
    [InlineData(1.0, 9.542, 6.532)]
    // Front R inverted: the fronts cancel as vectors, leaving the sub whole; by power 1.5² + 0.5² = 2.5.
    [InlineData(-1.0, 0.0, 3.979)]
    public void TheSidesAddAsTheirSums_TheMonoSubHalfInEach(double frontRight, double vectorDb, double energyDb)
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

    [Theory]
    // The shown side arrives 3 ms after the other (4.2 and 7.2 ms); the earlier of the two placements opens the window.
    [InlineData(null, null)]
    [InlineData(null, 1.0)]
    [InlineData(2.0, 8.0)]
    public void TheWindowOpensAtTheEarlierSide_SoBothArrivalsAreSummed(double? shownPinMs, double? oppositePinMs)
    {
        const int lag = 144;
        ProcessedChannel frontLeft = Channel(
            Driver("Front L", VirtualCrossoverZone.Front), 1.0, arrival: Arrival + lag);
        ProcessedChannel frontRight = Channel(Driver("Front R", VirtualCrossoverZone.Front), 1.0);
        var opposite = new VirtualCrossoverSideSum([], Arrival, SampleRate, [frontRight]);
        MagnitudeGateSnapshot gate = Gate with { PinnedOffsetMs = shownPinMs, OppositePinnedOffsetMs = oppositePinMs };
        double reference = OneDriverDb();
        double peakHz = (double)SampleRate / lag;

        AnalysisCurve vector = VirtualCrossoverStereoSum.Build(
            StereoSumMode.Vector, [frontLeft], opposite, gate, _ => null)!;
        AnalysisCurve energy = VirtualCrossoverStereoSum.Build(
            StereoSumMode.Energy, [frontLeft], opposite, gate, _ => null)!;

        // 3 ms apart: a comb, in phase at 1/τ and opposed at 1/(2τ); by power the sides add flat.
        Assert.Equal(6.02, At(vector, peakHz) - reference, 1);
        Assert.True(At(vector, peakHz / 2) - reference < -15, $"no null at {peakHz / 2:0} Hz");
        Assert.Equal(3.01, At(energy, peakHz) - reference, 1);
        Assert.Equal(3.01, At(energy, peakHz / 2) - reference, 1);
    }

    [Fact]
    public void MonoBlocksAloneAreTheirOwnLPlusR_ButOneSideAloneIsNot()
    {
        ProcessedChannel sub = Channel(Driver("Sub", VirtualCrossoverZone.Sub, mono: true), 0.5);
        ProcessedChannel frontLeft = Channel(Driver("Front L", VirtualCrossoverZone.Front), 1.0);
        var onlySub = new VirtualCrossoverSideSum([], Arrival, SampleRate, [sub]);

        AnalysisCurve? vector = VirtualCrossoverStereoSum.Build(StereoSumMode.Vector, [sub], onlySub, Gate, _ => null);
        AnalysisCurve? energy = VirtualCrossoverStereoSum.Build(StereoSumMode.Energy, [sub], onlySub, Gate, _ => null);
        Assert.Equal(0.0, At1kHz(vector!) - OneDriverDb(), 2);
        Assert.Equal(-3.01, At1kHz(energy!) - OneDriverDb(), 2);
        Assert.Null(VirtualCrossoverStereoSum.Build(StereoSumMode.Vector, [frontLeft, sub], onlySub, Gate, _ => null));
    }

    [Theory]
    // Half an octave either side of 300 Hz is where the hand-over starts and ends; in its middle the powers
    // (10 and 2.51 for 10 dB and 4 dB) average to 6.26, 7.96 dB.
    [InlineData(100.0, 10.0)]
    [InlineData(212.13, 10.0)]
    [InlineData(300.0, 7.96)]
    [InlineData(424.26, 4.0)]
    [InlineData(5_000.0, 4.0)]
    public void Blend_IsVectorBelowAndEnergyAbove_HandedOverAcrossOneOctave(double hz, double expectedDb)
    {
        List<SignalPoint> vector = [new SignalPoint(hz, 10.0)];
        List<SignalPoint> energy = [new SignalPoint(hz, 4.0)];

        Assert.Equal(expectedDb, VirtualCrossoverStereoSum.Blend(vector, energy, 300.0).Single().Y, 2);
    }

    [Fact]
    public void Blend_FillsANullBetweenTheSides_AsCoherenceFades()
    {
        // Two equal sides in opposite polarity at the hand-over: Vector nulls, Energy reads +3.01 dB over one side.
        ProcessedChannel left = Channel(Driver("Front L", VirtualCrossoverZone.Front), 1.0);
        ProcessedChannel right = Channel(Driver("Front R", VirtualCrossoverZone.Front), -1.0);
        var opposite = new VirtualCrossoverSideSum([], Arrival, SampleRate, [right]);

        AnalysisCurve blend = VirtualCrossoverStereoSum.Build(
            StereoSumMode.Blend, [left], opposite, Gate, _ => null, blendHz: 1_000.0)!;

        // Half of Energy's power (2 over one side's 1): one side's level, 0 dB.
        Assert.Equal(0.0, At1kHz(blend) - OneDriverDb(), 1);
    }

    [Fact]
    public void NothingIsDrawnWhenOff_OrWhenTheSidesWereMeasuredAtDifferentRates()
    {
        Assert.Null(Build(StereoSumMode.Off, Stage(1.0)));
        Assert.Null(Build(StereoSumMode.Vector, Stage(1.0, oppositeRate: 96_000)));
    }
}
