using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What the Lock beside the side radios carries across, and what it leaves alone.
/// It reads by difference at every save, so the questions are all about WHICH
/// difference: one the hand made on the shown side (carried), one already there
/// when the lock went on (kept), one an automatic run wrote on both sides at once
/// (kept), one made on the hidden side (kept).
/// </summary>
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
        // The sides disagreed on the low-pass before the lock; moving the HIGH-pass
        // on the left equalizes the whole crossover, not just the corner touched —
        // otherwise the two crossovers would still differ after an edit meant to
        // make them the same. Polarity is its own unit and stays.
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
        // One save can hold a crossover move and a polarity flip together (a batch
        // update of the block); the two units are read independently, so neither
        // hides the other.
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
        // The write onto the hidden side is remembered in the same pass, so when
        // the user switches to that side the lock finds nothing that moved there —
        // a carried value is not a hand edit to bounce back.
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
    public void BothSidesWrittenInOneStep_KeepTheirOwnAnswers()
    {
        // A step that moved the hidden side too is an automatic run (the crossover
        // wizard, a junction tune, an agent proposal addressing both sides), and its
        // answer for the hidden side is not overwritten by the shown one.
        VirtualCrossoverChannelPairSettings pair = StereoPair();
        pair.Right.InvertPolarity = true;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.InvertPolarity = true;
        pair.Right.InvertPolarity = false;
        pair.Right.HighPassEdge = Bw12At100;
        pair.Left.HighPassEdge = Lr48At3000;
        bool wrote = sideLock.Follow([pair], shownRight: false);

        Assert.False(wrote);
        Assert.False(pair.Right.InvertPolarity);
        Assert.Equal(Bw12At100, pair.Right.HighPassEdge);
    }

    [Fact]
    public void ARunThatKeptTheHiddenSide_IsRememberedRatherThanRead()
    {
        // The auto-delay may flip the shown side and decide to KEEP the hidden one —
        // which a difference cannot tell from a hand that only reached the shown
        // side. The panel hands such a result to Remember, and nothing is carried.
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
        // Something wrote the hidden side alone (an L→R copy from the dialog, say)
        // while the left was shown. It is not undone, and when the user then switches
        // to that side it is not carried back either: it was already remembered.
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
        // A block added under the lock arrives at the save that added it: its state
        // is the starting point, and nothing is carried for it until it is edited.
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
        // The panel binds new pair objects on a load. Remember takes them as
        // loaded, so the very first edit afterwards is carried like any other.
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
