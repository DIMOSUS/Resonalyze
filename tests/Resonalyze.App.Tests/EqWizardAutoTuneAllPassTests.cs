using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Auto Tune against a bank holding all-pass bands. The tuner fits magnitude and
/// emits bells only, so a run would replace the user's phase work with filters the
/// error curve never asked to change — the panel asks first, and these pin what
/// "keep" then has to mean: the bands survive the run, and the fit is given a
/// budget that leaves room for them.
/// </summary>
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
        // The preamp belongs to the fit — an all-pass carries no level of its own.
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
        // The tuner can regenerate a bell on the next run; the all-pass sits on a
        // junction the user aligned by hand and no run will propose it again. This
        // only bites when the fit ignored the reduced budget, so it is the backstop
        // rather than the normal path.
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
        // The two halves have to agree: if the fit spent the whole bank, the merge
        // above would have to throw away bands the user watched it place.
        using var panel = new EqWizardPanel();

        int full = MaxBandsFor(panel, reservedBands: 0);
        int reserved = MaxBandsFor(panel, reservedBands: 3);

        Assert.Equal(EqualizationCurve.MaxBandCount, full);
        Assert.Equal(EqualizationCurve.MaxBandCount - 3, reserved);
    }

    [Fact]
    public void AutoTuneOptions_WithNoRoomLeft_StillAskForAValidFit()
    {
        // A bank that is all all-pass leaves the fit nothing, and MaxBands is a
        // clamped range: a zero or negative budget must degrade to the smallest
        // legal fit rather than throw on the way to the tuner.
        using var panel = new EqWizardPanel();

        Assert.Equal(
            1, MaxBandsFor(panel, reservedBands: EqualizationCurve.MaxBandCount));
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
