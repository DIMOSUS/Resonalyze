using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AuditionFixtures;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionRenderTests
{
    [Fact]
    public void Render_NeedsATrackAnOutputAndACalibrationItCanCarry_OrARenderToCancel()
    {
        using var folder = new TemporaryDirectory();
        string song = Track(folder.Path, "song.wav", Rate, 2, 0.1);
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(Context(own: Conflict()), new());
        Assert.False(VirtualCrossoverAuditionRender.Available(session));

        session.SelectSource(song);
        Assert.False(VirtualCrossoverAuditionRender.Available(session));
        session.SelectTarget(folder.File("out.wav"));
        Assert.True(VirtualCrossoverAuditionRender.Available(session));

        session.SelectCalibration(VirtualCrossoverCalibrationSelection.OwnId, "Own");
        Assert.False(VirtualCrossoverAuditionRender.Available(session));
        session.BeginRender();
        Assert.True(VirtualCrossoverAuditionRender.Available(session));
    }

    [Fact]
    public void TheRequest_IsReadOnce_WithTheLabelsTheResultShows()
    {
        using var folder = new TemporaryDirectory();
        VirtualCrossoverAuditionSession session = Ready(folder, Context());
        session.CabinStyle = null;
        session.SpatialAverageRequested = false;

        AuditionRenderRequest plain = VirtualCrossoverAuditionRender.Request(session);
        session.CabinStyle = CabinBodyStyle.Hatchback;
        session.SpatialAverageRequested = true;
        session.SelectCalibration(Good.Id, "good mic");

        Assert.Null(plain.Cabin);
        Assert.Equal("off", plain.CabinLabel);
        Assert.Null(plain.SpatialAverage);
        Assert.Equal("impulse responses (one microphone position)", plain.MagnitudeLabel);
        Assert.Equal("off", plain.CalibrationLabel);

        AuditionRenderRequest full = VirtualCrossoverAuditionRender.Request(session);
        Assert.NotNull(full.Cabin);
        Assert.Equal("hatchback", full.CabinLabel);
        Assert.Same(session.Context.SpatialAverage, full.SpatialAverage);
        Assert.Equal("spatial averages (MMM / array)", full.MagnitudeLabel);
        Assert.Equal("good mic", full.CalibrationLabel);
        Assert.Equal(session.SourcePath, full.SourcePath);
        Assert.Equal(session.TargetPath, full.TargetPath);
    }

    [Fact]
    public void ARender_WritesTheTrackThroughTheTune()
    {
        using var folder = new TemporaryDirectory();
        VirtualCrossoverAuditionSession session = Ready(folder, Context());
        session.CabinStyle = CabinBodyStyle.Sedan;
        session.SelectCalibration(Good.Id, "good mic");
        var reports = new List<AuditionProgress>();

        AuditionRenderOutcome outcome = VirtualCrossoverAuditionRender.Run(
            VirtualCrossoverAuditionRender.Request(session), new SynchronousProgress<AuditionProgress>(reports.Add), default);

        Assert.True(File.Exists(session.TargetPath));
        Assert.False(File.Exists(session.TargetPath + ".partial"));
        Assert.Equal(2, AudioFileCodec.Probe(session.TargetPath!).ChannelCount);
        Assert.True(outcome.CabinApplied);
        Assert.True(outcome.CorrectionFirTaps > 0);
        Assert.Equal("good mic", outcome.CalibrationLabel);
        Assert.Equal("average sedan", outcome.CabinLabel);
        Assert.Equal(("Finished", 1.0), (reports[^1].Status, reports[^1].Fraction));
    }

    [Fact]
    public void ACancelledRender_WritesNothing()
    {
        using var folder = new TemporaryDirectory();
        VirtualCrossoverAuditionSession session = Ready(folder, Context());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => VirtualCrossoverAuditionRender.Run(
            VirtualCrossoverAuditionRender.Request(session), new SynchronousProgress<AuditionProgress>(_ => { }), cancellation.Token));

        Assert.Empty(Directory.GetFiles(folder.Path, "out*"));
    }

    private static VirtualCrossoverAuditionSession Ready(TemporaryDirectory folder, VirtualCrossoverAuditionContext context)
    {
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(context, new());
        session.SelectSource(Track(folder.Path, "song.wav", 44_100, 2, 0.2));
        session.SelectTarget(folder.File("out.wav"));
        return session;
    }
}
