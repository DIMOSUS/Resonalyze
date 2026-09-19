using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze.App.Tests;

/// <summary>The selected band is marked where it sits: a low-Q bell's summit is guesswork, and a shelf has none.</summary>
public sealed class EqWizardBandGuideTests
{
    [Fact]
    public void SelectingABand_PutsTheGuideOnItsFrequency()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);

        PlotModel model = EqWizardTestPlots.Draw(session, selectedBand: 1);

        LineAnnotation guide = Assert.Single(Guides(model));
        Assert.Equal(1_250.0, guide.X);
        Assert.Equal(LineAnnotationType.Vertical, guide.Type);
    }

    [Fact]
    public void RetuningTheSelectedBand_MovesTheGuideWithIt()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);

        Retune(session, 1, 4_000);

        Assert.Equal(4_000.0, Assert.Single(Guides(EqWizardTestPlots.Draw(session, selectedBand: 1))).X);
    }

    [Fact]
    public void WithoutASelectionThereIsNoGuide()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);

        // This OxyPlot has no annotation Visible, so hidden means removed from the collection.
        Assert.Empty(Guides(EqWizardTestPlots.Draw(session, selectedBand: null)));
    }

    private static EqWizardSession ThreeBands()
    {
        var session = new EqWizardSession();
        session.Bank.SetCount(3);
        return session;
    }

    private static void Retune(EqWizardSession session, int index, double frequencyHz) =>
        session.Bank.Edit(index, session.Bank.Bands[index] with { FrequencyHz = frequencyHz });

    // The Auto Tune window's edges are vertical lines too; the guide is the one inside it.
    private static IReadOnlyList<LineAnnotation> Guides(PlotModel model) =>
        model.Annotations
            .OfType<LineAnnotation>()
            .Where(line => line.Type == LineAnnotationType.Vertical && line.LineStyle == LineStyle.Dot)
            .ToList();
}
