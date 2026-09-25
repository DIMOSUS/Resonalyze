using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>Every mode's view options, one owner: what a history entry and the settings file keep of them.</summary>
public sealed class AnalyzerViewSettingsTests
{
    // The defaults the main window gave each mode before the options had an owner.
    [Fact]
    public void EachModeStartsWithItsOwnDefaults()
    {
        var view = new AnalyzerViewSettings();

        Assert.Equal(
            FrequencyResponseOptions.DefaultPhaseSmoothingInverseOctaves,
            view.PhaseResponse.SmoothingInverseOctaves);
        Assert.Equal(
            FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves,
            view.GroupDelay.SmoothingInverseOctaves);
        Assert.Equal(WaterfallMode.Fourier, view.Waterfall.WaterfallMode);
        Assert.Equal(WaterfallMode.BurstDecay, view.BurstDecay.WaterfallMode);
        Assert.Equal(1024, view.BurstDecay.Window);
        Assert.Equal(128, view.BurstDecay.RightTukeyWindow);
    }

    [Fact]
    public void AHistoryEntryBringsBackEveryModesView()
    {
        var left = new AnalyzerViewSettings();
        left.FrequencyResponse.SmoothingInverseOctaves = 3;
        left.PhaseResponseVisibility.ShowExcessPhase = false;
        left.GroupDelay.SmoothingInverseOctaves = 12;
        left.ImpulseResponse.TimeUnit = ImpulseTimeUnit.Samples;
        left.Waterfall.SliceCount = 40;
        left.BurstDecay.Periods = 12;
        left.LiveSpectrum.AnalysisMode = LiveAnalysisMode.Rta;
        left.TimeAlignment.BandMode = TimeAlignmentBandMode.FullBand;

        MeasurementSessionSnapshot session = left.CaptureSession(ModeTab.GroupDelay, [2, 5]);
        var back = new AnalyzerViewSettings();
        back.ApplySession(session, sampleRate: 48_000);

        Assert.Equal(ModeTab.GroupDelay, session.ActiveMode);
        Assert.Equal([2, 5], session.ActiveOverlaySlots);
        Assert.Equal(3, back.FrequencyResponse.SmoothingInverseOctaves);
        Assert.False(back.PhaseResponseVisibility.ShowExcessPhase);
        Assert.Equal(12, back.GroupDelay.SmoothingInverseOctaves);
        Assert.Equal(ImpulseTimeUnit.Samples, back.ImpulseResponse.TimeUnit);
        Assert.Equal(40, back.Waterfall.SliceCount);
        Assert.Equal(12, back.BurstDecay.Periods);
        Assert.Equal(LiveAnalysisMode.Rta, back.LiveSpectrum.AnalysisMode);
        Assert.Equal(TimeAlignmentBandMode.FullBand, back.TimeAlignment.BandMode);
    }

    [Fact]
    public void AHistoryEntryOrANewSessionKeepsTheRigsLiveCalibration()
    {
        var left = new AnalyzerViewSettings();
        left.LiveSpectrum.CalibrationId = "old-rig";
        MeasurementSessionSnapshot session = left.CaptureSession(ModeTab.LiveSpectrum, []);
        var view = new AnalyzerViewSettings();
        view.LiveSpectrum.CalibrationId = "current-rig";

        view.ApplySession(session, sampleRate: 48_000);
        Assert.Equal("current-rig", view.LiveSpectrum.CalibrationId);

        view.ApplySession(new MeasurementSessionSnapshot(), sampleRate: 48_000);
        Assert.Equal("current-rig", view.LiveSpectrum.CalibrationId);
    }

    [Fact]
    public void TheSettingsFileKeepsTheViewAcrossARestart()
    {
        using var engine = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        var before = new AnalyzerViewSettings();
        before.FrequencyResponse.SmoothingInverseOctaves = 24;
        before.ImpulseResponse.Invert = true;
        before.BurstDecay.Periods = 18;
        var file = new MeasurementSettingsFile();

        file.CaptureFrom(engine, before);
        var after = new AnalyzerViewSettings();
        file.ApplyTo(engine, after);

        Assert.Equal(24, after.FrequencyResponse.SmoothingInverseOctaves);
        Assert.True(after.ImpulseResponse.Invert);
        Assert.Equal(18, after.BurstDecay.Periods);
        Assert.Equal(WaterfallMode.BurstDecay, after.BurstDecay.WaterfallMode);
    }
}
