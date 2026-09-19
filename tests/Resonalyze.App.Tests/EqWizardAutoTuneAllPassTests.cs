using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>The tuner emits bells only, so kept all-pass bands must survive the run and be reserved from its budget.</summary>
public sealed class EqWizardAutoTuneAllPassTests
{
    private static readonly PeqBand AllPass =
        new(90, 2.5, 0, PeqBandType.AllPassSecondOrder);

    [Fact]
    public void WithAllPassBands_KeepsThemAfterTheFittedBands()
    {
        var tuned = new EqualizationCurve(
            [new PeqBand(1_000, 2, -4), new PeqBand(3_150, 3, -2)], preampDb: -3.5);

        EqualizationCurve merged =
            EqWizardFit.WithAllPassBands(tuned, [AllPass]);

        Assert.Equal([.. tuned.Bands, AllPass], merged.Bands);
        Assert.Equal(-3.5, merged.PreampDb);
    }

    [Fact]
    public void WithAllPassBands_WithNothingToKeep_ReturnsTheFitUntouched()
    {
        var tuned = new EqualizationCurve([new PeqBand(1_000, 2, -4)], preampDb: -1);

        Assert.Same(tuned, EqWizardFit.WithAllPassBands(tuned, []));
    }

    [Fact]
    public void WithAllPassBands_OverTheSlotBudget_DropsFittedBandsNotTheAllPass()
    {
        // Backstop: the tuner can regenerate a bell, but a hand-aligned all-pass would never be proposed again.
        var tuned = new EqualizationCurve(
            Enumerable.Range(0, EqualizationCurve.MaxBandCount)
                .Select(i => new PeqBand(100 + i, 2, -1)),
            preampDb: 0);

        EqualizationCurve merged =
            EqWizardFit.WithAllPassBands(tuned, [AllPass]);

        Assert.Equal(EqualizationCurve.MaxBandCount, merged.Bands.Count);
        Assert.Equal(AllPass, merged.Bands[^1]);
        Assert.Equal(
            tuned.Bands.Take(EqualizationCurve.MaxBandCount - 1),
            merged.Bands.Take(EqualizationCurve.MaxBandCount - 1));
    }

    [Fact]
    public void AutoTuneOptions_TakeTheKeptBandsOffTheFitsBudget()
    {
        var session = new EqWizardSession();

        int full = EqWizardFit.Options(session, reservedBands: 0).MaxBands;
        int reserved = EqWizardFit.Options(session, reservedBands: 3).MaxBands;

        Assert.Equal(EqualizationCurve.MaxBandCount, full);
        Assert.Equal(EqualizationCurve.MaxBandCount - 3, reserved);
    }

    [Fact]
    public void AutoTuneOptions_TakeTheKeptBandsOffTheCHOSENLimit_NotOffTheSlotCount()
    {
        // Max Filters budgets the bank: subtract kept bands from the user's number, not from the 32-slot ceiling.
        var session = new EqWizardSession();
        session.SetBandLimit(8);

        Assert.Equal(8, EqWizardFit.Options(session, reservedBands: 0).MaxBands);
        Assert.Equal(5, EqWizardFit.Options(session, reservedBands: 3).MaxBands);
    }

    [Fact]
    public void AutoTuneOptions_WithNoRoomLeft_StillHandTheTunerARangeItCanHonour()
    {
        // AutoTune refuses a reserve that swallows the budget; the options builder must only degrade, not throw.
        var session = new EqWizardSession();

        Assert.Equal(
            1, EqWizardFit.Options(session, reservedBands: EqualizationCurve.MaxBandCount).MaxBands);
    }
}
