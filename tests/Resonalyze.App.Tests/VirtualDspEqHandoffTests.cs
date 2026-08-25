using System.Numerics;
using System.Reflection;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

// The PEQ handoff between Virtual DSP and the EQ Wizard. The claims worth holding:
// what travels (the chain minus the PEQ under edit, or the raw measurement), how the
// curve is windowed (the DSP plot's own gate, so the wizard shows the very curve the
// user just left), which Auto Tune window a crossover implies, and where — and
// whether — a finished bank may land back.
public sealed class VirtualDspEqHandoffTests
{
    private const int SampleRate = 48_000;
    private const double TargetLevel = -41;

    private static readonly PhaseAnalysisSettings GateTemplate = new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        LeftMs: 2.0,
        PlateauMs: 12.0,
        RightMs: 5.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    // ----------------------------------------------------------------- builder

    [Fact]
    public void WithChain_AppliesTheChainWithoutItsPeq()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = new List<PeqBand> { new(1_000, 2, -9) };
        channel.Settings.PeqPreampDb = -3;

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        // Bit-exact against the same ApplyChain with only the PEQ removed: gain,
        // delay, polarity and the crossover all still shape the curve. The all-pass
        // does not — it is a band of the bank, and the bank is what is under edit.
        Complex[] expected = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain() with { Peq = null },
            SampleRate);
        Assert.Equal(expected, request.Source.Measurement!.ImpulseResponse);

        Complex[] withPeq = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain(),
            SampleRate);
        Assert.NotEqual(withPeq, request.Source.Measurement.ImpulseResponse);
    }

    [Fact]
    public void WithChain_CarriesTheNeighboursThePhaseViewDrawsAgainst()
    {
        // What makes an all-pass tunable at all: the drivers it has to line up with.
        // They travel as processed responses, so the wizard can re-read them at a gate
        // of its own, and with the window and τ the whole set was placed under.
        VirtualCrossoverChannel channel = BuildChannel();
        var neighbourResponse = new Complex[1_024];
        neighbourResponse[500] = 1.0;
        var context = new EqWizardPhaseContext(
            GateTemplate with { GateOffsetMs = 9.0 },
            GateOffsetMs: 9.5,
            DetrendMs: 10.0,
            PinnedOffset: false,
            new PlacementChannel(new Complex[1_024], 0, default),
            SampleRate,
            OxyPlot.OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyPlot.OxyColors.Orange, new PlacementChannel(neighbourResponse, 0, default), 9.75)]);

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, phaseContext: context);

        Assert.Same(context, request.Source.PhaseContext);
        Assert.Same(
            neighbourResponse,
            request.Source.PhaseContext!.Neighbours.Single().ImpulseResponse);
    }

    [Fact]
    public void RawHandoff_DrawsNoNeighbours()
    {
        // A raw curve has no crossover, no delay and no polarity in front of it, while
        // the neighbours have all of theirs — a Linkwitz-Riley corner alone turns 360°
        // through the overlap, and the delay moves the very arrival the phase is
        // referenced to. Drawing them together would invite lining up a system nobody
        // is building, and an all-pass tuned against that picture is wrong exactly
        // where it is supposed to help.
        VirtualCrossoverChannel channel = BuildChannel();
        var context = new EqWizardPhaseContext(
            GateTemplate,
            GateOffsetMs: 9.5,
            DetrendMs: 10.0,
            PinnedOffset: false,
            new PlacementChannel(new Complex[1_024], 0, default),
            SampleRate,
            OxyPlot.OxyColors.SkyBlue,
            [new EqWizardPhaseNeighbour(
                "B", OxyPlot.OxyColors.Orange, new PlacementChannel(new Complex[1_024], 0, default), 9.75)]);

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: false, phaseContext: context);

        Assert.Null(request.Source.PhaseContext);
    }

    [Fact]
    public void WithChain_WindowsAtTheRenderAnchor_AndThePinWins()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        VirtualDspEqHandoffRequest anchored = Build(
            channel, withChain: true, renderAnchorIndex: 480);
        Assert.Equal(10.0, anchored.Source.GateSettings!.GateOffsetMs, 6);
        Assert.Equal(480, anchored.Source.Measurement!.PeakIndex);

        VirtualDspEqHandoffRequest pinned = Build(
            channel, withChain: true, pinnedGateOffsetMs: 12.5, renderAnchorIndex: 480);
        Assert.Equal(12.5, pinned.Source.GateSettings!.GateOffsetMs, 6);

        // The rest of the template must travel untouched — it IS the DSP gate.
        Assert.Equal(
            GateTemplate with { GateOffsetMs = 12.5 },
            pinned.Source.GateSettings);
    }

    [Fact]
    public void WithChain_WithoutARenderToFollow_OpensOnItsOwnFront()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: null);

        // The same rule the plot applies to a lone channel: the response START.
        Complex[] response = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain() with { Peq = null },
            SampleRate,
            out ValidSampleRange validRange);
        int expectedAnchor = ProcessedChannels.StartAnchorIndex(
            response,
            VirtualCrossoverAnalysis.FindPeakIndex(response),
            SampleRate,
            validRange);
        Assert.Equal(expectedAnchor, request.Source.Measurement!.PeakIndex);
        Assert.Equal(
            expectedAnchor * 1_000.0 / SampleRate,
            request.Source.GateSettings!.GateOffsetMs,
            6);
    }

    [Fact]
    public void Raw_HandsTheMeasurementItself_AnchoredOnItsOwnStart()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        // A pin belongs to the processed view's time; the panel's Raw curve ignores
        // it and so must the raw handoff. Like the panel's Raw curve, the window
        // anchors on the raw response's own START (the peak only answers when the
        // estimator refuses the record) — a woofer's peak trails its onset by more
        // than the steady-state window's 2 ms fade-in, so a peak anchor would hand
        // the wizard the record minus its direct arrival.
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: false, pinnedGateOffsetMs: 12.5, renderAnchorIndex: 480);

        Assert.Same(
            channel.TransferImpulseResponse,
            request.Source.Measurement!.ImpulseResponse);
        int expectedAnchor = ProcessedChannels.StartAnchorIndex(
            channel.TransferImpulseResponse!,
            channel.TransferPeakIndex,
            SampleRate);
        Assert.Equal(expectedAnchor, request.Source.Measurement.PeakIndex);
        Assert.Equal(
            expectedAnchor * 1_000.0 / SampleRate,
            request.Source.GateSettings!.GateOffsetMs,
            6);
    }

    [Fact]
    public void TheWizardRendersTheRequestExactlyAsTheDspPanelWould()
    {
        // The invariant the handoff promises: the wizard's source-curve call
        // (GetGatedPrimarySpectrum with the request's measurement and gate) equals the
        // DSP magnitude view's own build of the bypass-chain response — same helper,
        // same inputs, composed independently here.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: 480);

        AnalysisCurve wizard = DataHelper.GetGatedPrimarySpectrum(
            request.Source.Measurement!,
            request.Source.GateSettings!,
            calibration: null,
            smoothingInverseOctaves: 0);
        AnalysisCurve panel = DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                VirtualCrossoverAnalysis.ApplyChain(
                    channel.TransferImpulseResponse!,
                    channel.Settings.ToChain() with { Peq = null },
                    SampleRate),
                480,
                SampleRate),
            GateTemplate with { GateOffsetMs = 10.0 },
            calibration: null,
            smoothingInverseOctaves: 0);

        Assert.Equal(panel.Points.Count, wizard.Points.Count);
        for (int i = 0; i < panel.Points.Count; i++)
        {
            Assert.Equal(panel.Points[i].X, wizard.Points[i].X);
            Assert.Equal(panel.Points[i].Y, wizard.Points[i].Y);
        }
    }

    [Fact]
    public void TheCorrectedPreviewIsThePanelsOwnBuildOfTheSameBank()
    {
        // The invariant the whole handoff exists for, at the point it is hardest to
        // hold: with a bank loaded. The wizard filters and THEN gates — one ApplyChain
        // of the whole chain from the original measurement — which is exactly what the
        // panel does for a channel carrying that PEQ.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: 480);
        var bank = new EqualizationCurve(
            new[] { new PeqBand(100, 5, -6), new PeqBand(3_000, 2, 3) }, preampDb: -2);

        IReadOnlyList<SignalPoint> wizard = EqWizardGatedPreview.Render(
            new EqWizardGatedPreviewRequest(
                request.Source.PreviewImpulseResponse!,
                request.Source.PreviewChain!,
                bank,
                request.Source.Measurement!.PeakIndex,
                SampleRate,
                request.Source.GateSettings!,
                Calibration: null,
                SmoothingInverseOctaves: 0));

        AnalysisCurve panel = DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                VirtualCrossoverAnalysis.ApplyChain(
                    channel.TransferImpulseResponse!,
                    channel.Settings.ToChain() with { Peq = bank },
                    SampleRate),
                480,
                SampleRate),
            GateTemplate with { GateOffsetMs = 10.0 },
            calibration: null,
            smoothingInverseOctaves: 0);

        Assert.Equal(panel.Points.Count, wizard.Count);
        for (int i = 0; i < panel.Points.Count; i++)
        {
            Assert.Equal(panel.Points[i].Y, wizard[i].Y);
        }
    }

    [Fact]
    public void TheCorrectedPreviewDivergesFromTheIdealMagnitude_WhichIsWhyItIsComputed()
    {
        // The reason the preview cannot simply add the filter's ideal magnitude to the
        // bare curve: a window does not commute with a filter. A Q 5 band at 100 Hz
        // under a 6 ms gate reads several dB apart between the two, so if this ever
        // stops being true the expensive path has lost its justification.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: 480);
        var bank = new EqualizationCurve(new[] { new PeqBand(100, 5, -6) });

        IReadOnlyList<SignalPoint> bare = EqWizardGatedPreview.Render(
            Preview(request, bank: null));
        IReadOnlyList<SignalPoint> honest = EqWizardGatedPreview.Render(
            Preview(request, bank));

        double worstAgainstIdeal = 0;
        for (int i = 0; i < bare.Count; i++)
        {
            double hz = bare[i].X;
            if (hz < 80 || hz > 125)
            {
                continue;
            }

            double ideal = bare[i].Y +
                DigitalEqualizationResponse.MagnitudeDbAt(bank, hz, SampleRate);
            worstAgainstIdeal = Math.Max(worstAgainstIdeal, Math.Abs(ideal - honest[i].Y));
        }

        Assert.True(
            worstAgainstIdeal > 1.0,
            $"expected the gate to swallow much of a Q 5 band, saw {worstAgainstIdeal:0.00} dB");
    }

    [Fact]
    public void TheBareCurveIsTheCorrectedPathWithNoBank()
    {
        // Both curves come from one renderer, so they cannot drift: the source curve is
        // literally the corrected one with nothing substituted in.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: 480);

        IReadOnlyList<SignalPoint> bare = EqWizardGatedPreview.Render(
            Preview(request, bank: null));
        AnalysisCurve panel = DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                VirtualCrossoverAnalysis.ApplyChain(
                    channel.TransferImpulseResponse!,
                    channel.Settings.ToChain() with { Peq = null },
                    SampleRate),
                480,
                SampleRate),
            GateTemplate with { GateOffsetMs = 10.0 },
            calibration: null,
            smoothingInverseOctaves: 0);

        Assert.Equal(panel.Points.Count, bare.Count);
        for (int i = 0; i < panel.Points.Count; i++)
        {
            Assert.Equal(panel.Points[i].Y, bare[i].Y);
        }
    }

    [Fact]
    public void TheCrossoverSetsTheAutoTuneWindow_CornerToCorner()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        channel.Settings.HighPassEdge = channel.Settings.HighPassEdge with { FrequencyHz = 80 };
        channel.Settings.LowPassEdge = channel.Settings.LowPassEdge with { FrequencyHz = 500 };

        VirtualDspEqHandoffRequest bandPass = Build(channel, withChain: true);
        Assert.Equal(80, bandPass.AutoTuneMinHz);
        Assert.Equal(500, bandPass.AutoTuneMaxHz);

        channel.Settings.CrossoverKind = CrossoverKind.HighPass;
        VirtualDspEqHandoffRequest highPass = Build(channel, withChain: true);
        Assert.Equal(80, highPass.AutoTuneMinHz);
        Assert.Equal(20_000, highPass.AutoTuneMaxHz);

        channel.Settings.CrossoverKind = CrossoverKind.LowPass;
        VirtualDspEqHandoffRequest lowPass = Build(channel, withChain: true);
        Assert.Equal(20, lowPass.AutoTuneMinHz);
        Assert.Equal(500, lowPass.AutoTuneMaxHz);

        // No crossover means no opinion: the wizard's window stays where it was.
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest off = Build(channel, withChain: true);
        Assert.Null(off.AutoTuneMinHz);
        Assert.Null(off.AutoTuneMaxHz);

        // Raw edits deliberately get none either — the band belongs to the chain
        // the raw curve is measured without.
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        VirtualDspEqHandoffRequest raw = Build(channel, withChain: false);
        Assert.Null(raw.AutoTuneMinHz);
        Assert.Null(raw.AutoTuneMaxHz);
    }

    [Fact]
    public void TheChannelsPeqSeedsTheBank_AndItsPinningTravels()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = new List<PeqBand>
        {
            new(63, 1.4, 3),
            new(4_000, 8, -4.5)
        };
        channel.Settings.PeqPreampDb = -2.5;

        CalibrationFile calibration = CalibrationFile.Parse("20 0\n20000 1.5\n");
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, targetLevelDb: -41, calibration: calibration);

        Assert.Equal(channel.Settings.PeqBands, request.BankSeed.Bands);
        Assert.Equal(-2.5, request.BankSeed.PreampDb);
        // The DSP panel's target level travels verbatim: the source is rendered in
        // that plot's own dB frame, so "one target" means one height too.
        Assert.Equal(-41, request.TargetLevelDb);
        Assert.Equal(EqWizardSourceKind.VirtualDspChannel, request.Source.Kind);
        // The curve itself travels, not an id: the panel may be drawing with a curve
        // its session carries, which the wizard's own list could never resolve.
        Assert.Same(calibration, request.Source.PinnedCalibration);
        Assert.Equal("mic-1", request.Source.PinnedCalibrationName);
        Assert.Same(calibration, request.Token.Calibration);
        Assert.Equal(SampleRate, request.Source.SampleRateHz);
        // Pinned: the selector must come up disabled, yet smoothing stays live.
        Assert.False(request.Source.SupportsCalibration);
        Assert.True(request.Source.SupportsSmoothing);
        Assert.Same(channel, request.Token.Channel);
        Assert.False(request.Token.RightSide);
    }

    [Fact]
    public void AMonoHandoffAddressesTheLeftSet_EvenFromTheRightView()
    {
        // Mono routes the right view to the single left set; the token must say LEFT
        // outright, so a pair un-mono'd mid-edit still receives the result on the set
        // the tune was taken from — not on a right slot the wizard never saw.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Pair.Mono = true;
        channel.ActiveRight = true;

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        Assert.False(request.Token.RightSide);
    }

    [Fact]
    public void TheMeasurementsCoherenceTravels()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        // 5 bins pair with an 8-sample FFT: k = 1..4 at k · rate / 8.
        channel.TransferCoherence = [1.0, 0.95, 0.9, 0.8, 0.7];

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        Assert.NotNull(request.Source.Coherence);
        Assert.Equal(4, request.Source.Coherence!.Count);
        Assert.Equal(SampleRate / 8.0, request.Source.Coherence[0].X, 6);
        Assert.Equal(0.95, request.Source.Coherence[0].Y);
    }

    // ------------------------------------------------------------------ return

    [Fact]
    public void ReturnLandsOnTheSideTheTokenNames_NotTheActiveOne()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: true);
        // The user flipped back to the left side while editing.
        channel.ActiveRight = false;
        var curve = new EqualizationCurve(
            new[] { new PeqBand(250, 3, -6) }, preampDb: -1.5);

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));

        VirtualCrossoverChannelSettings right = channel.Pair.SideFor(rightSide: true);
        Assert.Equal(curve.Bands, right.PeqBands);
        Assert.Equal(-1.5, right.PeqPreampDb);
        Assert.Equal("EQ Wizard", right.PeqSourceName);
        // The left side — the one on screen — is untouched.
        Assert.Empty(channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void AHandoffTakenFromAMonoPair_LandsOnItsSurvivingSet()
    {
        // Mono routes both sides to the single left set, so the handoff addresses it
        // outright and a write into the unreachable right slot cannot happen.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Pair.Mono = true;
        channel.ActiveRight = true;
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(request.Token.RightSide);
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void ReturnAfterThePairChangedRouting_Refuses()
    {
        // Taken from the right side of a stereo pair; the pair then became mono,
        // which sends SideFor(true) to the LEFT settings. Delivering there would put
        // the right side's tune on the shared set without a word.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: true);
        channel.Pair.Mono = true;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void ReturnAfterTheSideGotANewMeasurement_Refuses()
    {
        // The session survives a trip back to Virtual DSP by the tab, so the user can
        // give that very side a different measurement there. The bank was computed
        // from a curve that no longer exists — and the wizard, still showing the old
        // one, gives no sign of it.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.SideState(rightSide: false).BeginSourceLoad();
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheCalibrationChanged_Refuses()
    {
        // The wizard disables its own calibration selector during a handoff, because
        // a bank fitted under one correction and summed under another is not the same
        // bank. The Virtual DSP panel's selector is reachable by a plain tab switch —
        // which a session deliberately survives — so the same rule has to hold on the
        // way back, or that lock is decorative.
        VirtualCrossoverChannel channel = BuildChannel();
        CalibrationFile fitted = CalibrationFile.Parse("20 0\n20000 1.5\n");
        VirtualDspEqReturnToken token =
            TokenFor(channel, rightSide: false) with { Calibration = fitted };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1,
            CalibrationFile.Parse("20 0\n20000 -1.5\n"), GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);

        // Turning it off entirely is a change too — not a way back to "no opinion".
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);

        // The same correction, re-read (a settings refresh hands the panel a fresh
        // instance of the same file), is the same correction: curves compare by
        // content, never by reference or by the name this machine gives them.
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1,
            CalibrationFile.Parse("20 0\n20000 1.5\n"), GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterThePanelsPeqWasReplacedOrCleared_Refuses()
    {
        // A lost update the chain check cannot see, because it excludes the very stage
        // under edit: with a session open, the panel's own PEQ row still offers Load
        // from file and Clear, and the older bank would silently win.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = new List<PeqBand> { new(120, 2, -4) };
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var newer = new List<PeqBand> { new(3_000, 4, 2) };
        channel.Settings.PeqBands = newer;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(newer, channel.Settings.PeqBands);

        // Cleared counts the same: an empty bank is a state, not an absence.
        VirtualDspEqReturnToken second = TokenFor(channel, rightSide: false);
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, second, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheGateMoved_Refuses()
    {
        // The window is half of what the curve IS, and the panel's gate dialog is a
        // tab switch away while a session runs. Both the shape of the window and where
        // the user pinned it count.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate with { PlateauMs = 400 }, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterThePanelsTargetLevelMoved_Refuses()
    {
        // The bank's preamp was fitted against an absolute level. If the panel's own
        // has been moved independently meanwhile, the wizard's answer and the panel's
        // conflict — and silently picking either would be a guess.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate, null, targetLevelDb: TargetLevel + 5, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheGainChanged_Refuses()
    {
        // The gain does not bend the curve, but the handoff carries the panel's
        // ABSOLUTE target level and the bank's preamp was fitted against it. Moving
        // the channel 6 dB after the fact leaves the returned tune exactly 6 dB off
        // the target the wizard was aiming at — while still drawing the old level.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.GainDb -= 6;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheCrossoverChanged_Refuses()
    {
        // The crossover is the one chain stage that BENDS the magnitude: the bank was
        // fitted to a shape that a different corner or slope no longer produces.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.LowPassEdge =
            channel.Settings.LowPassEdge with { FrequencyHz = 900 };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);

        // Switching it off entirely is a change too.
        VirtualDspEqReturnToken fresh = TokenFor(channel, rightSide: false);
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, fresh, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterADelayOrAnAllPassBandEdit_Refuses()
    {
        // Swept across the ranges the UI allows, these are NOT free: at 192 kHz, where
        // the rate clamps the window to 171 ms, a delay edit moves the gated shape by
        // up to 1.70 dB and an all-pass by 4.77 dB (40 Hz, Q 20 — 318 ms of group
        // delay against that window). See SteadyStateWindowTests. The delay refuses
        // through the chain comparison; the all-pass now rides in the bank, so it
        // refuses through the PEQ guard instead — a phase-only band is exactly the
        // edit a magnitude-only comparison would wave through.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken delayToken = TokenFor(channel, rightSide: false);
        channel.Settings.DelayMs += 4.2;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, delayToken, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));

        channel.Settings.PeqBands =
            [new PeqBand(40, 2, 0, PeqBandType.AllPassSecondOrder)];
        VirtualDspEqReturnToken apToken = TokenFor(channel, rightSide: false);
        channel.Settings.PeqBands =
            [new PeqBand(40, 20, 0, PeqBandType.AllPassSecondOrder)];
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, apToken, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(20, Assert.Single(channel.Settings.PeqBands).Q);
    }

    [Fact]
    public void ReturnAfterAPolarityFlip_IsAllowed()
    {
        // The one chain stage that survives: a polarity flip is -1 at every frequency,
        // so it changes neither the shape the bank corrects nor the level it was
        // fitted against — measured as exactly 0 dB at every rate.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.InvertPolarity = !channel.Settings.InvertPolarity;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void AChainSessionWithNoCrossoverStillNoticesOneBeingTurnedOn()
    {
        // The distinction a null crossover could blur: "raw handoff, no crossover in
        // the curve" versus "chain handoff whose crossover happened to be off". They
        // stay apart because ToChain yields CrossoverSpec.Off — a value, not null —
        // while only DspChannelChain.Identity (the raw preview chain) carries null.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        Assert.Equal(CrossoverSpec.Off, request.Token.PreviewChain.Crossover);
        Assert.True(request.Token.WithChain);

        channel.Settings.CrossoverKind = CrossoverKind.HighPass;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ChangingHowThePhaseViewReads_DoesNotRefuseTheReturn()
    {
        // The gate template is shared with the phase and impulse views, and the
        // magnitude forces Fixed and ignores its FDW cycles, detrend and unwrap
        // entirely. Refusing over those would cost a finished tune because the user
        // changed a view the handoff's curve never came from.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate with
            {
                FdwCycles = 8,
                DetrendMode = PhaseDetrendMode.Manual,
                ManualDetrendMilliseconds = 3,
                Unwrap = true
            },
            null,
            TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ChangingTheMagnitudeWindowItself_StillRefuses()
    {
        // The other half: the durations and the mode ARE what the magnitude reads.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate with { PlateauMs = 400 }, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void TheRequestCarriesTheLevelRangeThePanelCanHold()
    {
        // The wizard's own box is wider than the panel's, and the level travels back:
        // without the range the wizard could offer a level that arrives clamped, and
        // the tune would realize a height it was never fitted to.
        VirtualCrossoverChannel channel = BuildChannel();

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        Assert.Equal(-120, request.TargetLevelMinDb);
        Assert.Equal(60, request.TargetLevelMaxDb);
    }

    [Fact]
    public void ARawSessionIsImmuneToTheProcessedGatePin()
    {
        // A raw handoff anchors on the measurement's own start and never reads the
        // processed view's pin, so moving that pin cannot have changed its curve —
        // refusing the return would cost the user a finished tune for nothing.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void AChainSessionIsNotImmuneToThatPin()
    {
        // The other half, so the exemption above cannot silently widen: a chain
        // handoff DOES read the pin, and a moved pin re-windows its curve.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ARawSessionIsImmuneToCrossoverEdits()
    {
        // A raw handoff's curve is measured WITHOUT the chain, so the crossover cannot
        // have shaped it and changing one cannot invalidate the bank.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: false);
        channel.Settings.LowPassEdge =
            channel.Settings.LowPassEdge with { FrequencyHz = 900 };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(request.Token.WithChain);
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheProjectWasReplaced_RefusesEvenThoughTheChannelSurvives()
    {
        // Binding a project REUSES the runtime channel objects when the channel count
        // matches — only its Pair is swapped — so the object the token names is still
        // in the panel's list afterwards while describing a different session. Without
        // the generation this wrote a bank tuned against one car into another.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token =
            TokenFor(channel, rightSide: false) with { ProjectGeneration = 4 };
        // What an import does to the very object the token holds.
        channel.Pair = new VirtualCrossoverChannelPairSettings();
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 5, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Empty(channel.Settings.PeqBands);

        // The generation ALONE is what refuses it: an untouched channel of the same
        // shape, addressed at its own generation, still lands — so the test cannot
        // pass merely because some other guard happened to fire.
        VirtualCrossoverChannel untouched = BuildChannel();
        VirtualDspEqReturnToken control =
            TokenFor(untouched, rightSide: false) with { ProjectGeneration = 4 };
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { untouched }, control, curve, projectGeneration: 5, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { untouched }, control, curve, projectGeneration: 4, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));
        Assert.Equal(curve.Bands, untouched.Settings.PeqBands);
    }

    [Fact]
    public void ReturnToAChannelNoLongerInThePanel_RefusesAndWritesNothing()
    {
        VirtualCrossoverChannel removed = BuildChannel();
        VirtualCrossoverChannel survivor = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(removed, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { survivor }, token, curve, projectGeneration: 1, calibration: null, GateTemplate, null, TargetLevel, spatialAverage: null));

        Assert.Empty(removed.Settings.PeqBands);
        Assert.Empty(survivor.Settings.PeqBands);
    }

    // ------------------------------------------------------------------ helpers

    // A token addressing a channel exactly as it stands now — what Build would
    // write for it — so a test can then change ONE thing and see the return judged.
    private static VirtualDspEqReturnToken TokenFor(
        VirtualCrossoverChannel channel, bool rightSide) =>
        new(
            channel,
            rightSide && !channel.Pair.Mono,
            ProjectGeneration: 1,
            channel.SideState(rightSide).SourceRevision,
            channel.Pair.Mono,
            channel.SideSettings(rightSide).ToChain() with { Peq = null },
            WithChain: true,
            new PeqBankState(
                channel.SideSettings(rightSide).PeqBands,
                channel.SideSettings(rightSide).PeqPreampDb),
            TargetLevel,
            GateTemplate,
            PinnedGateOffsetMs: null,
            Calibration: null,
            SpatialAverage: null);

    private static EqWizardGatedPreviewRequest Preview(
        VirtualDspEqHandoffRequest request, EqualizationCurve? bank) =>
        new(
            request.Source.PreviewImpulseResponse!,
            request.Source.PreviewChain!,
            bank,
            request.Source.Measurement!.PeakIndex,
            SampleRate,
            request.Source.GateSettings!,
            Calibration: null,
            SmoothingInverseOctaves: 0);

    private static VirtualDspEqHandoffRequest Build(
        VirtualCrossoverChannel channel,
        bool withChain,
        double? pinnedGateOffsetMs = null,
        int? renderAnchorIndex = 480,
        double targetLevelDb = -41,
        CalibrationFile? calibration = null,
        long projectGeneration = 1,
        EqWizardPhaseContext? phaseContext = null,
        LiveCaptureDocument? spatialAverage = null,
        double spatialAverageOffsetDb = 0) =>
        VirtualDspEqHandoff.Build(
            channel,
            channel.ActiveRight,
            withChain,
            GateTemplate,
            pinnedGateOffsetMs,
            renderAnchorIndex,
            phaseContext,
            targetLevelDb,
            targetLevelMinDb: -120,
            targetLevelMaxDb: 60,
            smoothingInverseOctaves: 0,
            calibration,
            calibrationName: calibration == null ? null : "mic-1",
            projectGeneration,
            spatialAverage,
            spatialAverageOffsetDb);

    // -------------------------------------------------------- spatial average

    /// <summary>
    /// On the hybrid view the capture REPLACES the magnitude, and the impulse response
    /// stays for the phase view. That split is the whole feature: the average is where
    /// tonal balance is honest, the impulse response is where timing is.
    /// </summary>
    [Fact]
    public void WithASpatialAverage_TheCaptureTravelsAndTheMagnitudeStopsBeingGated()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, spatialAverage: capture, spatialAverageOffsetDb: -73.5);

        Assert.Same(capture, request.Source.SpatialAverage);
        Assert.Equal(-73.5, request.Source.SpatialAverageOffsetDb);
        // Not gated: an average is a steady-state curve with no window at all, so the
        // magnitude side must not be routed through the gated preview.
        Assert.False(request.Source.IsGated);
        // But the phase side still has everything it needs, and reads the impulse
        // response through the panel's gate.
        Assert.NotNull(request.Source.Measurement);
        Assert.NotNull(request.Source.PreviewImpulseResponse);
        Assert.NotNull(request.Source.GateSettings);
        Assert.Contains("MMM", request.Source.DisplayName);
    }

    /// <summary>
    /// Without one, nothing changes: the panel is drawing impulse responses and the
    /// handoff hands impulse responses over, gated exactly as before.
    /// </summary>
    [Fact]
    public void WithoutASpatialAverage_TheMagnitudeIsStillGated()
    {
        VirtualDspEqHandoffRequest request = Build(BuildChannel(), withChain: true);

        Assert.Null(request.Source.SpatialAverage);
        Assert.True(request.Source.IsGated);
    }

    /// <summary>
    /// The bank the wizard fits is fitted against the CAPTURE, not against the
    /// impulse response — which is what the whole hybrid exists for. Driven through a
    /// live panel, because the choice is made where the source curve is computed.
    /// </summary>
    [Fact]
    public void TheWizardsSourceCurveComesFromTheCapture()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        // No chain and no offset, so the curve IS the capture: a level nothing in the
        // impulse-response path could produce.
        channel.Settings.GainDb = 0;
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, spatialAverage: Capture(), spatialAverageOffsetDb: 0);

        using var panel = new EqWizardPanel();
        typeof(EqWizardPanel)
            .GetMethod("BeginVirtualDspHandoff", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [request]);
        object? curve = typeof(EqWizardPanel)
            .GetMethod("GetSourceCurve", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, []);

        Assert.NotNull(curve);
        var points = (IReadOnlyList<DataPoint>)typeof(EqWizardCurve)
            .GetProperty("Points")!
            .GetValue(curve)!;
        Assert.NotEmpty(points);
        Assert.All(
            points.Where(point => point.X is > 100 and < 10_000),
            point => Assert.Equal(-20, point.Y, 3));
    }

    /// <summary>
    /// A bank fitted against the spatial average must not land on a panel that has
    /// gone back to its impulse responses. It is the same invisible divergence the
    /// calibration guard refuses, and by the same line: it moves the MAGNITUDE the
    /// bank was fitted against, and the wizard is still showing what it opened on.
    /// </summary>
    [Fact]
    public void ReturnAfterTheHybridWasTurnedOff_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();
        VirtualDspEqReturnToken token = Build(
            channel, withChain: true, spatialAverage: capture).Token;
        var curve = new EqualizationCurve([new PeqBand(120, 1.4, -3)], -1.5);

        // The panel is back on impulse responses: nothing to hand over now.
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null,
            GateTemplate, null, TargetLevel, spatialAverage: null));

        // A DIFFERENT capture is refused too, even one whose numbers match: it is a
        // different measurement, and a re-attached file is a different capture.
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null,
            GateTemplate, null, TargetLevel, spatialAverage: Capture()));

        // The one it was fitted against still lands.
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null,
            GateTemplate, null, TargetLevel, spatialAverage: capture));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    // A flat spatial average at a known level, so what the wizard draws identifies
    // which of the two measurements it read.
    private static LiveCaptureDocument Capture() => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "l tw mmm",
        CurveDb = Enumerable.Repeat(-20.0, 1_024).ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };

    // A channel whose left side holds a synthetic measurement: a decaying wavelet
    // arriving at sample 480 (10 ms), through a full DSP chain so every stage has
    // something to prove it travelled — or was left out.
    private static VirtualCrossoverChannel BuildChannel()
    {
        var impulseResponse = new Complex[4_096];
        for (int i = 0; i < 64; i++)
        {
            impulseResponse[480 + i] =
                Math.Exp(-i / 12.0) * Math.Cos(2 * Math.PI * i / 16.0);
        }

        var channel = new VirtualCrossoverChannel("A");
        channel.SampleRate = SampleRate;
        channel.TransferImpulseResponse = impulseResponse;
        channel.TransferPeakIndex = 480;
        channel.Settings.GainDb = -3;
        channel.Settings.DelayMs = 1.25;
        channel.Settings.InvertPolarity = true;
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        channel.Settings.HighPassEdge = channel.Settings.HighPassEdge with { FrequencyHz = 80 };
        channel.Settings.LowPassEdge = channel.Settings.LowPassEdge with { FrequencyHz = 500 };
        return channel;
    }
}
