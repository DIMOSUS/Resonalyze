using Resonalyze.Dsp;
using Resonalyze.Options;
using static Resonalyze.App.Tests.LiveSpectrumSettingsSessionTests;

namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumSettingsReaderTests
{
    [Fact]
    public void AStoredValue_FloorsOntoAnAscendingList_OrTakesItsFirstEntry()
    {
        Assert.Equal(0, LiveSpectrumSettingsChoices.Floor([0, 50, 75], -5));
        Assert.Equal(50, LiveSpectrumSettingsChoices.Floor([0, 50, 75], 74));
        Assert.Equal(75, LiveSpectrumSettingsChoices.Floor([0, 50, 75], 900));
        Assert.Equal(WindowType.FlatTop, LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Windows, WindowType.FlatTop));
        Assert.Equal(AveragingSpeed.Fast, LiveSpectrumSettingsChoices.Offered(LiveSpectrumSettingsChoices.Averagings, (AveragingSpeed)42));
    }

    [Fact]
    public void TheLabels_GiveASequenceItsDuration_AndZeroPercentIsOff()
    {
        Assert.Equal($"32768 — {683:0} ms", LiveSpectrumSettingsChoices.SequenceLengthLabel(32_768, 48_000));
        Assert.Equal("32768", LiveSpectrumSettingsChoices.SequenceLengthLabel(32_768, 0));
        Assert.Equal("Off", LiveSpectrumSettingsChoices.PercentLabel(0));
        Assert.Equal("25%", LiveSpectrumSettingsChoices.PercentLabel(25));
        Assert.Equal(
            ["Silent", "Pink noise (periodic)", "Pink noise", "Brown / red noise", "White noise"],
            LiveSpectrumSettingsChoices.Signals(referenceFree: true, mmm: false).Select(LiveSpectrumSettingsChoices.SignalLabel));
        Assert.Equal([NoiseColor.PinkPeriodic], LiveSpectrumSettingsChoices.Signals(referenceFree: true, mmm: true));
        Assert.DoesNotContain(NoiseColor.Silent, LiveSpectrumSettingsChoices.Signals(referenceFree: false, mmm: false));
    }

    [Fact]
    public void Transfer_MutesTheScaleAndTheSlope_AndRtaMutesTheCurvesItDoesNotDraw()
    {
        LiveSpectrumSettingsSession transfer = Loaded();
        Assert.False(LiveSpectrumSettingsLook.CurvesMuted(transfer));
        Assert.Equal(LiveSettingTone.Muted, LiveSpectrumSettingsLook.Spl(transfer));
        Assert.Equal(LiveSettingTone.Muted, LiveSpectrumSettingsLook.Tilt(transfer));

        LiveSpectrumSettingsSession rta = Loaded(options => options.AnalysisMode = LiveAnalysisMode.Rta);
        Assert.True(LiveSpectrumSettingsLook.CurvesMuted(rta));
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Spl(rta));
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Tilt(rta));

        rta.MoveSignal(NoiseColor.Silent);
        rta.CommitSignal();
        Assert.Equal(LiveSettingTone.Muted, LiveSpectrumSettingsLook.Tilt(rta));
    }

    [Fact]
    public void Mmm_KeepsWhatItPinsInTheNormalColour()
    {
        LiveSpectrumSettingsSession mmm = Loaded(options => options.AnalysisMode = LiveAnalysisMode.Mmm, splAvailable: false, liveCurve: true);

        Assert.True(LiveSpectrumSettingsLook.CurvesMuted(mmm));
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Spl(mmm));
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Tilt(mmm));
    }

    [Fact]
    public void Amber_MarksAnUncalibratedScaleBesideALiveCurve_AndTransferWithoutALoopback()
    {
        LiveSpectrumSettingsSession rta = Loaded(options => options.AnalysisMode = LiveAnalysisMode.Rta, splAvailable: false, liveCurve: true);
        Assert.Equal(LiveSettingTone.Warning, LiveSpectrumSettingsLook.Spl(rta));
        rta.SetAvailability(isSplAvailable: false, hasLiveCurve: false, hasTransferReference: false);
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Spl(rta));
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Transfer(rta));

        rta.SelectMode(LiveAnalysisMode.TransferFunction);
        Assert.Equal(LiveSettingTone.Warning, LiveSpectrumSettingsLook.Transfer(rta));
        rta.SetAvailability(isSplAvailable: false, hasLiveCurve: false, hasTransferReference: true);
        Assert.Equal(LiveSettingTone.Normal, LiveSpectrumSettingsLook.Transfer(rta));
    }

    [Fact]
    public void TheScaleTooltip_SaysWhyItCannotApply()
    {
        const string Base = "Shows the RTA in absolute dB SPL";
        LiveSpectrumSettingsSession session = Loaded();
        Assert.StartsWith(Base, LiveSpectrumSettingsToolTips.Spl(session));
        Assert.DoesNotContain("\r\n", LiveSpectrumSettingsToolTips.Spl(session));

        session.SetAvailability(isSplAvailable: false, hasLiveCurve: true, hasTransferReference: true);
        Assert.Contains("\r\nView-only right now", LiveSpectrumSettingsToolTips.Spl(session));

        session.SetAvailability(isSplAvailable: false, hasLiveCurve: false, hasTransferReference: true);
        Assert.Contains("\r\nNo SPL calibration is configured", LiveSpectrumSettingsToolTips.Spl(session));
    }

    [Fact]
    public void TheTransferTooltip_SaysTheAnalyzerRunsAsAnRtaWithoutALoopback()
    {
        LiveSpectrumSettingsSession session = Loaded();
        Assert.Equal(
            "Dual-channel transfer function: the microphone divided by the loopback reference, with coherence.",
            LiveSpectrumSettingsToolTips.Transfer(session));

        session.SetAvailability(isSplAvailable: true, hasLiveCurve: false, hasTransferReference: false);
        Assert.EndsWith(
            "\r\nNo loopback reference channel is configured (Measurement Options), so the analyzer runs as a " +
            "reference-free RTA regardless of this choice.",
            LiveSpectrumSettingsToolTips.Transfer(session));
    }
}
