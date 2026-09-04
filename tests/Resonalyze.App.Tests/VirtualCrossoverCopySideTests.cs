using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// What an L→R copy carries when the all-pass rides in the PEQ bank. The two
/// scopes address ONE band list from opposite ends — PEQ the gain-bearing bands,
/// All-pass the phase-only ones — so a copy that voiced a side must not quietly
/// take its alignment with it, and vice versa.
/// </summary>
public sealed class VirtualCrossoverCopySideTests
{
    private static readonly PeqBand Bell = new(1_000, 2.0, -4.0);
    private static readonly PeqBand Shelf = new(80, 0.7, 3.0, PeqBandType.LowShelf);
    private static readonly PeqBand SourceAllPass =
        new(90, 2.5, 0, PeqBandType.AllPassSecondOrder);
    private static readonly PeqBand TargetAllPass =
        new(300, 1.0, 0, PeqBandType.AllPassFirstOrder);

    [Fact]
    public void CopyingThePeqAlone_LeavesTheTargetsOwnAllPassStanding()
    {
        // The everyday case: the two sides hold the same driver, so the voicing is
        // shared — but each side's all-pass was tuned against its own junction, and a
        // left tweeter's arrival is not a right tweeter's.
        (VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to) =
            CreateSides();

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: false, Phase: false, Peq: true));

        Assert.Equal([Bell, Shelf, TargetAllPass], to.PeqBands);
        Assert.Equal(from.PeqPreampDb, to.PeqPreampDb);
        Assert.Equal(from.PeqSourceName, to.PeqSourceName);
    }

    [Fact]
    public void CopyingTheAllPassAlone_LeavesTheTargetsOwnVoicingStanding()
    {
        // The mirror case, and the reason the all-pass keeps a tick of its own: the
        // preamp and the profile name belong to the voicing, so they stay put too.
        (VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to) =
            CreateSides();

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: true, Phase: false, Peq: false));

        Assert.Equal([new PeqBand(3_150, 4.0, -2.0), SourceAllPass], to.PeqBands);
        Assert.Equal(-1.0, to.PeqPreampDb);
        Assert.Equal("target.txt", to.PeqSourceName);
    }

    [Fact]
    public void CopyingBoth_CarriesTheWholeBank()
    {
        (VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to) =
            CreateSides();

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: true, Phase: false, Peq: true));

        Assert.Equal([Bell, Shelf, SourceAllPass], to.PeqBands);
    }

    [Fact]
    public void CopyingNeither_LeavesTheBankUntouched()
    {
        // A copy of the crossover alone must not reshuffle the band list — the merge
        // reorders (tonal first, all-pass last), so running it needlessly would show
        // up as a bank the user never edited coming back in a different order.
        (VirtualCrossoverChannelSettings from, VirtualCrossoverChannelSettings to) =
            CreateSides();
        List<PeqBand> before = to.PeqBands;

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: true,
            AllPass: false, Phase: false, Peq: false));

        Assert.Same(before, to.PeqBands);
    }

    [Fact]
    public void AnUntickedScopesBandsSurviveEvenWhenTheMergeOverflows()
    {
        // The promise an unticked scope makes is that the target's own bands are left
        // alone. Over the slot budget it is the COPIED kind that has to give way —
        // deleting bands the user never touched, to make room for filters from the
        // other side, is silent loss on a bank they cannot see all of at once.
        var from = new VirtualCrossoverChannelSettings
        {
            PeqBands =
            {
                new PeqBand(90, 2.5, 0, PeqBandType.AllPassSecondOrder),
                new PeqBand(300, 1.0, 0, PeqBandType.AllPassFirstOrder)
            }
        };

        var to = new VirtualCrossoverChannelSettings();
        for (int i = 0; i < EqualizationCurve.MaxBandCount - 1; i++)
        {
            to.PeqBands.Add(new PeqBand(100 + i, 2.0, -1.0));
        }

        List<PeqBand> voicing = to.PeqBands.ToList();

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: true, Phase: false, Peq: false));

        // Every one of the target's own filters is still there, in order.
        Assert.Equal(voicing, to.PeqBands.Take(voicing.Count));
        // One free slot, so one all-pass came over and the second did not.
        Assert.Equal(EqualizationCurve.MaxBandCount, to.PeqBands.Count);
        Assert.Equal(
            new PeqBand(90, 2.5, 0, PeqBandType.AllPassSecondOrder), to.PeqBands[^1]);
    }

    [Fact]
    public void AMergedBankOverTheSlotBudget_DropsCopiedBandsRatherThanTheAllPass()
    {
        // A full bank on one side and an all-pass on the other cannot both fit. The
        // all-pass sits on a junction this side was aligned on and is the harder thing
        // to dial in again, so the copied gain-bearing bands are what give way.
        var from = new VirtualCrossoverChannelSettings();
        for (int i = 0; i < EqualizationCurve.MaxBandCount; i++)
        {
            from.PeqBands.Add(new PeqBand(100 + i, 2.0, -1.0));
        }

        var to = new VirtualCrossoverChannelSettings { PeqBands = { TargetAllPass } };

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: false, Phase: false, Peq: true));

        Assert.Equal(EqualizationCurve.MaxBandCount, to.PeqBands.Count);
        Assert.Equal(TargetAllPass, to.PeqBands[^1]);
        Assert.Equal(from.PeqBands.Take(EqualizationCurve.MaxBandCount - 1), to.PeqBands[..^1]);
    }

    [Fact]
    public void ThePhaseRotation_TravelsOnItsOwnTick()
    {
        // Off by default and ticked separately, like the delay: the phase control is a
        // timing tool, and the two sides are not the same distance from the listener.
        // Copied as the NUMBER — its reference is the target side's own crossover,
        // which is what the device would read.
        var from = new VirtualCrossoverChannelSettings { PhaseRotationDegrees = 90 };
        var to = new VirtualCrossoverChannelSettings { PhaseRotationDegrees = 22.5 };

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: true,
            AllPass: true, Phase: false, Peq: true));

        Assert.Equal(22.5, to.PhaseRotationDegrees);

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: false,
            AllPass: false, Phase: true, Peq: false));

        Assert.Equal(90, to.PhaseRotationDegrees);
    }

    // A source side voiced with two filters and aligned with one all-pass, against a
    // target holding a different filter and a different all-pass — so every assertion
    // above can tell which side a band came from.
    private static (VirtualCrossoverChannelSettings From, VirtualCrossoverChannelSettings To)
        CreateSides() =>
        (new VirtualCrossoverChannelSettings
        {
            PeqBands = { Bell, Shelf, SourceAllPass },
            PeqPreampDb = -4.5,
            PeqSourceName = "source.txt"
        },
        new VirtualCrossoverChannelSettings
        {
            PeqBands = { new PeqBand(3_150, 4.0, -2.0), TargetAllPass },
            PeqPreampDb = -1.0,
            PeqSourceName = "target.txt"
        });

    private static void Copy(
        VirtualCrossoverChannelSettings from,
        VirtualCrossoverChannelSettings to,
        VirtualCrossoverCopyScope scope) =>
        typeof(VirtualCrossoverPanel)
            .GetMethod(
                "CopyChainSettings",
                BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [from, to, scope]);
}
