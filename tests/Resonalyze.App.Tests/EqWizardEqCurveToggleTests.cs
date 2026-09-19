using OxyPlot;
using OxyPlot.Axes;

namespace Resonalyze.App.Tests;

public sealed class EqWizardEqCurveToggleTests
{
    [Fact]
    public void ByDefaultTheBanksCurveAndItsAxisAreDrawn()
    {
        var session = new EqWizardSession();

        PlotModel model = EqWizardTestPlots.Draw(session);

        Assert.True(session.ShowEqCurve);
        Assert.Contains("EQ", EqWizardTestPlots.CurveTitles(model));
        Assert.True(EqAxis(model).IsAxisVisible);
    }

    [Fact]
    public void TurningItOffTakesTheCurveAndTheRightHandAxisWithIt()
    {
        var session = new EqWizardSession();

        session.SetShowEqCurve(false);
        PlotModel model = EqWizardTestPlots.Draw(session);

        Assert.DoesNotContain("EQ", EqWizardTestPlots.CurveTitles(model));
        // A scale with no trace would read as a scale for the left-axis curves.
        Assert.False(EqAxis(model).IsAxisVisible);
    }

    [Fact]
    public void InPhaseTheBanksOwnCurveGoesButTheSharedAxisStays()
    {
        var session = new EqWizardSession();
        session.Bank.SetCount(3);
        session.SetPhaseMode(true);

        session.SetShowEqCurve(false);
        PlotModel model = EqWizardTestPlots.Draw(session, selectedBand: 1);

        Assert.DoesNotContain("EQ phase", EqWizardTestPlots.CurveTitles(model));
        Assert.Contains("Band 2 phase", EqWizardTestPlots.CurveTitles(model));
        Assert.True(EqAxis(model).IsAxisVisible);
        // Re-armed even when not drawn, or it keeps a decibel title over a phase plot.
        Assert.Equal("Phase (°)", EqAxis(model).Title);
    }

    [Fact]
    public void TheChoiceSurvivesASettingsRoundTrip()
    {
        var saved = new EqWizardSession();
        saved.SetShowEqCurve(false);

        var restored = new EqWizardSession();
        restored.ApplySettings(saved.CaptureSettings());

        Assert.False(restored.ShowEqCurve);
        Assert.DoesNotContain("EQ", EqWizardTestPlots.CurveTitles(EqWizardTestPlots.Draw(restored)));
    }

    private static Axis EqAxis(PlotModel model) =>
        model.Axes.First(axis => axis.Key == EqWizardPlot.EqGainAxisKey);
}
