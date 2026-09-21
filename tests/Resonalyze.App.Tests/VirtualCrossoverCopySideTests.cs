using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>PEQ and All-pass scopes address one band list from opposite ends: voicing and alignment copy independently.</summary>
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
        // The merge reorders (tonal first, all-pass last), so a crossover-only copy must not run it.
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
        // Over the slot budget the COPIED kind gives way; the target's own bands are never deleted.
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

        Assert.Equal(voicing, to.PeqBands.Take(voicing.Count));
        Assert.Equal(EqualizationCurve.MaxBandCount, to.PeqBands.Count);
        Assert.Equal(
            new PeqBand(90, 2.5, 0, PeqBandType.AllPassSecondOrder), to.PeqBands[^1]);
    }

    [Fact]
    public void AMergedBankOverTheSlotBudget_DropsCopiedBandsRatherThanTheAllPass()
    {
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
    public void TheAcousticWish_TravelsWithTheCrossoverItDescribes()
    {
        // The wish is stated FOR an edge; left behind, it would aim the target of a side that now runs another
        // filter. A side without one must have it cleared, not inherit the target's.
        var from = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.BandPass,
            HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 80, 24),
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 2_500, 18),
            AcousticHighPass = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };
        var to = new VirtualCrossoverChannelSettings
        {
            AcousticHighPass = new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 12),
            AcousticLowPass = new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 48)
        };

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: false, Delay: false, InvertPolarity: false, Crossover: true,
            AllPass: false, Phase: false, Peq: false));

        Assert.Equal(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24), to.AcousticHighPass);
        Assert.Null(to.AcousticLowPass);
    }

    [Fact]
    public void AnUntickedCrossover_LeavesTheTargetsOwnAcousticWishStanding()
    {
        var from = new VirtualCrossoverChannelSettings
        {
            AcousticHighPass = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)
        };
        var to = new VirtualCrossoverChannelSettings
        {
            AcousticHighPass = new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 12)
        };

        Copy(from, to, new VirtualCrossoverCopyScope(
            Gain: true, Delay: true, InvertPolarity: true, Crossover: false,
            AllPass: true, Phase: true, Peq: true));

        Assert.Equal(
            new JunctionAcousticTarget(CrossoverFilterFamily.Bessel, 12), to.AcousticHighPass);
    }
    [Fact]
    public void ThePhaseRotation_TravelsOnItsOwnTick()
    {
        // Copied as the NUMBER: its reference is the target side's own crossover.
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
        scope.Copy(from, to);
}
