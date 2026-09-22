using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AuditionFixtures;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionSessionTests
{
    [Fact]
    public void APickedTrack_IsProbed_AndOneThatCannotBeRead_LeavesNoTrack()
    {
        using var folder = new TemporaryDirectory();
        string song = Track(folder.Path, "song.wav", Rate, 2, 0.2);
        string junk = folder.File("junk.wav");
        File.WriteAllBytes(junk, [1, 2, 3, 4]);
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(Context(), new());
        session.ResultSection = "== Result ==";

        session.SelectSource(song);

        Assert.Equal(song, session.SourcePath);
        Assert.StartsWith("== Track ==", session.TrackSection);
        Assert.Empty(session.ResultSection);

        session.SelectSource(junk);

        Assert.Null(session.SourcePath);
        Assert.Contains("UNREADABLE", session.TrackSection);
    }

    [Fact]
    public void TheTrackAndTheOutput_AreComparedAsFiles()
    {
        using var folder = new TemporaryDirectory();
        string song = Track(folder.Path, "song.wav", Rate, 2, 0.1);
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(Context(), new());
        session.SelectSource(song);
        session.SelectTarget(folder.File("out.wav"));

        Assert.True(session.IsSource(song.ToUpperInvariant()));
        Assert.True(session.IsTarget(Path.Combine(folder.Path, ".", "OUT.wav")));
        Assert.False(session.IsSource(folder.File("out.wav")));
        Assert.True(session.HasDistinctFiles);
    }

    [Fact]
    public void AnOutputThatExists_IsReplacedOnlyWithConsent()
    {
        using var folder = new TemporaryDirectory();
        string previous = Track(folder.Path, "previous.wav", Rate, 2, 0.1);
        var memory = new VirtualCrossoverAuditionMemory { TargetPath = previous };

        VirtualCrossoverAuditionSession restored = VirtualCrossoverAuditionSession.Restore(Context(), memory);
        Assert.True(restored.TargetNeedsConsent);
        restored.ConfirmOverwrite();
        Assert.False(restored.TargetNeedsConsent);

        VirtualCrossoverAuditionSession picked = VirtualCrossoverAuditionSession.Restore(Context(), new());
        picked.SelectTarget(previous);
        Assert.False(picked.TargetNeedsConsent);
        picked.SelectTarget(folder.File("new.wav"));
        Assert.False(picked.TargetOverwriteConfirmed);
    }

    [Fact]
    public void TheNextDialog_RestoresWhatThisOneLeft()
    {
        using var folder = new TemporaryDirectory();
        string song = Track(folder.Path, "song.wav", 44_100, 1, 0.1);
        var memory = new VirtualCrossoverAuditionMemory();
        VirtualCrossoverAuditionSession first = VirtualCrossoverAuditionSession.Restore(Context(), memory);
        first.SelectSource(song);
        first.SelectTarget(folder.File("out.wav"));
        first.CabinStyle = CabinBodyStyle.Wagon;
        first.SpatialAverageRequested = false;
        first.Remember();

        VirtualCrossoverAuditionSession next = VirtualCrossoverAuditionSession.Restore(Context(), memory);

        Assert.Equal(song, next.SourcePath);
        Assert.Contains("Mono", next.TrackSection);
        Assert.Equal(folder.File("out.wav"), next.TargetPath);
        Assert.Equal(CabinBodyStyle.Wagon, next.CabinStyle);
        Assert.Equal("wagon", next.CabinLabel);
        Assert.False(next.SpatialAverageRequested);
    }

    [Fact]
    public void AnUntickedBoxWithoutAverages_IsNotRememberedAsAChoice()
    {
        var memory = new VirtualCrossoverAuditionMemory();
        VirtualCrossoverAuditionSession without = VirtualCrossoverAuditionSession.Restore(Context(averages: false), memory);
        Assert.False(without.SpatialAverageRequested);
        Assert.Null(without.RequestedSpatialAverage);
        without.Remember();

        VirtualCrossoverAuditionSession with = VirtualCrossoverAuditionSession.Restore(Context(), memory);

        Assert.True(with.SpatialAverageRequested);
        Assert.NotNull(with.RequestedSpatialAverage);
    }

    [Fact]
    public void ATrackThatWentAway_IsNotRestored()
    {
        using var folder = new TemporaryDirectory();
        var memory = new VirtualCrossoverAuditionMemory { SourcePath = folder.File("gone.wav") };

        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(Context(), memory);
        session.Remember();

        Assert.Null(session.SourcePath);
        Assert.Contains("UNREADABLE", session.TrackSection);
        Assert.Null(memory.SourcePath);
    }

    [Fact]
    public void ARender_IsCancelledOnce()
    {
        VirtualCrossoverAuditionSession session = VirtualCrossoverAuditionSession.Restore(Context(), new());
        Assert.False(session.RequestCancel());

        CancellationToken token = session.BeginRender();

        Assert.True(session.Rendering);
        Assert.True(session.RequestCancel());
        Assert.True(token.IsCancellationRequested);
        Assert.False(session.RequestCancel());
        session.EndRender();
        Assert.False(session.Rendering);
    }
}
