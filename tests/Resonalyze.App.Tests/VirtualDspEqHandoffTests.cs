using System.Numerics;
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
        // delay, polarity, crossover and all-pass all still shape the curve.
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
    public void Raw_HandsTheMeasurementItself_AnchoredOnItsOwnPeak()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        // A pin belongs to the processed view's time; the panel's Raw curve ignores
        // it and so must the raw handoff.
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: false, pinnedGateOffsetMs: 12.5, renderAnchorIndex: 480);

        Assert.Same(
            channel.TransferImpulseResponse,
            request.Source.Measurement!.ImpulseResponse);
        Assert.Equal(channel.TransferPeakIndex, request.Source.Measurement.PeakIndex);
        Assert.Equal(
            channel.TransferPeakIndex * 1_000.0 / SampleRate,
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

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, targetLevelDb: -41, calibrationId: "mic-1");

        Assert.Equal(channel.Settings.PeqBands, request.BankSeed.Bands);
        Assert.Equal(-2.5, request.BankSeed.PreampDb);
        // The DSP panel's target level travels verbatim: the source is rendered in
        // that plot's own dB frame, so "one target" means one height too.
        Assert.Equal(-41, request.TargetLevelDb);
        Assert.Equal(EqWizardSourceKind.VirtualDspChannel, request.Source.Kind);
        Assert.Equal("mic-1", request.Source.PinnedCalibrationId);
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
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: null));

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
            new[] { channel }, request.Token, curve, projectGeneration: 1, calibrationId: null));
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
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: null));
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
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: null));
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
        VirtualDspEqReturnToken token =
            TokenFor(channel, rightSide: false) with { CalibrationId = "0deg" };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: "90deg"));
        Assert.Empty(channel.Settings.PeqBands);

        // Turning it off entirely is a change too — not a way back to "no opinion".
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: null));
        Assert.Empty(channel.Settings.PeqBands);

        // The same correction, differently spelled, is the same correction: the ids
        // are normalized and compared without case, exactly as the selector stores them.
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: " 0DEG "));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterAChainEdit_IsAllowed()
    {
        // The line the refusals stop at: crossover, gain, delay and the all-pass are
        // the user's own knobs on their own channel. The bank stays theirs to apply,
        // and throwing away real work over a change they made on purpose would be
        // worse than the staleness it guards.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.GainDb = -9;
        channel.Settings.LowPassEdge =
            channel.Settings.LowPassEdge with { FrequencyHz = 900 };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibrationId: null));
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
            new[] { channel }, token, curve, projectGeneration: 5, calibrationId: null));
        Assert.Empty(channel.Settings.PeqBands);

        // The same token against the generation it was taken in still lands.
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 4, calibrationId: null));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnToAChannelNoLongerInThePanel_RefusesAndWritesNothing()
    {
        VirtualCrossoverChannel removed = BuildChannel();
        VirtualCrossoverChannel survivor = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(removed, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { survivor }, token, curve, projectGeneration: 1, calibrationId: null));

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
            CalibrationId: null);

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
        string? calibrationId = null,
        long projectGeneration = 1) =>
        VirtualDspEqHandoff.Build(
            channel,
            channel.ActiveRight,
            withChain,
            GateTemplate,
            pinnedGateOffsetMs,
            renderAnchorIndex,
            targetLevelDb,
            smoothingInverseOctaves: 0,
            calibrationId,
            projectGeneration);

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
        channel.Settings.AllPassType = AllPassType.SecondOrder;
        channel.Settings.AllPassFrequencyHz = 300;
        return channel;
    }
}
