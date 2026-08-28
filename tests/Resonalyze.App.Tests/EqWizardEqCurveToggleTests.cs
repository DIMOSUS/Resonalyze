using System.Reflection;
using System.Windows.Forms;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The EQ-curve toggle: whether the bank's own response is drawn on the plot's
/// right-hand axis. It is a view preference, so it persists — and the axis it owns
/// goes with it, but only while nothing else is drawn against that axis.
/// </summary>
public sealed class EqWizardEqCurveToggleTests
{
    private const string EqGainAxisKey = "eq-wizard:gain";

    [Fact]
    public void ByDefaultTheBanksCurveAndItsAxisAreDrawn()
    {
        using var panel = new EqWizardPanel();

        Assert.True(Toggle(panel).Checked);
        Assert.Contains("EQ", CurveTitles(panel));
        Assert.True(EqAxis(panel).IsAxisVisible);
    }

    [Fact]
    public void TurningItOffTakesTheCurveAndTheRightHandAxisWithIt()
    {
        using var panel = new EqWizardPanel();

        Toggle(panel).Checked = false;

        Assert.DoesNotContain("EQ", CurveTitles(panel));
        // The axis carries that curve alone in this view, and a scale with no trace
        // on it reads as a scale for the curves that ARE drawn — which belong to
        // the left axis.
        Assert.False(EqAxis(panel).IsAxisVisible);
    }

    [Fact]
    public void InPhaseTheBanksOwnCurveGoesButTheSharedAxisStays()
    {
        using var panel = new EqWizardPanel();
        SetBandCount(panel, 3);
        SelectSlot(panel, Slots(panel)[1]);
        Field<CheckBox>(panel, "checkBoxEqPhase").Checked = true;

        Toggle(panel).Checked = false;

        // Every phase curve is on the right axis, so emptying it takes more than
        // turning this one off — here the selected band's phase is still there.
        Assert.DoesNotContain("EQ phase", CurveTitles(panel));
        Assert.Contains("Band 2 phase", CurveTitles(panel));
        Assert.True(EqAxis(panel).IsAxisVisible);
        // Re-armed even though the curve was not drawn: the axis has to be told it
        // carries degrees, or it keeps a decibel title over a phase plot.
        Assert.Equal("Phase (°)", EqAxis(panel).Title);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        using var saved = new EqWizardPanel();
        Toggle(saved).Checked = false;

        using var restored = new EqWizardPanel();
        ApplyPersistedSettings(restored, saved.CaptureSettings());

        Assert.False(Toggle(restored).Checked);
        Assert.DoesNotContain("EQ", CurveTitles(restored));
    }

    private static CheckBox Toggle(EqWizardPanel panel) =>
        Field<CheckBox>(panel, "checkBoxEqCurve");

    private static Axis EqAxis(EqWizardPanel panel) =>
        Field<PlotView>(panel, "plotWizard").Model!.Axes
            .First(axis => axis.Key == EqGainAxisKey);

    private static IReadOnlyList<string> CurveTitles(EqWizardPanel panel) =>
        Field<PlotView>(panel, "plotWizard").Model!.Series
            .OfType<XYAxisSeries>()
            .Select(series => series.Title ?? string.Empty)
            .ToList();

    private static IReadOnlyList<PeqSlotControl> Slots(EqWizardPanel panel) =>
        Field<List<PeqSlotControl>>(panel, "peqSlots");

    private static void SetBandCount(EqWizardPanel panel, int count) =>
        Invoke(panel, "SetBandCount", count);

    private static void SelectSlot(EqWizardPanel panel, object slot) =>
        Invoke(panel, "SelectSlot", slot);

    private static void ApplyPersistedSettings(
        EqWizardPanel panel,
        MeasurementSettingsFile.EqWizardSettings settings) =>
        Invoke(panel, "ApplyPersistedSettings", settings);

    private static void Invoke(EqWizardPanel panel, string name, params object[] arguments) =>
        typeof(EqWizardPanel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, arguments);

    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
