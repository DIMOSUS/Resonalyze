using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The Lock reads by difference at every save: shown-side hand edits carry; pre-existing, automatic and hidden-side ones are kept.</summary>
public sealed class VirtualCrossoverSideLockTests
{
    private static readonly CrossoverEdge Lr24At80 =
        new(CrossoverFilterFamily.LinkwitzRiley, 80, 24);
    private static readonly CrossoverEdge Lr24At2500 =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24);
    private static readonly CrossoverEdge Bw12At100 =
        new(CrossoverFilterFamily.Butterworth, 100, 12);
    private static readonly CrossoverEdge Lr48At3000 =
        new(CrossoverFilterFamily.LinkwitzRiley, 3_000, 48);

    [Fact]
    public void EngagingCopiesNothing_ExistingDifferencesStand()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.HighPassEdge = Bw12At100;
        pair.Right.InvertPolarity = true;
        var sideLock = new VirtualCrossoverSideLock();

        sideLock.Engage([pair]);
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.True(pair.Right.InvertPolarity);
        Assert.False(pair.Left.InvertPolarity);
    }

    [Fact]
    public void ACornerMovedOnTheShownSide_CarriesTheWholeCrossoverAcross()
    {
        // Moving one edge equalizes the whole crossover; polarity is its own unit.
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.LowPassEdge = Lr48At3000;
        pair.Right.InvertPolarity = true;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.HighPassEdge = Bw12At100;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.True(wrote);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.Equal(Lr24At2500, pair.Right.LowPassEdge);
        Assert.Equal(CrossoverKind.BandPass, pair.Right.CrossoverKind);
        Assert.True(pair.Right.InvertPolarity);
    }

    [Fact]
    public void APolarityFlipOnTheShownSide_CarriesOnlyThePolarity()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.LowPassEdge = Lr48At3000;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.InvertPolarity = true;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.True(wrote);
        Assert.True(pair.Right.InvertPolarity);
        Assert.Equal(Lr48At3000, pair.Right.LowPassEdge);
    }

    [Fact]
    public void TheRightSideShown_CarriesOntoTheLeft()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Right.CrossoverKind = CrossoverKind.HighPass;
        pair.Right.HighPassEdge = Bw12At100;
        bool wrote = sideLock.Follow([pair], shownRight: true);

        Assert.True(wrote);
        Assert.Equal(CrossoverKind.HighPass, pair.Left.CrossoverKind);
        Assert.Equal(Bw12At100, pair.Left.HighPassEdge);
    }

    [Fact]
    public void BothUnitsMovedOnTheShownSideInOneStep_AreBothCarried()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.HighPassEdge = Bw12At100;
        pair.Left.InvertPolarity = true;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.True(wrote);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.True(pair.Right.InvertPolarity);
    }

    [Fact]
    public void ACarriedChange_IsNotCarriedBackFromTheOtherSide()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.HighPassEdge = Bw12At100;
        pair.Left.InvertPolarity = true;
        Assert.True(sideLock.Follow([pair], shownRight: false));

        Assert.False(sideLock.Follow([pair], shownRight: true));
        Assert.Equal(Bw12At100, pair.Left.HighPassEdge);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.True(pair.Left.InvertPolarity);
        Assert.True(pair.Right.InvertPolarity);
    }

    [Fact]
    public void GainDelayPhaseAndPeq_AreNotLocked()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.GainDb = -3;
        pair.Left.DelayMs = 1.5;
        pair.Left.PhaseRotationDegrees = 90;
        pair.Left.PeqBands.Add(new PeqBand(1_000, 2.0, -4.0));
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.Equal(0, pair.Right.GainDb);
        Assert.Equal(0, pair.Right.DelayMs);
        Assert.Equal(0, pair.Right.PhaseRotationDegrees);
        Assert.Empty(pair.Right.PeqBands);
    }

    [Fact]
    public void AnEdgeWrittenOntoBothSides_IsRememberedNotCarriedWholesale()
    {
        // A junction tune writes one edge on both sides, so as a difference it would carry the other edge too; the panel calls Remember.
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.HighPassEdge = Bw12At100;
        pair.Right.LowPassEdge = Lr48At3000;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.LowPassEdge = Lr48At3000;
        pair.Right.LowPassEdge = Lr48At3000;
        sideLock.Remember([pair]);
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.Equal(Lr24At80, pair.Left.HighPassEdge);
    }

    [Fact]
    public void WithoutRemember_TheSameRunWouldBeReadAsAHandEdit()
    {
        // Pinned so a guard that "detects" automatic runs is not reintroduced in place of the explicit call.
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.HighPassEdge = Bw12At100;
        pair.Right.LowPassEdge = Lr48At3000;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.LowPassEdge = Lr48At3000;
        pair.Right.LowPassEdge = Lr48At3000;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.True(wrote);
        Assert.Equal(Lr24At80, pair.Right.HighPassEdge);
    }

    [Fact]
    public void ARunThatKeptTheHiddenSide_IsRememberedRatherThanRead()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.InvertPolarity = true;
        sideLock.Remember([pair]);
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.False(pair.Right.InvertPolarity);
    }

    [Fact]
    public void AChangeOnTheHiddenSide_IsAbsorbedRatherThanMirroredBack()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Right.HighPassEdge = Bw12At100;
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
        Assert.Equal(Lr24At80, pair.Left.HighPassEdge);

        Assert.False(sideLock.Follow([pair], shownRight: true));
        Assert.Equal(Lr24At80, pair.Left.HighPassEdge);
    }

    [Fact]
    public void AMonoPair_IsLeftAlone()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Mono = true;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.HighPassEdge = Bw12At100;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.Equal(Lr24At80, pair.Right.HighPassEdge);
    }

    [Fact]
    public void APairFirstSeenAtASave_IsRememberedNotMirrored()
    {
        VirtualCrossoverChannelPairSettings first = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([first]);

        VirtualCrossoverChannelPairSettings added = StereoPair();
        added.Left.HighPassEdge = Bw12At100;
        Assert.False(sideLock.Follow([first, added], shownRight: false));
        Assert.Equal(Lr24At80, added.Right.HighPassEdge);

        added.Left.InvertPolarity = true;
        Assert.True(sideLock.Follow([first, added], shownRight: false));
        Assert.True(added.Right.InvertPolarity);
    }

    [Fact]
    public void RebindingAfterALoad_StartsFromTheLoadedState()
    {
        VirtualCrossoverChannelPairSettings before = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([before]);

        VirtualCrossoverChannelPairSettings loaded = StereoPair();
        sideLock.Remember([loaded]);
        loaded.Left.InvertPolarity = true;

        Assert.True(sideLock.Follow([loaded], shownRight: false));
        Assert.True(loaded.Right.InvertPolarity);
    }

    [Fact]
    public void ReleasedLock_CarriesNothing()
    {
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);
        sideLock.Release();

        pair.Left.InvertPolarity = true;

        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.False(pair.Right.InvertPolarity);
    }

    private static VirtualCrossoverChannelPairSettings StereoPair() => new()
    {
        Left = BandPass(),
        Right = BandPass()
    };

    private static VirtualCrossoverChannelSettings BandPass() => new()
    {
        CrossoverKind = CrossoverKind.BandPass,
        HighPassEdge = Lr24At80,
        LowPassEdge = Lr24At2500
    };
}
