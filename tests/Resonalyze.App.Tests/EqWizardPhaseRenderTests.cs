using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The EQ Wizard's phase view, on the job it exists for: an all-pass band is
/// invisible on a magnitude plot, so the only way to tell whether one lined a driver
/// up with its neighbour through the crossover region is to draw the measured phase
/// of both and look.
/// </summary>
public sealed class EqWizardPhaseRenderTests
{
    private const int SampleRate = 48_000;
    private const int ArrivalSample = 480; // 10 ms
    private const double CrossoverHz = 300;

    [Fact]
    public void AnAllPassBandPullsTheEditedChannelOntoItsNeighbourThroughTheJunction()
    {
        // The field case, synthesised: two drivers meeting at 300 Hz, the neighbour
        // carrying a phase rotation of its own — a driver's own excess phase, which
        // no delay and no polarity flip can undo, because it is frequency-dependent.
        // Through the junction the two read 153° apart. Adding the matching all-pass
        // to the channel being edited rotates it the same way, and they land on each
        // other — 0.0°, since here it is exactly the same biquad.
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
        // They are frozen measurements of drivers nobody is editing. If a bank edit
        // could move them, the view would answer "the junction lines up" by moving
        // the thing being compared against.
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
        // Degrees wrapped to ±180, like every other phase plot in the app and in REW.
        // The breaks matter: a wrap drawn at full stroke reads as a phase transition
        // that never happened.
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

        // A low-passed driver rotates far more than one turn between 20 Hz and
        // 20 kHz, so this curve cannot be wrap-free.
        Assert.NotEmpty(curve.WrapSegments);
        Assert.Contains(curve.Points, point => double.IsNaN(point.Y));
    }

    // The mean angular distance between the edited channel and its neighbour across
    // the junction's overlap (half an octave either side of the corner), measured the
    // way an eye reads it: on the wrapped curves, modulo a full turn.
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
            // One shared window opening just ahead of the common arrival, and one
            // shared τ: both channels are read exactly alike, so any difference the
            // test sees is the drivers', not the analysis's.
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

    // A window long enough to hold a 300 Hz LR24's own ringing, so the curves
    // describe the drivers rather than the window.
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
