using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class FirConstructorSessionTests
{
    private static readonly CrossoverEdge Lr24At2000 = new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
    private static readonly CrossoverEdge Lr24At80 = new(CrossoverFilterFamily.LinkwitzRiley, 80, 24);

    internal static FirCrossoverDesign LowPass(int taps = 255, int rate = 48_000) =>
        new(CrossoverKind.LowPass, Lr24At2000, Lr24At80, FirCrossoverMethod.IirMagnitude, FirWindow.Kaiser, 8, taps, rate);

    [Fact]
    public void AnEdit_StartsARebuildAfterASettle_AtTheDesignsRate()
    {
        var session = new FirConstructorSession();

        using FirConstructorRebuild rebuild = session.Edit(LowPass(rate: 96_000))!;

        Assert.True(session.RebuildPending);
        Assert.True(rebuild.Settle);
        Assert.Equal(96_000, rebuild.RateHz);
        Assert.Equal(LowPass(rate: 96_000), rebuild.Design);
        Assert.Null(rebuild.BareKernel);
        Assert.Empty(session.Problem);
    }

    [Fact]
    public void ADesignWithAProblem_CancelsTheRebuild_AndShowsNothing()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild first = session.Edit(LowPass())!;
        Land(session, first);
        using FirConstructorRebuild pending = session.Edit(LowPass(taps: 511))!;

        FirCrossoverDesign inverted = LowPass() with
        {
            Kind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24)
        };
        Assert.Null(session.Edit(inverted));

        Assert.True(pending.Token.IsCancellationRequested);
        Assert.False(session.RebuildPending);
        Assert.Equal(inverted.Problem(), session.Problem);
        Assert.Null(session.Rendering);
        Assert.Null(session.Design);
        Assert.Null(session.KernelName);
        Assert.False(session.Land(pending, Render(pending)));
    }

    [Fact]
    public void OnlyTheLatestRebuild_Lands_AndAnEarlierOneIsCancelled()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild first = session.Edit(LowPass(taps: 255))!;
        using FirConstructorRebuild second = session.Edit(LowPass(taps: 511))!;

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
        Assert.False(session.IsCurrent(first));
        Assert.False(session.Land(first, Render(first)));
        Assert.True(session.RebuildPending);
        Assert.Null(session.Kernel);

        FirConstructorRendering rendering = Render(second);
        Assert.True(session.Land(second, rendering));

        Assert.False(session.RebuildPending);
        Assert.Same(rendering, session.Rendering);
        Assert.Same(rendering.Kernel, session.Kernel);
        Assert.Equal(LowPass(taps: 511), session.Design);
        Assert.Null(session.KernelName);
        Assert.Equal(48_000, session.RateHz);
    }

    [Fact]
    public void ABareKernel_IsShownAtTheRateItIsGiven_WithoutASettle_AndHasNoDesign()
    {
        var session = new FirConstructorSession();
        var kernel = new FirFilter([0.25, 0.5, 0.25], 44_100);

        using FirConstructorRebuild rebuild = session.ShowBare(kernel, "room.wav", 88_200);
        Assert.False(rebuild.Settle);
        Assert.Same(kernel, rebuild.BareKernel);
        Assert.Null(rebuild.Design);
        Assert.True(session.Land(rebuild, Render(rebuild)));

        Assert.Same(kernel, session.Kernel);
        Assert.Null(session.Design);
        Assert.Equal("room.wav", session.KernelName);
        Assert.Equal(88_200, session.RateHz);
    }

    [Fact]
    public void AFailedRebuild_ShowsNothing_AndSaysWhy()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild first = session.Edit(LowPass())!;
        Land(session, first);
        using FirConstructorRebuild failing = session.Edit(LowPass(taps: 511))!;

        Assert.True(session.Fail(failing, "out of memory"));

        Assert.Equal("The kernel could not be built: out of memory", session.Problem);
        Assert.False(session.RebuildPending);
        Assert.Null(session.Rendering);
        Assert.Null(session.Design);
    }

    [Fact]
    public void AnEarlierRebuildThatFails_LeavesTheLatestAlone()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild first = session.Edit(LowPass())!;
        using FirConstructorRebuild second = session.Edit(LowPass(taps: 511))!;

        Assert.False(session.Fail(first, "cancelled too late"));

        Assert.True(session.RebuildPending);
        Assert.Empty(session.Problem);
        Assert.True(session.Land(second, Render(second)));
        Assert.Equal(LowPass(taps: 511), session.Design);
    }

    [Fact]
    public void TheFirstHandoff_SetsTheStandaloneWorkAside_WithABareKernelStillRebuilding()
    {
        var session = new FirConstructorSession();
        var bare = new FirFilter([0.25, 0.5, 0.25], 48_000);
        using FirConstructorRebuild importing = session.ShowBare(bare, "room.wav", 48_000);
        FirConstructorHandoffRequest first = Request("B");
        FirConstructorHandoffRequest second = Request("C");

        session.BeginHandoff(first, LowPass(taps: 1_023));
        Assert.True(session.InHandoff);
        Assert.Same(first, session.Handoff);
        session.BeginHandoff(second, LowPass(taps: 99));
        Assert.Same(second, session.Handoff);

        FirConstructorStandaloneWork? work = session.EndHandoff();

        Assert.False(session.InHandoff);
        Assert.Equal(new FirConstructorStandaloneWork(LowPass(taps: 1_023), bare, "room.wav"), work);
        Assert.Null(session.EndHandoff());
    }

    [Fact]
    public void AnEdit_ForgetsTheBareKernel_SoAHandoffSetsAsideOnlyTheDesign()
    {
        var session = new FirConstructorSession();
        using FirConstructorRebuild importing = session.ShowBare(new FirFilter([0.25, 0.5, 0.25]), "room.wav", 48_000);
        using FirConstructorRebuild editing = session.Edit(LowPass())!;

        session.BeginHandoff(Request("B"), LowPass());

        Assert.Equal(new FirConstructorStandaloneWork(LowPass(), null, null), session.EndHandoff());
    }

    [Fact]
    public void AFinishedRebuild_IsLetGo_SoTheNextEditDoesNotCancelItsDisposedSource()
    {
        var session = new FirConstructorSession();
        FirConstructorRebuild first = session.Edit(LowPass())!;
        Land(session, first);
        session.Finish(first);
        first.Dispose();

        using FirConstructorRebuild second = session.Edit(LowPass(taps: 511))!;

        Assert.True(session.IsCurrent(second));
    }

    internal static FirConstructorHandoffRequest Request(string channel, FirCrossoverDesign? design = null, int rate = 48_000)
    {
        var owner = new VirtualCrossoverChannel(channel);
        return new FirConstructorHandoffRequest(
            $"Channel {channel}, left side",
            design?.Build(),
            null,
            design,
            null,
            rate,
            new FirConstructorReturnToken(owner, false, 1, false, rate, null));
    }

    private static FirConstructorRendering Render(FirConstructorRebuild rebuild) =>
        FirConstructorRender.Run(rebuild, CancellationToken.None);

    private static void Land(FirConstructorSession session, FirConstructorRebuild rebuild) =>
        Assert.True(session.Land(rebuild, Render(rebuild)));
}
