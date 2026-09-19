using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardPhaseModeTests
{
    private const int SampleRate = 48_000;
    private const int ArrivalSample = 600;

    [Fact]
    public void AHandoffsGateIsAdoptedAsItStands()
    {
        // Not re-derived from one channel: the panel placed these windows over the whole set.
        var session = new EqWizardSession();
        var context = new EqWizardPhaseContext(
            Gate(9.0),
            GateOffsetMs: 9.5,
            DetrendMs: 10.25,
            PinnedOffset: false,
            new PlacementChannel(Wavelet(), 0, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange, new PlacementChannel(Wavelet(), 0, default), 9.75)]);

        session.Load(Source(context));

        EqWizardPhaseContext seeded = session.PhaseContext!;
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
        // Detrend mode decides what phase is referenced to, so it must arrive as the user left it.
        var session = new EqWizardSession();
        var context = new EqWizardPhaseContext(
            Gate(9.0) with { DetrendMode = detrendMode },
            GateOffsetMs: 9.5,
            DetrendMs: 10.25,
            pinned,
            new PlacementChannel(Wavelet(), 0, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange, new PlacementChannel(Wavelet(), 0, default), 9.75)]);

        session.Load(Source(context));

        Assert.Equal(detrendMode, session.PhaseContext!.Gate.DetrendMode);
        Assert.Equal(pinned, session.PhaseContext!.PinnedOffset);
        Assert.Equal(pinned, session.PhaseGatePinned);
    }

    [Fact]
    public void ChangingTheDetrendModeResolvesANewReference()
    {
        // τ is resolved once, when the gate changes: a τ moving with the bank would slide every curve.
        var session = new EqWizardSession();
        var context = new EqWizardPhaseContext(
            Gate(9.0), GateOffsetMs: 9.5, DetrendMs: 10.25, PinnedOffset: false,
            new PlacementChannel(Wavelet(), 0, default), SampleRate, OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange, new PlacementChannel(Wavelet(), 0, default), 9.75)]);
        session.Load(Source(context));

        ApplyPhaseGate(session, context, 4.0, autoOffset: true, PhaseDetrendMode.Off);
        Assert.Equal(0.0, session.PhaseContext!.DetrendMs);

        ApplyPhaseGate(session, context, 4.0, autoOffset: true, PhaseDetrendMode.Manual);
        Assert.Equal(11.5, session.PhaseContext!.DetrendMs);

        ApplyPhaseGate(session, context, 4.0, autoOffset: true, PhaseDetrendMode.Auto);
        double estimated = session.PhaseContext!.DetrendMs;
        Assert.NotEqual(11.5, estimated);
        Assert.NotEqual(0.0, estimated);
    }

    [Fact]
    public void AMeasurementOpenedOnItsOwnGetsItsOwnFrontAndNoNeighbours()
    {
        var session = new EqWizardSession();

        session.Load(Source(phaseContext: null));

        EqWizardPhaseContext seeded = session.PhaseContext!;
        Assert.Empty(seeded.Neighbours);
        // At the arrival or a hair ahead, never after.
        double arrivalMs = ArrivalSample * 1_000.0 / SampleRate;
        Assert.InRange(seeded.GateOffsetMs, arrivalMs - 0.5, arrivalMs);
        Assert.Equal(seeded.GateOffsetMs, seeded.DetrendMs);
        Assert.True(seeded.Gate.PlateauMs > 0);
    }

    [Fact]
    public async Task ChangingTheProcessorRate_RendersTheMeasuredPhaseAgain()
    {
        // The rendered phase is keyed by the bank; the same bank realised at another rate is another curve.
        var session = new EqWizardSession();
        session.Load(Source(phaseContext: null));
        session.Bank.Add(PeqBandType.AllPassSecondOrder);
        EqualizationCurve bank = session.Bank.Curve;
        await session.Previews.RequestPhaseCurve(bank)!;
        GatedPhaseCurve at48 = session.Previews.PhaseCurves().Edited!;
        Assert.Null(session.Previews.RequestPhaseCurve(bank));

        session.SetManualSampleRate(96_000);

        Assert.Null(session.Previews.PhaseCurves().Edited);
        await session.Previews.RequestPhaseCurve(bank)!;
        GatedPhaseCurve at96 = session.Previews.PhaseCurves().Edited!;
        Assert.NotEqual(at48.Points, at96.Points);
    }

    [Fact]
    public void AnImportedCurveHasNoPhaseToDraw()
    {
        var session = new EqWizardSession();

        session.Load(new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.TextCurve,
            DisplayName = "curve.txt",
            Description = "An imported magnitude curve",
            Points = [new SignalPoint(100, -3), new SignalPoint(1_000, -1)],
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        });

        Assert.Null(session.PhaseContext);
    }

    [Fact]
    public void LoadingASecondSourceDropsTheFirstsWindow()
    {
        var session = new EqWizardSession();
        session.Load(Source(new EqWizardPhaseContext(
            Gate(9.0), 9.5, 10.25, false,
            new PlacementChannel(Wavelet(), 0, default), SampleRate, OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange, new PlacementChannel(Wavelet(), 0, default), 9.75)])));

        session.Load(Source(phaseContext: null));

        EqWizardPhaseContext seeded = session.PhaseContext!;
        Assert.Empty(seeded.Neighbours);
        double arrivalMs = ArrivalSample * 1_000.0 / SampleRate;
        Assert.InRange(seeded.GateOffsetMs, arrivalMs - 0.5, arrivalMs);
    }

    [Fact]
    public void PinningTheGateGivesEveryCurveTheSameWindow()
    {
        var session = new EqWizardSession();
        var context = new EqWizardPhaseContext(
            Gate(9.0), GateOffsetMs: 5.0, DetrendMs: 10.25, PinnedOffset: false,
            new PlacementChannel(Arriving(240), 240, default), SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange,
                new PlacementChannel(Arriving(480), 480, default), 10.0)]);
        session.Load(Source(context));

        ApplyPhaseGate(session, context, offsetMs: 4.0, autoOffset: false);

        EqWizardPhaseContext pinned = session.PhaseContext!;
        Assert.Equal(4.0, pinned.GateOffsetMs);
        Assert.Equal(4.0, pinned.Neighbours.Single().GateOffsetMs);
        Assert.Equal(2.5, pinned.Gate.PlateauMs);
        Assert.Equal(11.5, pinned.DetrendMs);

        ApplyPhaseGate(session, context, offsetMs: 4.0, autoOffset: true);

        EqWizardPhaseContext auto = session.PhaseContext!;
        Assert.Equal(5.0, auto.GateOffsetMs, 1);
        Assert.Equal(10.0, auto.Neighbours.Single().GateOffsetMs, 1);
    }

    [Fact]
    public void ChangingTheWindowLengthResolvesThePlacementsAgain()
    {
        // Per-curve vs shared placement depends on window lengths, so it is re-resolved, not carried from the handoff.
        var session = new EqWizardSession();
        var context = new EqWizardPhaseContext(
            Gate(9.0),
            GateOffsetMs: 5.0,
            DetrendMs: 5.0,
            PinnedOffset: false,
            new PlacementChannel(Arriving(240), 240, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange,
                new PlacementChannel(Arriving(480), 480, default), 5.0)]);
        session.Load(Source(context));

        ApplyPhaseGate(
            session, context, 5.0, autoOffset: true, PhaseDetrendMode.Manual,
            detrendMs: 5.0, leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        EqWizardPhaseContext resolved = session.PhaseContext!;
        Assert.Equal(5.0, resolved.GateOffsetMs, 1);
        Assert.Equal(10.0, resolved.Neighbours.Single().GateOffsetMs, 1);
    }

    [Fact]
    public void TheGateDialogsAutoSnapsToTheEarliestFront_NotToThePinItReplaces()
    {
        // Under a pin every offset is one absolute time; the dialog's Auto snap must come from the drivers, as the plot does.
        var pinned = new EqWizardPhaseContext(
            Gate(20.0),
            GateOffsetMs: 20.0,
            DetrendMs: 20.0,
            PinnedOffset: true,
            new PlacementChannel(Arriving(240), 240, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange,
                new PlacementChannel(Arriving(480), 480, default), 20.0)]);

        double fit = EqWizardPhase.AutoGateFitOffsetMs(pinned);

        Assert.Equal(5.0, fit, 1);
    }

    [Fact]
    public void AnEstimatedDetrendFollowsTheWindowsJustResolved()
    {
        // Auto τ must be estimated through the window this call resolved, not the replaced ones.
        var session = new EqWizardSession();
        var shared = new EqWizardPhaseContext(
            Gate(5.0),
            GateOffsetMs: 5.0,
            DetrendMs: 5.0,
            PinnedOffset: false,
            new PlacementChannel(Arriving(240), 240, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange,
                new PlacementChannel(Arriving(480), 480, default), 5.0)]);
        session.Load(Source(shared));

        ApplyPhaseGate(
            session, shared, offsetMs: 20.0, autoOffset: false, PhaseDetrendMode.Auto,
            detrendMs: 5.0, leftMs: 0.5, plateauMs: 4.0, rightMs: 1.5);

        EqWizardPhaseContext resolved = session.PhaseContext!;
        Assert.Equal(20.0, resolved.GateOffsetMs);
        Assert.Equal(20.0, resolved.Neighbours.Single().GateOffsetMs);
        Assert.NotEqual(5.0, resolved.DetrendMs);
    }

    [Fact]
    public void UnpinningAGateThatArrivedPinnedPutsEachWindowBackOnItsDriver()
    {
        // Auto after a pinned handoff must return windows to their own arrivals, not reuse the pinned offsets.
        var session = new EqWizardSession();
        var pinned = new EqWizardPhaseContext(
            Gate(4.0),
            GateOffsetMs: 4.0,
            DetrendMs: 10.25,
            PinnedOffset: true,
            new PlacementChannel(Arriving(240), 240, default),
            SampleRate,
            OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyColors.Orange,
                new PlacementChannel(Arriving(480), 480, default), 4.0)]);
        session.Load(Source(pinned));
        Assert.True(session.PhaseGatePinned);

        ApplyPhaseGate(session, pinned, offsetMs: 4.0, autoOffset: true);

        EqWizardPhaseContext auto = session.PhaseContext!;
        Assert.False(session.PhaseGatePinned);
        Assert.Equal(5.0, auto.GateOffsetMs, 1);
        Assert.Equal(10.0, auto.Neighbours.Single().GateOffsetMs, 1);
    }

    private static void ApplyPhaseGate(
        EqWizardSession session,
        EqWizardPhaseContext opened,
        double offsetMs,
        bool autoOffset,
        PhaseDetrendMode detrendMode = PhaseDetrendMode.Manual,
        double detrendMs = 11.5,
        double leftMs = 0.5,
        double plateauMs = 2.5,
        double rightMs = 1.5) =>
        session.ApplyPhaseGate(
            opened, offsetMs, autoOffset, leftMs, plateauMs, rightMs,
            PhaseWindowMode.FrequencyDependent, 6, detrendMode, detrendMs);

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

    private static Complex[] Arriving(int startSample)
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[startSample + i] =
                Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        return response;
    }

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
}
