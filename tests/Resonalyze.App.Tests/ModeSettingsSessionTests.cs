using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class ModeSettingsSessionTests
{
    [Theory]
    [InlineData(256, 16, 256, 16, 240)]
    [InlineData(300, 256, 256, 256, 44)]
    [InlineData(100, 256, 256, 100, 0)]
    [InlineData(4096, -5, 99, 0, 99)]
    public void TheFadesShareTheWindowLeftFirst(int window, int left, int right, int containedLeft, int containedRight) =>
        Assert.Equal((containedLeft, containedRight), TukeyFades.Contain(left, right, window));

    [Fact]
    public void TheFadesComeBackWhenTheWindowGrowsBack_AndTheFieldsTakeWhatTheOtherLeaves()
    {
        var fades = new TukeyFades();
        fades.Load(4096, 3900, 100);
        Assert.Equal((3900, 100, 3996, 196), (fades.Left, fades.Right, fades.LeftMaximum, fades.RightMaximum));

        fades.SetWindow(1000);
        Assert.Equal((1000, 0), (fades.Left, fades.Right));
        fades.SetLeft(600);
        Assert.Equal((600, 100), (fades.Left, fades.Right));
        fades.SetWindow(4096);
        Assert.Equal((600, 100), (fades.Left, fades.Right));
        fades.SetRight(3000);
        fades.SetWindow(2000);
        Assert.Equal((600, 1400), (fades.Left, fades.Right));
        fades.SetWindow(8192);
        Assert.Equal((600, 3000), (fades.Left, fades.Right));
    }

    [Fact]
    public void AStoredCycleCountOffTheListReadsAsTheDefault_AndAnyModeButFixedAsFdw()
    {
        Assert.Equal([4, 6, 8], WindowModeChoice.CycleChoices);
        Assert.Equal(PhaseAnalysisSettings.DefaultFdwCycles, WindowModeChoice.ValidCycles(5));
        Assert.Equal(8, WindowModeChoice.ValidCycles(8));
        WindowModeChoice odd = WindowModeChoice.From((PhaseWindowMode)7, 4);
        Assert.Equal((PhaseWindowMode.FrequencyDependent, 1, true), (odd.Mode, odd.ModeIndex, odd.CyclesEditable));
        Assert.Equal(PhaseWindowMode.Fixed, WindowModeChoice.ModeAt(0));
        Assert.Equal(PhaseWindowMode.FrequencyDependent, WindowModeChoice.ModeAt(-1));
        Assert.False(WindowModeChoice.From(PhaseWindowMode.Fixed, 6).CyclesEditable);
    }

    [Fact]
    public void TheSmoothingListsAndTheirReadings()
    {
        Assert.Equal(SmoothingPresetOptions.SupportedInverseOctaves, SmoothingPresetOptions.Offered(false));
        IReadOnlyList<int> withPsycho = SmoothingPresetOptions.Offered(true);
        Assert.Equal(SpectrumSmoothing.PsychoacousticCode, withPsycho[withPsycho.ToList().IndexOf(6) + 1]);
        Assert.Equal(0, SmoothingPresetOptions.ReadBack(null));
        Assert.Equal(12, SmoothingPresetOptions.ReadBack(12));
        Assert.Equal("Psycho", SmoothingPresetOptions.GetLabel(SpectrumSmoothing.PsychoacousticCode));
        Assert.Equal(6, SmoothingPresetOptions.Normalize(SpectrumSmoothing.PsychoacousticCode, includePsychoacoustic: false));
    }

    [Fact]
    public void TheGateSaysWhatItResolves()
    {
        Assert.Equal("Reliable from ≈ 167+ Hz", GateReadout.ReliableFrom(0.5, 4.0, 1.5));
        Assert.Equal("Reliable from ≈ — Hz", GateReadout.ReliableFrom(0, 0, 0));
    }

    [Fact]
    public void TheGateHoldsWhatItsFieldsShow_AndAutoSitsOnTheIrStart()
    {
        var gate = new GateFields();
        gate.Load(auto: false, offsetMs: 1234.56789, leftMs: 900, plateauMs: 1.23456, rightMs: 0.004);
        Assert.Equal((1234.568m, 680m, 1.23m, 0m, true), (gate.OffsetMs, gate.LeftMs, gate.PlateauMs, gate.RightMs, gate.OffsetEditable));

        var measurement = new ModeSettingsMeasurement(ModeSettingsWiringTests.Transfer(48_000, peak: 480), 48_000);
        gate.Snap(measurement);
        Assert.Equal(1234.568m, gate.OffsetMs);
        gate.Auto = true;
        gate.Snap(new ModeSettingsMeasurement(null, 48_000));
        Assert.Equal(1234.568m, gate.OffsetMs);
        gate.Snap(measurement);
        Assert.Equal(ModeSettingsLimits.GateOffsetMs.Clamp(measurement.TransferStartMs()!.Value), gate.OffsetMs);
        Assert.False(gate.OffsetEditable);
    }

    [Fact]
    public void EachGatedPanelWritesItsOwnFields()
    {
        var stored = new FrequencyResponseOptions
        {
            PhaseGateAutoFit = false,
            PhaseLeftMs = 1.0,
            PhaseDetrendMs = 7.77777,
            PhaseDetrendMode = PhaseDetrendMode.Manual,
            PhaseWindowMode = PhaseWindowMode.Fixed,
            Unwrap = false,
            GroupDelayLeftMs = 2.0,
            GroupDelayFdwCycles = 8,
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode
        };
        GatedAnalysisSettingsSession phase = GatedAnalysisSettingsSession.ForPhase();
        GatedAnalysisSettingsSession groupDelay = GatedAnalysisSettingsSession.ForGroupDelay();
        phase.Load(stored, new CurveVisibilityOptions { ShowExcessPhase = false });
        groupDelay.Load(stored, new CurveVisibilityOptions { ShowExcessGroupDelay = false });

        var phaseWritten = new FrequencyResponseOptions { GroupDelayLeftMs = 9 };
        var phaseCurves = new CurveVisibilityOptions { ShowExcessGroupDelay = false };
        phase.WriteTo(phaseWritten, phaseCurves);
        var groupDelayWritten = new FrequencyResponseOptions { PhaseLeftMs = 9, Unwrap = false };
        var groupDelayCurves = new CurveVisibilityOptions { ShowExcessPhase = false };
        groupDelay.WriteTo(groupDelayWritten, groupDelayCurves);

        Assert.Equal(
            (false, 1.0, 7.77777, PhaseDetrendMode.Manual, PhaseWindowMode.Fixed, false, 9.0, 6.0),
            (phaseWritten.PhaseGateAutoFit, phaseWritten.PhaseLeftMs, phaseWritten.PhaseDetrendMs,
                phaseWritten.PhaseDetrendMode, phaseWritten.PhaseWindowMode, phaseWritten.Unwrap,
                phaseWritten.GroupDelayLeftMs, phaseWritten.SmoothingInverseOctaves));
        Assert.Equal((false, false), (phaseCurves.ShowExcessPhase, phaseCurves.ShowExcessGroupDelay));
        Assert.Equal(
            (2.0, 8, 9.0, false),
            (groupDelayWritten.GroupDelayLeftMs, groupDelayWritten.GroupDelayFdwCycles, groupDelayWritten.PhaseLeftMs,
                groupDelayWritten.Unwrap));
        Assert.Equal((false, false), (groupDelayCurves.ShowExcessGroupDelay, groupDelayCurves.ShowExcessPhase));
    }

    [Fact]
    public void TheResetButtonsRestoreTheOptionsDefaults()
    {
        var defaults = new FrequencyResponseOptions();
        GatedAnalysisDefaults groupDelay = GatedAnalysisSettingsSession.ForGroupDelay().Defaults;
        GatedAnalysisDefaults phase = GatedAnalysisSettingsSession.ForPhase().Defaults;

        Assert.Equal(
            (defaults.GroupDelayWindowMode, defaults.GroupDelayFdwCycles, 0.5m, 10m, 3m, (decimal?)null, 12),
            (groupDelay.WindowMode!.Value.Mode, groupDelay.WindowMode!.Value.Cycles, groupDelay.LeftMs,
                groupDelay.PlateauMs, groupDelay.RightMs, groupDelay.DetrendMs, groupDelay.SmoothingInverseOctaves));
        Assert.Equal(
            ((WindowModeChoice?)null, 0.5m, 4m, 1.5m, (decimal?)0m, 12),
            (phase.WindowMode, phase.LeftMs, phase.PlateauMs, phase.RightMs, phase.DetrendMs, phase.SmoothingInverseOctaves));
    }

    [Fact]
    public void TauShowsByMode_AndOnlyManualKeepsTheUsersValue()
    {
        var tau = new PhaseDetrendState();
        tau.Load(PhaseDetrendMode.Auto, 2500.0);
        tau.AutoMs = 1.23456;
        Assert.Equal((1.235m, false), (tau.ShownMs, tau.IsManual));
        tau.Type(9m);
        Assert.Equal(2500.0, tau.ManualMs);

        tau.ModeIndex = (int)PhaseDetrendMode.Manual;
        Assert.Equal(2000m, tau.ShownMs);
        tau.Type(-3.5m);
        tau.ModeIndex = (int)PhaseDetrendMode.Off;
        Assert.Equal((0m, -3.5), (tau.ShownMs, tau.ManualMs));
        tau.TakeEstimate(1.00049);
        Assert.Equal(1.0, tau.ManualMs);

        tau.ModeIndex = (int)PhaseDetrendMode.Auto;
        tau.AutoMs = null;
        Assert.Equal(0m, tau.ShownMs);
        tau.ModeIndex = 7;
        Assert.Equal(PhaseDetrendMode.Auto, tau.Mode);
    }

    [Fact]
    public void ABusyDocumentIsNotRead_ForTheAutoValueNorTheButtons()
    {
        using var analyzer = new TestAnalyzer();
        analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
        GatedAnalysisSettingsSession session = GatedAnalysisSettingsSession.ForPhase();
        session.Load(new FrequencyResponseOptions { PhaseGateAutoFit = false, PhaseGateOffsetMs = 10 }, new CurveVisibilityOptions());
        var estimate = new PhaseDetrendEstimate();

        Assert.True(estimate.TryResolveAuto(analyzer.Document, session.DetrendReading(), out double? auto));
        Assert.NotNull(auto);
        Assert.NotNull(PhaseDetrendEstimate.Estimate(analyzer.Document, session.DetrendReading()));
        using (analyzer.Document.TryAcquire())
        {
            Assert.False(estimate.TryResolveAuto(analyzer.Document, session.DetrendReading(), out _));
            Assert.Null(PhaseDetrendEstimate.Estimate(analyzer.Document, session.DetrendReading()));
        }

        analyzer.Document.Clear();
        Assert.True(estimate.TryResolveAuto(analyzer.Document, session.DetrendReading(), out double? none));
        Assert.Null(none);
    }

    [Fact]
    public void AWaterfallStepNeverRestsOnZero_AndTheReadOutsFollowTheRate()
    {
        WaterfallSettingsSession waterfall = WaterfallSettingsSession.ForWaterfall();
        waterfall.Follow(new ModeSettingsMeasurement(null, 48_000));
        waterfall.Load(new WaterfallGenerateOptions { Step = 0, SliceCount = 2, Window = 99_999 });
        Assert.Equal((1, 4, 32768, 48_000m, 0.08m), (waterfall.Step, waterfall.SliceCount, waterfall.Fades.Window,
            waterfall.SampleRateShown, waterfall.CaptureTimeMs));

        waterfall.SetStep(0);
        Assert.Equal(-1, waterfall.Step);
        waterfall.SetStep(0);
        Assert.Equal(1, waterfall.Step);
        waterfall.SetSliceCount(96);
        waterfall.Follow(new ModeSettingsMeasurement(null, 384_000));
        Assert.Equal((384_000m, 0.25m), (waterfall.SampleRateShown, waterfall.CaptureTimeMs));

        var written = new WaterfallGenerateOptions { Periods = 5 };
        waterfall.WriteTo(written);
        Assert.Equal((96, 1, 5.0), (written.SliceCount, written.Step, written.Periods));
    }

    [Fact]
    public void BurstDecayTimesItsWindow_AndWritesItsPeriods()
    {
        WaterfallSettingsSession burst = WaterfallSettingsSession.ForBurstDecay();
        burst.Follow(new ModeSettingsMeasurement(null, 96_000));
        burst.Load(new WaterfallGenerateOptions
        {
            Window = 9600,
            Periods = 30.7,
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode,
            SliceCount = 7
        });
        Assert.Equal((100m, 30, 6), (burst.CaptureTimeMs, burst.Periods, burst.SmoothingInverseOctaves));
        burst.SetWindow(4800);
        Assert.Equal(50m, burst.CaptureTimeMs);

        var written = new WaterfallGenerateOptions { SliceCount = 11, Step = 3 };
        burst.WriteTo(written);
        Assert.Equal((4800, 30.0, 11, 3), (written.Window, written.Periods, written.SliceCount, written.Step));
    }

    [Fact]
    public void TheBandCentresARateRealizes_AndWhereAStoredValueLands()
    {
        Assert.DoesNotContain(20_000.0, ImpulseBandCentres.For(ImpulseBandCentres.ThirdOctaveBand, 44_100));
        Assert.Contains(20_000.0, ImpulseBandCentres.For(ImpulseBandCentres.ThirdOctaveBand, 48_000));
        Assert.Equal(16_000.0, ImpulseBandCentres.For(ImpulseBandCentres.OctaveBand, 48_000)[^1]);
        Assert.Equal(ImpulseBandCentres.For(0.0, 44_100), ImpulseBandCentres.For(ImpulseBandCentres.OctaveBand, 0));
        Assert.Equal(ImpulseBandCentres.ThirdOctaveBand, ImpulseBandCentres.NearestWidth(0.5));
        Assert.Equal(0.0, ImpulseBandCentres.NearestWidth(-1));
        Assert.Equal(500.0, ImpulseBandCentres.Nearest([125.0, 500.0], 300.0));
        Assert.Equal(("1/3 octave", "Off", "63 Hz", "16 kHz"), (ImpulseBandCentres.WidthLabel(1.0 / 3.0),
            ImpulseBandCentres.WidthLabel(0), ImpulseBandCentres.CentreLabel(63), ImpulseBandCentres.CentreLabel(16_000)));
    }

    [Fact]
    public void TheImpulseViewKeepsItsCentreWhileTheBandIsOff_AndEachPanelWritesItsOwnFields()
    {
        var session = new ImpulseViewSettingsSession();
        session.Load(new ImpulseResponseOptions { BandFilterOctaves = 0, BandCenterHz = 700, Length = 99_999, ShowAutocorrelation = false }, 48_000);
        Assert.Equal((false, 500.0, 32768), (session.BandActive, session.CentreHz, session.Length));

        session.SetBandOctaves(ImpulseBandCentres.ThirdOctaveBand);
        Assert.Equal(500.0, session.CentreHz);
        session.SetBandOctaves(ImpulseBandCentres.OctaveBand);
        session.CentreIndex = session.Centres.ToList().IndexOf(16_000);
        session.Follow(48_000);
        Assert.Equal(16_000.0, session.CentreHz);
        session.Follow(44_100);
        Assert.Equal(8_000.0, session.CentreHz);
        session.SetBandOctaves(ImpulseBandCentres.ThirdOctaveBand);
        Assert.Equal(8_000.0, session.CentreHz);

        var written = new ImpulseResponseOptions { ShowAutocorrelation = true };
        session.WriteTo(written);
        Assert.Equal((ImpulseBandCentres.ThirdOctaveBand, 8_000.0, true), (written.BandFilterOctaves, written.BandCenterHz, written.ShowAutocorrelation));
        session.WriteAutocorrelation(written);
        Assert.False(written.ShowAutocorrelation);
    }

    [Fact]
    public void AStoredViewTheListsDoNotHoldIsWrittenAsTheirFirst()
    {
        var session = new ImpulseViewSettingsSession();
        session.Load(new ImpulseResponseOptions
        {
            AmplitudeScale = (ImpulseAmplitudeScale)9,
            TimeUnit = (ImpulseTimeUnit)9,
            TimeOrigin = ImpulseTimeOrigin.Peak
        }, 48_000);
        Assert.Null(session.AmplitudeScale);

        var written = new ImpulseResponseOptions { AmplitudeScale = ImpulseAmplitudeScale.Decibels, TimeUnit = ImpulseTimeUnit.Samples };
        session.WriteTo(written);

        Assert.Equal(
            (ImpulseAmplitudeScale.Linear, ImpulseTimeUnit.Milliseconds, ImpulseTimeOrigin.Peak),
            (written.AmplitudeScale, written.TimeUnit, written.TimeOrigin));
    }

    [Fact]
    public void AWaterfallDrawsFromEightSlices()
    {
        Assert.Null(WaterfallSliceVerdict.Explain(WaterfallMode.Fourier, 8));
        Assert.Contains("Raise Slices", WaterfallSliceVerdict.Explain(WaterfallMode.Fourier, 4), StringComparison.Ordinal);
        Assert.Contains("Lengthen the window", WaterfallSliceVerdict.Explain(WaterfallMode.BurstDecay, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void WithNothingOpenThePanelsReadTheConfiguredRate()
    {
        Assert.Equal(96_000, new ModeSettingsMeasurement(null, 96_000).SampleRate);
        var open = new ModeSettingsMeasurement(ModeSettingsWiringTests.Transfer(48_000, peak: 480), 96_000);
        Assert.Equal((48_000, false), (open.SampleRate, open.SplAvailable));
        Assert.Null(new ModeSettingsMeasurement(null, 48_000).TransferStartMs());
    }
}
