using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumSettingsSessionTests
{
    internal static LiveSpectrumSettingsSession Loaded(
        Action<LiveSpectrumOptions>? edit = null,
        bool splAvailable = true,
        bool liveCurve = false,
        bool loopback = true,
        int rate = 48_000)
    {
        var options = new LiveSpectrumOptions();
        edit?.Invoke(options);
        var session = new LiveSpectrumSettingsSession();
        session.Load(options, splAvailable, liveCurve, loopback, rate);
        return session;
    }

    internal static LiveSpectrumOptions Written(LiveSpectrumSettingsSession session)
    {
        var options = new LiveSpectrumOptions();
        session.WriteTo(options);
        return options;
    }

    [Fact]
    public void AStoredValue_LandsOnTheList_WhileTheUsersOwnPickIsKeptAsWritten()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.NoiseColor = NoiseColor.Pink;
            options.SequenceLength = 5_000;
            options.OverlapPercent = 30;
            options.CoherenceThresholdPercent = 15;
            options.WindowType = (WindowType)99;
            options.AveragingSpeed = (AveragingSpeed)99;
            options.SmoothingInverseOctaves = 5;
        });

        Assert.Equal(4_096, session.SequenceLength);
        Assert.Equal(0, session.OverlapPercent);
        Assert.Equal(10, session.CoherenceLimitPercent);
        Assert.Equal(WindowType.Hann, session.Window);
        Assert.Equal(AveragingSpeed.Fast, session.Averaging);
        Assert.Equal(6, session.SmoothingInverseOctaves);

        LiveSpectrumOptions written = Written(session);
        Assert.Equal(4_096, written.SequenceLength);
        Assert.Equal(30, written.OverlapPercent);
        Assert.Equal(10, written.CoherenceThresholdPercent);
        Assert.Equal((WindowType)99, written.WindowType);
        Assert.Equal((AveragingSpeed)99, written.AveragingSpeed);
        Assert.Equal(6, written.SmoothingInverseOctaves);
    }

    [Fact]
    public void Mmm_PinsItsRecipe_AndLeavingItGivesTheUsersChoicesBack()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.AnalysisMode = LiveAnalysisMode.Rta;
            options.NoiseColor = NoiseColor.White;
            options.WindowType = WindowType.BlackmanHarris;
            options.OverlapPercent = 75;
            options.AveragingSpeed = AveragingSpeed.Slow;
            options.SmoothingInverseOctaves = 3;
            options.MagnitudeScale = MagnitudeScale.Relative;
            options.CompensateNoiseTilt = false;
        });

        session.SelectMode(LiveAnalysisMode.Mmm);

        Assert.Equal([NoiseColor.PinkPeriodic], session.Signals);
        Assert.Equal(NoiseColor.PinkPeriodic, session.Signal);
        Assert.Equal((WindowType.Rectangular, 0), (session.Window, session.OverlapPercent));
        Assert.Equal((AveragingSpeed.Infinite, 0), (session.Averaging, session.SmoothingInverseOctaves));
        Assert.True(session.Spl && session.Tilt && session.InputMagnitude);
        Assert.False(session.SignalEditable || session.WindowEditable || session.OverlapEditable);
        Assert.False(session.AveragingEditable || session.SmoothingEditable || session.CoherenceLimitEditable);
        Assert.False(session.SplInteractive || session.TiltInteractive || session.InputMagnitudeInteractive);
        LiveSpectrumOptions pinned = Written(session);
        Assert.Equal(
            (LiveAnalysisMode.Mmm, NoiseColor.White, WindowType.BlackmanHarris, 75, AveragingSpeed.Slow, 3),
            (pinned.AnalysisMode, pinned.NoiseColor, pinned.WindowType, pinned.OverlapPercent, pinned.AveragingSpeed,
                pinned.SmoothingInverseOctaves));
        Assert.Equal((MagnitudeScale.Relative, false), (pinned.MagnitudeScale, pinned.CompensateNoiseTilt));

        session.SelectMode(LiveAnalysisMode.Rta);

        Assert.Equal(NoiseColor.White, session.Signal);
        Assert.Equal((WindowType.BlackmanHarris, 75), (session.Window, session.OverlapPercent));
        Assert.Equal((AveragingSpeed.Slow, 3), (session.Averaging, session.SmoothingInverseOctaves));
        Assert.False(session.Spl || session.Tilt);
    }

    [Fact]
    public void PeriodicPink_ForcesARectangularWindowWithoutOverlap_AndAnotherNoiseRestoresThePicks()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.NoiseColor = NoiseColor.Pink;
            options.WindowType = WindowType.FlatTop;
            options.OverlapPercent = 50;
        });

        session.MoveSignal(NoiseColor.PinkPeriodic);
        session.CommitSignal();
        Assert.Equal((WindowType.Rectangular, 0, false, false),
            (session.Window, session.OverlapPercent, session.WindowEditable, session.OverlapEditable));

        session.MoveSignal(NoiseColor.Brown);
        session.CommitSignal();
        Assert.Equal((WindowType.FlatTop, 50, true, true),
            (session.Window, session.OverlapPercent, session.WindowEditable, session.OverlapEditable));
        Assert.Equal(NoiseColor.Brown, Written(session).NoiseColor);
    }

    [Fact]
    public void APickMovedWithoutACommit_ShowsButRunsNoRule_AndIsNotTheUsersPick()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.AnalysisMode = LiveAnalysisMode.Rta;
            options.NoiseColor = NoiseColor.Pink;
            options.WindowType = WindowType.Hann;
        });

        session.MoveSignal(NoiseColor.Silent);
        session.MoveWindow(WindowType.FlatTop);

        Assert.True(session.TiltApplicable);
        Assert.True(session.WindowEditable);
        LiveSpectrumOptions written = Written(session);
        Assert.Equal(NoiseColor.Silent, written.NoiseColor);
        Assert.Equal(WindowType.Hann, written.WindowType);

        session.CommitWindow();
        Assert.Equal(WindowType.FlatTop, Written(session).WindowType);
        session.CommitSignal();
        Assert.False(session.TiltApplicable);
        Assert.Equal(WindowType.FlatTop, session.Window);
    }

    [Fact]
    public void Silent_IsRtaOnly_AndComesBackWhenRtaDoes()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.AnalysisMode = LiveAnalysisMode.Rta;
            options.NoiseColor = NoiseColor.Silent;
        });
        Assert.Equal(NoiseColor.Silent, session.Signal);
        Assert.Contains(NoiseColor.Silent, session.Signals);

        session.SelectMode(LiveAnalysisMode.TransferFunction);
        Assert.DoesNotContain(NoiseColor.Silent, session.Signals);
        Assert.Equal(NoiseColor.PinkPeriodic, session.Signal);
        Assert.Equal(NoiseColor.PinkPeriodic, Written(session).NoiseColor);

        session.SelectMode(LiveAnalysisMode.Rta);
        Assert.Equal(NoiseColor.Silent, session.Signal);
    }

    [Fact]
    public void TheReferenceFreeModes_ForceTheRtaOn_AndATransferGivesTheUsersTickBack()
    {
        LiveSpectrumSettingsSession session = Loaded(options => options.ShowInputMagnitude = false);
        Assert.False(session.InputMagnitude);
        Assert.True(session.InputMagnitudeInteractive);

        session.SetInputMagnitude(true);
        session.ClickInputMagnitude();
        session.SelectMode(LiveAnalysisMode.Rta);
        session.SetInputMagnitude(false);
        session.ClickInputMagnitude();

        Assert.True(Written(session).ShowInputMagnitude);
        session.SelectMode(LiveAnalysisMode.TransferFunction);
        Assert.True(session.InputMagnitude);
    }

    [Fact]
    public void AClick_IsTheUsersPickOnlyWhereTheBoxTakesOne()
    {
        LiveSpectrumSettingsSession session = Loaded(options => options.AnalysisMode = LiveAnalysisMode.Rta);
        session.SetSpl(true);
        session.ClickSpl();
        session.SetTilt(true);
        session.ClickTilt();
        LiveSpectrumOptions rta = Written(session);
        Assert.Equal((MagnitudeScale.SoundPressureLevel, true), (rta.MagnitudeScale, rta.CompensateNoiseTilt));

        session.SelectMode(LiveAnalysisMode.TransferFunction);
        Assert.False(session.SplInteractive || session.TiltInteractive);
        session.SetSpl(false);
        session.ClickSpl();
        session.SetTilt(false);
        session.ClickTilt();
        LiveSpectrumOptions transfer = Written(session);
        Assert.Equal((MagnitudeScale.SoundPressureLevel, true), (transfer.MagnitudeScale, transfer.CompensateNoiseTilt));
    }

    [Fact]
    public void ForcingTheScaleOff_AlsoForgetsTheUsersPick()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.AnalysisMode = LiveAnalysisMode.Mmm;
            options.MagnitudeScale = MagnitudeScale.SoundPressureLevel;
        });

        session.ForceSplOff();

        Assert.False(session.Spl);
        Assert.Equal(MagnitudeScale.Relative, Written(session).MagnitudeScale);
        session.SelectMode(LiveAnalysisMode.Rta);
        Assert.False(session.Spl);
    }

    [Fact]
    public void TheSameMode_RunsNoRule()
    {
        LiveSpectrumSettingsSession session = Loaded(options =>
        {
            options.AnalysisMode = LiveAnalysisMode.Rta;
            options.NoiseColor = NoiseColor.Pink;
        });
        session.MoveSignal(NoiseColor.White);

        session.SelectMode(LiveAnalysisMode.Rta);

        Assert.Equal(NoiseColor.White, session.Signal);
    }

    [Fact]
    public void TheAvailability_MarksAnUncalibratedScaleOnlyBesideALiveCurve()
    {
        LiveSpectrumSettingsSession session = Loaded(splAvailable: false, liveCurve: true, loopback: false);
        Assert.True(session.SplViewOnlyConflict);
        Assert.False(session.HasTransferReference);

        session.SetAvailability(isSplAvailable: false, hasLiveCurve: false, hasTransferReference: true);
        Assert.False(session.SplViewOnlyConflict);
        Assert.False(session.SplAvailable);
        Assert.True(session.HasTransferReference);
    }

    [Fact]
    public void ACheckedBox_AndAListNoRuleTouches_AreWrittenAsShown()
    {
        LiveSpectrumSettingsSession session = Loaded();
        session.SetMainCurve(false);
        session.SetPeakHold(true);
        session.SetCoherence(false);
        session.MoveSequenceLength(512);
        session.MoveCoherenceLimit(40);
        session.MoveAveraging(AveragingSpeed.Slow);
        session.CommitAveraging();
        session.MoveSmoothing(12);
        session.CommitSmoothing();
        session.MoveOverlap(75);
        session.CommitOverlap();
        session.SetCalibration(null);

        LiveSpectrumOptions written = Written(session);
        Assert.Equal(
            (false, true, false, 512, 40, AveragingSpeed.Slow, 12, 75),
            (written.ShowMainCurve, written.PeakHold, written.ShowCoherence, written.SequenceLength,
                written.CoherenceThresholdPercent, written.AveragingSpeed, written.SmoothingInverseOctaves,
                written.OverlapPercent));
        Assert.Empty(session.Calibration);
    }
}
