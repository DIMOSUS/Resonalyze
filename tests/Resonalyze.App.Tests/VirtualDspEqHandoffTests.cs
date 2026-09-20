using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

    [Fact]
    public void TheHandoffCarriesTheChannelsEffectiveCrossover_ForTheTargetsShape()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        VirtualDspEqHandoffRequest chained = Build(channel, withChain: true);
        VirtualDspEqHandoffRequest raw = Build(channel, withChain: false);

        Assert.Equal(channel.Settings.EffectiveCrossover, chained.Source.TargetCrossover);
        // A raw handoff has no chain in its curve, so there is no slope to follow.
        Assert.Null(raw.Source.TargetCrossover);

        // A FIR crossover lives in the chain as a kernel, with the built chain's Crossover Off; the effective
        // crossover carries its design corners, and that is what the target follows.
        var design = new FirCrossoverDesign(
            CrossoverKind.HighPass,
            channel.Settings.HighPassEdge,
            channel.Settings.HighPassEdge,
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            255,
            SampleRate);
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        channel.Settings.Fir = design.Build();
        channel.Settings.FirDesign = design;

        VirtualDspEqHandoffRequest fir = Build(channel, withChain: true);

        Assert.Equal(CrossoverKind.Off, fir.Token.PreviewChain.Crossover?.Kind ?? CrossoverKind.Off);
        // The kernel travels, and the IIR stage does not: with the IIR off, EffectiveCrossover stands in for the
        // design, and sending it as well would shape the target with the same filter twice.
        Assert.Null(fir.Source.TargetCrossover);
        Assert.NotNull(fir.Source.TargetCrossoverFir);

        // Both at once is legitimate though rare, and then both shape the target.
        channel.Settings.CrossoverKind = CrossoverKind.LowPass;
        VirtualDspEqHandoffRequest both = Build(channel, withChain: true);

        Assert.Equal(CrossoverKind.LowPass, both.Source.TargetCrossover!.Kind);
        Assert.NotNull(both.Source.TargetCrossoverFir);
    }

    [Fact]
    public void AStatedAcousticCrossover_IsWhatTheTargetFollows_NotTheElectricalFilterUnderIt()
    {
        // A junction tune asked for acoustic LR24 and picked an electrical LR12 to get there on this driver. If the
        // EQ then aimed at the LR12 it would flatten the driver back out wherever boosts are still allowed, undoing
        // the acoustic slope the tune just found - so the goal the handoff carries is the ACOUSTIC one, per edge.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        channel.Settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 500, 12);
        channel.Settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24);
        channel.Settings.AcousticLowPass =
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);

        CrossoverSpec goal = Assert.IsType<CrossoverSpec>(
            Build(channel, withChain: true).Source.TargetCrossover);

        Assert.Equal(24, goal.LowPassEdge!.Value.SlopeDbPerOctave);
        Assert.Equal(500, goal.LowPassEdge!.Value.FrequencyHz);
        // The edge nobody stated anything about is still the electrical one.
        Assert.Equal(channel.Settings.HighPassEdge, goal.HighPassEdge);
        Assert.Equal(CrossoverKind.BandPass, goal.Kind);

        // A wish is a family and a slope: its corner is always the electrical one, so moving the corner moves the
        // wish with it. Nothing goes stale, which is what lets it live on the channel card.
        channel.Settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 12);
        CrossoverSpec moved = Build(channel, withChain: true).Source.TargetCrossover!;
        Assert.Equal(200, moved.LowPassEdge!.Value.FrequencyHz);
        Assert.Equal(24, moved.LowPassEdge!.Value.SlopeDbPerOctave);

        // Cleared on the card, the target is the electrical filter again.
        channel.Settings.AcousticLowPass = null;
        Assert.Equal(
            channel.Settings.EffectiveCrossover,
            Build(channel, withChain: true).Source.TargetCrossover);
    }

    [Fact]
    public void WithChain_AppliesTheChainWithoutItsPeq()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = new List<PeqBand> { new(1_000, 2, -9) };
        channel.Settings.PeqPreampDb = -3;

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        // The all-pass is a PEQ band, so it is excluded along with the bank under edit.
        Complex[] expected = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain(channel.Pair.Zone) with { Peq = null },
            SampleRate,
            SampleRate);
        Assert.Equal(expected, request.Source.Measurement!.ImpulseResponse);

        Complex[] withPeq = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain(channel.Pair.Zone),
            SampleRate,
            SampleRate);
        Assert.NotEqual(withPeq, request.Source.Measurement.ImpulseResponse);
    }

    [Fact]
    public void WithChain_CarriesTheNeighboursThePhaseViewDrawsAgainst()
    {
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
        // A raw curve must not travel with processed neighbours: an LR corner alone turns 360 deg through the overlap.
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

        Complex[] response = VirtualCrossoverAnalysis.ApplyChain(
            channel.TransferImpulseResponse!,
            channel.Settings.ToChain(channel.Pair.Zone) with { Peq = null },
            SampleRate,
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

        // Raw handoff ignores the pin and anchors on the raw START: a woofer's peak trails its onset by more than the 2 ms fade-in.
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
                    channel.Settings.ToChain(channel.Pair.Zone) with { Peq = null },
                    SampleRate,
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
        // The wizard filters THEN gates, from the original measurement, exactly as the panel does.
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
                SampleRate,
                request.Source.GateSettings!,
                Calibration: null,
                SmoothingInverseOctaves: 0));

        AnalysisCurve panel = DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                VirtualCrossoverAnalysis.ApplyChain(
                    channel.TransferImpulseResponse!,
                    channel.Settings.ToChain(channel.Pair.Zone) with { Peq = bank },
                    SampleRate,
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
        // A window does not commute with a filter (Q 5 at 100 Hz under 6 ms reads dB apart), which justifies the full-render preview.
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
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, renderAnchorIndex: 480);

        IReadOnlyList<SignalPoint> bare = EqWizardGatedPreview.Render(
            Preview(request, bank: null));
        AnalysisCurve panel = DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                VirtualCrossoverAnalysis.ApplyChain(
                    channel.TransferImpulseResponse!,
                    channel.Settings.ToChain(channel.Pair.Zone) with { Peq = null },
                    SampleRate,
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

        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest off = Build(channel, withChain: true);
        Assert.Null(off.AutoTuneMinHz);
        Assert.Null(off.AutoTuneMaxHz);

        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        VirtualDspEqHandoffRequest raw = Build(channel, withChain: false);
        Assert.Null(raw.AutoTuneMinHz);
        Assert.Null(raw.AutoTuneMaxHz);

        // A designed FIR high-pass beside an IIR low-pass: both stages filter, so both set an edge of the window.
        // Read from the IIR alone the window would start at 20 Hz and send the fit down the FIR's whole stopband.
        var design = new FirCrossoverDesign(
            CrossoverKind.HighPass,
            channel.Settings.HighPassEdge,
            channel.Settings.HighPassEdge,
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            255,
            SampleRate);
        channel.Settings.CrossoverKind = CrossoverKind.LowPass;
        channel.Settings.Fir = design.Build();
        channel.Settings.FirDesign = design;

        VirtualDspEqHandoffRequest both = Build(channel, withChain: true);
        Assert.Equal(80, both.AutoTuneMinHz);
        Assert.Equal(500, both.AutoTuneMaxHz);
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
        Assert.Equal(-41, request.TargetLevelDb);
        Assert.Equal(EqWizardSourceKind.VirtualDspChannel, request.Source.Kind);
        // The curve travels, not an id: a session-carried curve is not in the wizard's list.
        Assert.Same(calibration, request.Source.PinnedCalibration);
        Assert.Equal("mic-1", request.Source.PinnedCalibrationName);
        Assert.Same(calibration, request.Token.Calibration);
        Assert.Equal(SampleRate, request.Source.SampleRateHz);
        Assert.False(request.Source.SupportsCalibration);
        Assert.True(request.Source.SupportsSmoothing);
        Assert.Same(channel, request.Token.Channel);
        Assert.False(request.Token.RightSide);
    }

    [Fact]
    public void AMonoHandoffAddressesTheLeftSet_EvenFromTheRightView()
    {
        // Mono token says LEFT outright, so un-mono'ing mid-edit still returns to the set the tune came from.
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
        // 5 bins pair with an 8-sample FFT: k = 1..4 at k * rate / 8.
        channel.TransferCoherence = [1.0, 0.95, 0.9, 0.8, 0.7];

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        Assert.NotNull(request.Source.Coherence);
        Assert.Equal(4, request.Source.Coherence!.Count);
        Assert.Equal(SampleRate / 8.0, request.Source.Coherence[0].X, 6);
        Assert.Equal(0.95, request.Source.Coherence[0].Y);
    }

    [Fact]
    public void ReturnLandsOnTheSideTheTokenNames_NotTheActiveOne()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: true);
        channel.ActiveRight = false;
        var curve = new EqualizationCurve(
            new[] { new PeqBand(250, 3, -6) }, preampDb: -1.5);

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));

        VirtualCrossoverChannelSettings right = channel.Pair.SideFor(rightSide: true);
        Assert.Equal(curve.Bands, right.PeqBands);
        Assert.Equal(-1.5, right.PeqPreampDb);
        Assert.Equal("EQ Wizard", right.PeqSourceName);
        Assert.Empty(channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void AHandoffTakenFromAMonoPair_LandsOnItsSurvivingSet()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Pair.Mono = true;
        channel.ActiveRight = true;
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(request.Token.RightSide);
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void ReturnAfterThePairChangedRouting_Refuses()
    {
        // Pair turned mono after the handoff: SideFor(true) now resolves to LEFT, so delivery must refuse.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: true);
        channel.Pair.Mono = true;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Pair.SideFor(rightSide: false).PeqBands);
    }

    [Fact]
    public void ReturnAfterTheSideGotANewMeasurement_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.SideState(rightSide: false).BeginSourceLoad();
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheCalibrationChanged_Refuses()
    {
        // The wizard locks calibration during a handoff; the panel's selector is a tab away, so the return must enforce it too.
        VirtualCrossoverChannel channel = BuildChannel();
        CalibrationFile fitted = CalibrationFile.Parse("20 0\n20000 1.5\n");
        VirtualDspEqReturnToken token =
            TokenFor(channel, rightSide: false) with { Calibration = fitted };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1,
            CalibrationFile.Parse("20 0\n20000 -1.5\n"), SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        // Curves compare by content, not reference or name.
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1,
            CalibrationFile.Parse("20 0\n20000 1.5\n"), SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterThePanelsPeqWasReplacedOrCleared_Refuses()
    {
        // The chain check excludes the PEQ stage itself; the panel can still Load/Clear the bank meanwhile.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = new List<PeqBand> { new(120, 2, -4) };
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var newer = new List<PeqBand> { new(3_000, 4, 2) };
        channel.Settings.PeqBands = newer;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(newer, channel.Settings.PeqBands);

        VirtualDspEqReturnToken second = TokenFor(channel, rightSide: false);
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, second, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheGateMoved_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate with { PlateauMs = 400 }, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterThePanelsTargetLevelMoved_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, targetLevelDb: TargetLevel + 5, spatialAverage: null,
            SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheGainChanged_Refuses()
    {
        // Gain does not bend the curve, but the preamp was fitted against the absolute target level.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.GainDb -= 6;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheCrossoverChanged_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.LowPassEdge =
            channel.Settings.LowPassEdge with { FrequencyHz = 900 };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        VirtualDspEqReturnToken fresh = TokenFor(channel, rightSide: false);
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, fresh, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterADelayOrAnAllPassBandEdit_Refuses()
    {
        // Not free: at 192 kHz (171 ms window) a delay edit moves the gated shape up to 1.70 dB, an all-pass up to 4.77 dB. See SteadyStateWindowTests.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken delayToken = TokenFor(channel, rightSide: false);
        channel.Settings.DelayMs += 4.2;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, delayToken, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));

        channel.Settings.PeqBands =
            [new PeqBand(40, 2, 0, PeqBandType.AllPassSecondOrder)];
        VirtualDspEqReturnToken apToken = TokenFor(channel, rightSide: false);
        channel.Settings.PeqBands =
            [new PeqBand(40, 20, 0, PeqBandType.AllPassSecondOrder)];
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, apToken, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(20, Assert.Single(channel.Settings.PeqBands).Q);
    }

    [Fact]
    public void ReturnAfterAPolarityFlip_IsAllowed()
    {
        // Polarity is -1 at every frequency: measured exactly 0 dB change at every rate.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        channel.Settings.InvertPolarity = !channel.Settings.InvertPolarity;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void AChainSessionWithNoCrossoverStillNoticesOneBeingTurnedOn()
    {
        // ToChain yields CrossoverSpec.Off (a value); only DspChannelChain.Identity carries null.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        Assert.Equal(CrossoverSpec.Off, request.Token.PreviewChain.Crossover);
        Assert.True(request.Token.WithChain);

        channel.Settings.CrossoverKind = CrossoverKind.HighPass;
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ChangingHowThePhaseViewReads_DoesNotRefuseTheReturn()
    {
        // Magnitude forces Fixed and ignores FDW cycles, detrend and unwrap, so those edits must not refuse.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate with
            {
                FdwCycles = 8,
                DetrendMode = PhaseDetrendMode.Manual,
                ManualDetrendMilliseconds = 3,
                Unwrap = true
            },
            null,
            TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ChangingTheMagnitudeWindowItself_StillRefuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate with { PlateauMs = 400 }, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void TheRequestCarriesTheLevelRangeThePanelCanHold()
    {
        VirtualCrossoverChannel channel = BuildChannel();

        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);

        Assert.Equal(-120, request.TargetLevelMinDb);
        Assert.Equal(60, request.TargetLevelMaxDb);
    }

    [Fact]
    public void ARawSessionIsImmuneToTheProcessedGatePin()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void AChainSessionIsNotImmuneToThatPin()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: true);
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, pinnedGateOffsetMs: 12.5, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

    [Fact]
    public void ARawSessionIsImmuneToCrossoverEdits()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqHandoffRequest request = Build(channel, withChain: false);
        channel.Settings.LowPassEdge =
            channel.Settings.LowPassEdge with { FrequencyHz = 900 };
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(request.Token.WithChain);
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, request.Token, curve,
            projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheProjectWasReplaced_RefusesEvenThoughTheChannelSurvives()
    {
        // Binding a project reuses channel objects when counts match, so only the generation tells sessions apart.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token =
            TokenFor(channel, rightSide: false) with { ProjectGeneration = 4 };
        channel.Pair = new VirtualCrossoverChannelPairSettings();
        var curve = new EqualizationCurve(new[] { new PeqBand(250, 3, -6) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 5, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        VirtualCrossoverChannel untouched = BuildChannel();
        VirtualDspEqReturnToken control =
            TokenFor(untouched, rightSide: false) with { ProjectGeneration = 4 };
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { untouched }, control, curve, projectGeneration: 5, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { untouched }, control, curve, projectGeneration: 4, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
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
            new[] { survivor }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off, GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));

        Assert.Empty(removed.Settings.PeqBands);
        Assert.Empty(survivor.Settings.PeqBands);
    }

    [Fact]
    public void ReturnAfterTheProjectChangedProcessors_Refuses()
    {
        // The bank was fitted at the handed-off rate; the same numbers at another rate are different filters.
        VirtualCrossoverChannel channel = BuildChannel();
        VirtualDspEqReturnToken token = TokenFor(channel, rightSide: false);
        var curve = new EqualizationCurve(new[] { new PeqBand(8_000, 4, -5) });

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, TargetLevel, spatialAverage: null, 96_000));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void AHandoffRecordsTheProcessorItWasTakenUnder()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        DspProcessorProfile processor =
            DspProcessorCatalog.Preset("helix-dsp-ultra-s")!.ToProfile();

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, processorProfile: processor);

        Assert.Equal(96_000, request.Token.ProcessorSampleRateHz);
    }

    private static VirtualDspEqReturnToken TokenFor(
        VirtualCrossoverChannel channel, bool rightSide) =>
        new(
            channel,
            rightSide && !channel.Pair.Mono,
            ProjectGeneration: 1,
            channel.SideState(rightSide).SourceRevision,
            channel.Pair.Mono,
            channel.Pair.ToChain(rightSide) with { Peq = null },
            WithChain: true,
            new PeqBankState(
                channel.SideSettings(rightSide).PeqBands,
                channel.SideSettings(rightSide).PeqPreampDb),
            TargetLevel,
            GateTemplate,
            PinnedGateOffsetMs: null,
            Calibration: null,
            SpatialAverage: null,
            SpatialAverageCalibration: SpatialAverageCalibration.Off,
            ProcessorSampleRateHz: SampleRate);

    private static EqWizardGatedPreviewRequest Preview(
        VirtualDspEqHandoffRequest request, EqualizationCurve? bank) =>
        new(
            request.Source.PreviewImpulseResponse!,
            request.Source.PreviewChain!,
            bank,
            request.Source.Measurement!.PeakIndex,
            SampleRate,
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
        SpatialAverageCalibration? spatialAverageCalibration = null,
        double spatialAverageOffsetDb = 0,
        DspProcessorProfile? processorProfile = null) =>
        VirtualDspEqHandoff.Build(
            channel,
            channel.ActiveRight,
            withChain,
            processorProfile ?? DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj),
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
            spatialAverageCalibration
                ?? SpatialAverageCalibration.Specific(calibration),
            projectGeneration,
            spatialAverage,
            spatialAverageOffsetDb);

    /// <summary>On the hybrid view the capture replaces the magnitude; the impulse response stays for phase.</summary>
    [Fact]
    public void WithASpatialAverage_TheCaptureTravelsAndTheMagnitudeStopsBeingGated()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, spatialAverage: capture, spatialAverageOffsetDb: -73.5);

        Assert.Same(capture, request.Source.SpatialAverage);
        Assert.Equal(-73.5, request.Source.SpatialAverageOffsetDb);
        Assert.False(request.Source.IsGated);
        Assert.NotNull(request.Source.Measurement);
        Assert.NotNull(request.Source.PreviewImpulseResponse);
        Assert.NotNull(request.Source.GateSettings);
        Assert.Contains("MMM", request.Source.DisplayName);
    }

    [Fact]
    public void WithAnArrayAverage_TheReceiptNamesTheArrayRatherThanMmm()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument array = Capture();
        array.Title = "Array of 7 microphones";
        array.Method = SpatialAverageMethod.MicArray;

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, spatialAverage: array, spatialAverageOffsetDb: -73.5);

        Assert.Contains("Array", request.Source.DisplayName);
        Assert.DoesNotContain("MMM", request.Source.DisplayName);
    }

    [Fact]
    public void WithoutASpatialAverage_TheMagnitudeIsStillGated()
    {
        VirtualDspEqHandoffRequest request = Build(BuildChannel(), withChain: true);

        Assert.Null(request.Source.SpatialAverage);
        Assert.True(request.Source.IsGated);
    }

    [Fact]
    public void TheWizardsSourceCurveComesFromTheCapture()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.GainDb = 0;
        channel.Settings.CrossoverKind = CrossoverKind.Off;
        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, spatialAverage: Capture(), spatialAverageOffsetDb: 0);

        var session = new EqWizardSession();
        session.BeginHandoff(request);

        EqWizardCurve? curve = session.SourceCurve;
        Assert.NotNull(curve);
        IReadOnlyList<DataPoint> points = curve.Points;
        Assert.NotEmpty(points);
        Assert.All(
            points.Where(point => point.X is > 100 and < 10_000),
            point => Assert.Equal(-20, point.Y, 3));
    }

    [Fact]
    public void ReturnAfterTheHybridWasTurnedOff_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();
        VirtualDspEqReturnToken token = Build(
            channel, withChain: true, spatialAverage: capture).Token;
        var curve = new EqualizationCurve([new PeqBand(120, 1.4, -3)], -1.5);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, TargetLevel, spatialAverage: null, SampleRate));

        // A re-attached file is a different capture even with equal numbers.
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, TargetLevel, spatialAverage: Capture(), SampleRate));

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel }, token, curve, projectGeneration: 1, calibration: null, SpatialAverageCalibration.Off,
            GateTemplate, null, TargetLevel, spatialAverage: capture, SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    /// <remarks>Off/Own/Specific give three magnitudes from one capture, yet two leave the panel's correction identical.</remarks>
    [Fact]
    public void ReturnAfterTheCaptureWasReadAnotherWay_Refuses()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();
        CalibrationFile responsesOwn = CalibrationFile.Parse("20 -2\n20000 -2\n");
        VirtualDspEqReturnToken token = Build(
            channel,
            withChain: true,
            spatialAverage: capture,
            spatialAverageCalibration: SpatialAverageCalibration.Own).Token;
        var curve = new EqualizationCurve([new PeqBand(120, 1.4, -3)], -1.5);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel },
            token,
            curve,
            projectGeneration: 1,
            calibration: null,
            SpatialAverageCalibration.Specific(responsesOwn),
            GateTemplate,
            null,
            TargetLevel,
            spatialAverage: capture,
            SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel },
            token,
            curve,
            projectGeneration: 1,
            calibration: null,
            SpatialAverageCalibration.Off,
            GateTemplate,
            null,
            TargetLevel,
            spatialAverage: capture,
            SampleRate));
        Assert.Empty(channel.Settings.PeqBands);

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel },
            token,
            curve,
            projectGeneration: 1,
            calibration: null,
            SpatialAverageCalibration.Own,
            GateTemplate,
            null,
            TargetLevel,
            spatialAverage: capture,
            SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);
    }

    [Fact]
    public void ReturnUnderTheSameNamedCalibrationReadAgain_Lands()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        LiveCaptureDocument capture = Capture();
        const string points = "20 1.5\n1000 0.5\n20000 -3\n";
        VirtualDspEqReturnToken token = Build(
            channel,
            withChain: true,
            spatialAverage: capture,
            spatialAverageCalibration:
                SpatialAverageCalibration.Specific(CalibrationFile.Parse(points))).Token;
        var curve = new EqualizationCurve([new PeqBand(120, 1.4, -3)], -1.5);

        Assert.True(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel },
            token,
            curve,
            projectGeneration: 1,
            calibration: null,
            SpatialAverageCalibration.Specific(CalibrationFile.Parse(points)),
            GateTemplate,
            null,
            TargetLevel,
            spatialAverage: capture,
            SampleRate));
        Assert.Equal(curve.Bands, channel.Settings.PeqBands);

        channel.Settings.PeqBands = [];
        Assert.False(VirtualDspEqHandoff.TryApplyReturn(
            new[] { channel },
            token,
            curve,
            projectGeneration: 1,
            calibration: null,
            SpatialAverageCalibration.Specific(
                CalibrationFile.Parse("20 0\n20000 -6\n")),
            GateTemplate,
            null,
            TargetLevel,
            spatialAverage: capture,
            SampleRate));
        Assert.Empty(channel.Settings.PeqBands);
    }

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

    [Fact]
    public void AHandoff_CarriesTheProjectsProcessorAndRealizesTheChainAtItsRate()
    {
        // The processor rate reaches the fit; the measurement keeps its own rate for gates.
        VirtualCrossoverChannel channel = BuildChannel();
        DspProcessorProfile processor =
            DspProcessorCatalog.Preset("helix-dsp-ultra-s")!.ToProfile();

        VirtualDspEqHandoffRequest request = Build(
            channel, withChain: true, processorProfile: processor);

        Assert.Equal(processor, request.Source.ProcessorProfile);
        Assert.Equal(96_000, request.Source.ProcessorProfile!.SampleRateHz);
        Assert.Equal(SampleRate, request.Source.SampleRateHz);
        Assert.Equal(SampleRate, request.Source.Measurement!.SampleRate);

        Complex[] expected = VirtualCrossoverAnalysis.ApplyChain(
            channel.SideState(channel.ActiveRight).ProcessingSource!.CroppedImpulseResponse,
            request.Source.PreviewChain!,
            SampleRate,
            96_000);
        Complex[] actual = request.Source.Measurement.ImpulseResponse!;
        Assert.Equal(
            expected.Take(2_048).Select(value => value.Real),
            actual.Take(2_048).Select(value => value.Real));
    }

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
