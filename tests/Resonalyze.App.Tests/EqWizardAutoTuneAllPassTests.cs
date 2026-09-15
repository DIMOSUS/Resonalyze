using System.Reflection;
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
            EqWizardPanel.WithAllPassBands(tuned, [AllPass]);

        Assert.Equal([.. tuned.Bands, AllPass], merged.Bands);
        Assert.Equal(-3.5, merged.PreampDb);
    }

    [Fact]
    public void WithAllPassBands_WithNothingToKeep_ReturnsTheFitUntouched()
    {
        var tuned = new EqualizationCurve([new PeqBand(1_000, 2, -4)], preampDb: -1);

        Assert.Same(tuned, EqWizardPanel.WithAllPassBands(tuned, []));
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
            EqWizardPanel.WithAllPassBands(tuned, [AllPass]);

        Assert.Equal(EqualizationCurve.MaxBandCount, merged.Bands.Count);
        Assert.Equal(AllPass, merged.Bands[^1]);
        Assert.Equal(
            tuned.Bands.Take(EqualizationCurve.MaxBandCount - 1),
            merged.Bands.Take(EqualizationCurve.MaxBandCount - 1));
    }

    [Fact]
    public void AutoTuneOptions_TakeTheKeptBandsOffTheFitsBudget()
    {
        using var panel = new EqWizardPanel();

        int full = MaxBandsFor(panel, reservedBands: 0);
        int reserved = MaxBandsFor(panel, reservedBands: 3);

        Assert.Equal(EqualizationCurve.MaxBandCount, full);
        Assert.Equal(EqualizationCurve.MaxBandCount - 3, reserved);
    }

    [Fact]
    public void AutoTuneOptions_TakeTheKeptBandsOffTheCHOSENLimit_NotOffTheSlotCount()
    {
        // Max Filters budgets the bank: subtract kept bands from the user's number, not from the 32-slot ceiling.
        using var panel = new EqWizardPanel();
        SetBandLimit(panel, 8);

        Assert.Equal(8, MaxBandsFor(panel, reservedBands: 0));
        Assert.Equal(5, MaxBandsFor(panel, reservedBands: 3));
    }

    [Fact]
    public void AutoTuneOptions_WithNoRoomLeft_StillHandTheTunerARangeItCanHonour()
    {
        // AutoTune refuses a reserve that swallows the budget; the options builder must only degrade, not throw.
        using var panel = new EqWizardPanel();

        Assert.Equal(
            1, MaxBandsFor(panel, reservedBands: EqualizationCurve.MaxBandCount));
    }

    private static void SetBandLimit(EqWizardPanel panel, int limit)
    {
        dynamic combo = typeof(EqWizardPanel)
            .GetField("comboBoxBandsLimit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
        combo.SelectedItem = limit;
        Assert.Equal(limit, (int)combo.SelectedItem);
    }

    private static int MaxBandsFor(EqWizardPanel panel, int reservedBands)
    {
        object options = typeof(EqWizardPanel)
            .GetMethod(
                "CreateAutoTuneOptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [reservedBands])!;
        return (int)options.GetType()
            .GetProperty(nameof(EqAutoTuner.Options.MaxBands))!
            .GetValue(options)!;
    }
}
