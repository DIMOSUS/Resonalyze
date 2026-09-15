using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardPhaseRenderTests
{
    private const int SampleRate = 48_000;
    private const int ArrivalSample = 480;
    private const double CrossoverHz = 300;

    [Fact]
    public void AnAllPassBandPullsTheEditedChannelOntoItsNeighbourThroughTheJunction()
    {
        // The neighbour carries an all-pass rotation (153° apart at 300 Hz); the matching all-pass on the edited channel lands on it (0.0°).
        var neighbourExcess = new EqualizationCurve(
            [new PeqBand(CrossoverHz, 0.7, 0, PeqBandType.AllPassSecondOrder)]);
        EqWizardPhaseRequest request = Request(
            neighbourChain: HighPass() with { Peq = neighbourExcess },
            bank: null);

        double before = MeanJunctionDifferenceDegrees(request);

        double after = MeanJunctionDifferenceDegrees(request with
        {
            Bank = new EqualizationCurve(
                [new PeqBand(CrossoverHz, 0.7, 0, PeqBandType.AllPassSecondOrder)])
        });

        Assert.True(
            before > 120,
            $"The junction should start misaligned; it reads {before:0.0}°.");
        Assert.True(
            after < 1,
            $"The all-pass should line the junction up; it still reads {after:0.0}°.");
    }

    [Fact]
    public void TheNeighboursDoNotMoveWhenTheBankIsEdited()
    {
        // Neighbours are frozen: a bank edit that moved them would fake a match.
        EqWizardPhaseRequest request = Request(HighPass(), bank: null);

        List<GatedPhaseCurve> bare = EqWizardPhaseRender.RenderNeighbours(request, 1.5);
        List<GatedPhaseCurve> corrected = EqWizardPhaseRender.RenderNeighbours(
            request with
            {
                Bank = new EqualizationCurve(
                    [new PeqBand(CrossoverHz, 0.7, 0, PeqBandType.AllPassSecondOrder)])
            },
            1.5);

        Assert.Equal(
            bare.Single().Points.Select(point => point.Y),
            corrected.Single().Points.Select(point => point.Y));
    }

    [Fact]
    public void TheCurveIsWrappedDegreesWithTheWrapsBrokenOut()
    {
        EqWizardPhaseRequest request = Request(HighPass(), bank: null);

        GatedPhaseCurve curve = EqWizardPhaseRender.RenderEditedChannel(
            request, "A", OxyColors.White, 1.8);

        Assert.NotEmpty(curve.Points);
        foreach (SignalPoint point in curve.Points)
        {
            Assert.InRange(point.X, 20, 20_000);
            if (!double.IsNaN(point.Y))
            {
                Assert.InRange(point.Y, -180.0, 180.0);
            }
        }

        Assert.NotEmpty(curve.WrapSegments);
        Assert.Contains(curve.Points, point => double.IsNaN(point.Y));
    }

    private static double MeanJunctionDifferenceDegrees(EqWizardPhaseRequest request)
    {
        GatedPhaseCurve edited = EqWizardPhaseRender.RenderEditedChannel(
            request, "edited", OxyColors.White, 1.8);
        GatedPhaseCurve neighbour =
            EqWizardPhaseRender.RenderNeighbours(request, 1.5).Single();

        Dictionary<double, double> neighbourByHz = neighbour.Points
            .Where(point => !double.IsNaN(point.Y))
            .GroupBy(point => point.X)
            .ToDictionary(group => group.Key, group => group.First().Y);

        List<double> differences = edited.Points
            .Where(point => !double.IsNaN(point.Y))
            .Where(point => point.X >= CrossoverHz / 1.41 && point.X <= CrossoverHz * 1.41)
            .Where(point => neighbourByHz.ContainsKey(point.X))
            .Select(point => Math.Abs(WrapDegrees(point.Y - neighbourByHz[point.X])))
            .ToList();

        Assert.NotEmpty(differences);
        return differences.Average();
    }

    private static double WrapDegrees(double degrees)
    {
        double wrapped = (degrees + 180.0) % 360.0;
        if (wrapped < 0)
        {
            wrapped += 360.0;
        }

        return wrapped - 180.0;
    }

    private static EqWizardPhaseRequest Request(
        DspChannelChain neighbourChain,
        EqualizationCurve? bank)
    {
        var impulse = new Complex[16_384];
        impulse[ArrivalSample] = 1.0;

        Complex[] neighbourResponse = VirtualCrossoverAnalysis.ApplyChain(
            impulse, neighbourChain, SampleRate, SampleRate);

        return new EqWizardPhaseRequest(
            impulse,
            LowPass(),
            bank,
            // Shared window and τ: any difference is the drivers', not the analysis's.
            GateOffsetMs: 9.5,
            [
                new EqWizardPhaseNeighbour(
                "neighbour", OxyColors.Orange, new PlacementChannel(neighbourResponse, 0, default), 9.5)
            ],
            Gate(),
            DetrendMs: 10.0,
            SampleRate,
            SampleRate);
    }

    private static DspChannelChain LowPass() =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24)));

    private static DspChannelChain HighPass() =>
        new(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, CrossoverHz, 24)));

    private static PhaseAnalysisSettings Gate() => new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Manual,
        ManualDetrendMilliseconds: 10.0,
        GateOffsetMs: 9.5,
        LeftMs: 1.0,
        PlateauMs: 100.0,
        RightMs: 20.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);
}
