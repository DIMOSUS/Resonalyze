using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Where the wizard's phase view gets its window from. The answer differs by source
/// and it matters: a channel handed over from Virtual DSP must be read exactly as that
/// panel read it, or a junction lined up here would not be lined up there.
/// </summary>
public sealed class EqWizardPhaseModeTests
{
    private const int SampleRate = 48_000;
    private const int ArrivalSample = 600; // 12.5 ms

    [Fact]
    public void AHandoffsGateIsAdoptedAsItStands()
    {
        // Not re-derived from this one channel: the panel resolved these windows and
        // this τ over every driver it was drawing, and one channel cannot reproduce a
        // placement that was made over a set.
        using var panel = new EqWizardPanel();
        var context = new EqWizardPhaseContext(
            Gate(9.0),
            GateOffsetMs: 9.5,
            DetrendMs: 10.25,
            PinnedOffset: false,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange, Wavelet(), 9.75)]);

        ApplySource(panel, Source(context));

        EqWizardPhaseContext seeded = ContextOf(panel)!;
        Assert.Same(context, seeded);
        Assert.Equal(9.5, seeded.GateOffsetMs);
        Assert.Equal(10.25, seeded.DetrendMs);
        Assert.Equal("B", seeded.Neighbours.Single().Name);
    }

    [Theory]
    [InlineData(PhaseDetrendMode.Off, false)]
    [InlineData(PhaseDetrendMode.Manual, true)]
    [InlineData(PhaseDetrendMode.Auto, false)]
    public void TheGatesDetrendModeAndPinSurviveTheHandoff(
        PhaseDetrendMode detrendMode,
        bool pinned)
    {
        // Every setting the gate dialog offers has to arrive as the user left it. The
        // detrend MODE is the one that reads as cosmetic and is not: it decides what
        // the phase is referenced to, and a wizard that always said "Manual" would
        // offer the user a choice they never made — and estimate nothing when they
        // had asked for an estimate.
        using var panel = new EqWizardPanel();
        var context = new EqWizardPhaseContext(
            Gate(9.0) with { DetrendMode = detrendMode },
            GateOffsetMs: 9.5,
            DetrendMs: 10.25,
            pinned,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour("B", OxyColors.Orange, Wavelet(), 9.75)]);

        ApplySource(panel, Source(context));

        Assert.Equal(detrendMode, ContextOf(panel)!.Gate.DetrendMode);
        Assert.Equal(pinned, ContextOf(panel)!.PinnedOffset);
        Assert.Equal(pinned, PinnedFlag(panel));
    }

    [Fact]
    public void ChangingTheDetrendModeResolvesANewReference()
    {
        // Off is no reference at all; Manual is the user's own figure; Auto is
        // estimated from the earliest-arriving response of the set. Resolved once, when
        // the gate changes — a τ that moved with the bank would slide every curve under
        // its own correction.
        using var panel = new EqWizardPanel();
        var context = new EqWizardPhaseContext(
            Gate(9.0), GateOffsetMs: 9.5, DetrendMs: 10.25, PinnedOffset: false,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour("B", OxyColors.Orange, Wavelet(), 9.75)]);
        ApplySource(panel, Source(context));

        ApplyPhaseGate(panel, context, 4.0, autoOffset: true, PhaseDetrendMode.Off);
        Assert.Equal(0.0, ContextOf(panel)!.DetrendMs);

        ApplyPhaseGate(panel, context, 4.0, autoOffset: true, PhaseDetrendMode.Manual);
        Assert.Equal(11.5, ContextOf(panel)!.DetrendMs);

        ApplyPhaseGate(panel, context, 4.0, autoOffset: true, PhaseDetrendMode.Auto);
        double estimated = ContextOf(panel)!.DetrendMs;
        // Estimated, not echoed back: it is neither the dialog's figure nor zero.
        Assert.NotEqual(11.5, estimated);
        Assert.NotEqual(0.0, estimated);
    }

    [Fact]
    public void AMeasurementOpenedOnItsOwnGetsItsOwnFrontAndNoNeighbours()
    {
        // Nothing to be comparable with, so the window opens on this response's own
        // front and τ references the same instant — which flattens the propagation
        // delay out and leaves the driver's own phase, the only thing there is to see.
        using var panel = new EqWizardPanel();

        ApplySource(panel, Source(phaseContext: null));

        EqWizardPhaseContext seeded = ContextOf(panel)!;
        Assert.Empty(seeded.Neighbours);
        // On the response's own FRONT — at the arrival or a hair ahead of it, never
        // after: a window that opens late is reading what came after the driver.
        double arrivalMs = ArrivalSample * 1_000.0 / SampleRate;
        Assert.InRange(seeded.GateOffsetMs, arrivalMs - 0.5, arrivalMs);
        Assert.Equal(seeded.GateOffsetMs, seeded.DetrendMs);
        // A window, not a point: the gate has to hold the response it opens on.
        Assert.True(seeded.Gate.PlateauMs > 0);
    }

    [Fact]
    public void AnImportedCurveHasNoPhaseToDraw()
    {
        // A magnitude and nothing else: no window and no correction can invent a phase
        // for it, so the view has nothing to seed and the gate button stays dead.
        using var panel = new EqWizardPanel();

        ApplySource(panel, new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.TextCurve,
            DisplayName = "curve.txt",
            Description = "An imported magnitude curve",
            Points = [new SignalPoint(100, -3), new SignalPoint(1_000, -1)],
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        });

        Assert.Null(ContextOf(panel));
        Assert.False(GateButton(panel).Enabled);
    }

    [Fact]
    public void LoadingASecondSourceDropsTheFirstsWindow()
    {
        // A window left over from the previous source would open on an arrival this
        // one does not have — and on a handoff it would draw this channel against
        // neighbours that are no longer on screen.
        using var panel = new EqWizardPanel();
        ApplySource(panel, Source(new EqWizardPhaseContext(
            Gate(9.0), 9.5, 10.25, false, OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour("B", OxyColors.Orange, Wavelet(), 9.75)])));

        ApplySource(panel, Source(phaseContext: null));

        EqWizardPhaseContext seeded = ContextOf(panel)!;
        Assert.Empty(seeded.Neighbours);
        double arrivalMs = ArrivalSample * 1_000.0 / SampleRate;
        Assert.InRange(seeded.GateOffsetMs, arrivalMs - 0.5, arrivalMs);
    }

    [Fact]
    public void PinningTheGateGivesEveryCurveTheSameWindow()
    {
        // The pin is the user saying "read everything through THIS window". Leaving it
        // on Auto keeps the placements as they arrived — each driver's window on its
        // own arrival — which is what the offsets in a handoff's context ARE.
        using var panel = new EqWizardPanel();
        var context = new EqWizardPhaseContext(
            Gate(9.0), GateOffsetMs: 9.5, DetrendMs: 10.25, PinnedOffset: false,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour("B", OxyColors.Orange, Wavelet(), 9.75)]);
        ApplySource(panel, Source(context));

        ApplyPhaseGate(panel, context, offsetMs: 4.0, autoOffset: false);

        EqWizardPhaseContext pinned = ContextOf(panel)!;
        Assert.Equal(4.0, pinned.GateOffsetMs);
        Assert.Equal(4.0, pinned.Neighbours.Single().GateOffsetMs);
        // The window itself travelled from the dialog, and the τ with it.
        Assert.Equal(2.5, pinned.Gate.PlateauMs);
        Assert.Equal(11.5, pinned.DetrendMs);

        ApplyPhaseGate(panel, context, offsetMs: 4.0, autoOffset: true);

        EqWizardPhaseContext auto = ContextOf(panel)!;
        Assert.Equal(9.5, auto.GateOffsetMs);
        Assert.Equal(9.75, auto.Neighbours.Single().GateOffsetMs);
    }

    private static void ApplyPhaseGate(
        EqWizardPanel panel,
        EqWizardPhaseContext opened,
        double offsetMs,
        bool autoOffset,
        PhaseDetrendMode detrendMode = PhaseDetrendMode.Manual) =>
        typeof(EqWizardPanel)
            .GetMethod("ApplyPhaseGate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [
                opened, offsetMs, autoOffset, 0.5, 2.5, 1.5,
                PhaseWindowMode.FrequencyDependent, 6, detrendMode, 11.5
            ]);

    private static bool PinnedFlag(EqWizardPanel panel) =>
        (bool)typeof(EqWizardPanel)
            .GetField("phaseGatePinned", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;

    private static EqWizardCurveSource Source(EqWizardPhaseContext? phaseContext)
    {
        Complex[] response = Wavelet();
        return new EqWizardCurveSource
        {
            Kind = phaseContext == null
                ? EqWizardSourceKind.ImpulseResponse
                : EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "source",
            Description = "A measurement",
            Measurement = new ImpulseMeasurementView(response, ArrivalSample, SampleRate),
            PreviewImpulseResponse = phaseContext == null ? null : response,
            PreviewChain = phaseContext == null ? null : DspChannelChain.Identity,
            GateSettings = phaseContext == null ? null : Gate(9.0),
            PhaseContext = phaseContext,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    // A decaying wavelet arriving at a known sample, so the front estimate has a real
    // arrival to find rather than one lone spike.
    private static Complex[] Wavelet()
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[ArrivalSample + i] =
                Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        return response;
    }

    private static PhaseAnalysisSettings Gate(double offsetMs) => new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Manual,
        ManualDetrendMilliseconds: 10.0,
        offsetMs,
        LeftMs: 1.0,
        PlateauMs: 20.0,
        RightMs: 5.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    private static void ApplySource(EqWizardPanel panel, EqWizardCurveSource source) =>
        typeof(EqWizardPanel)
            .GetMethod("ApplySource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [source]);

    private static EqWizardPhaseContext? ContextOf(EqWizardPanel panel) =>
        (EqWizardPhaseContext?)typeof(EqWizardPanel)
            .GetField("phaseContext", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel);

    private static Button GateButton(EqWizardPanel panel) =>
        (Button)typeof(EqWizardPanel)
            .GetField("buttonPhaseGate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
