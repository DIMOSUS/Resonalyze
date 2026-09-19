using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

namespace Resonalyze.App.Tests;

/// <summary>
/// The selected band is marked where it sits: by its handle, or where handles are hidden by a guide (a low-Q bell's
/// summit is guesswork, and a shelf has none).
/// </summary>
public sealed class EqWizardBandGuideTests
{
    [Fact]
    public void SelectingABand_MarksItsHandle_AndFillsItsOwnGainOnTheEqAxis()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250, gainDb: -6);

        PlotModel model = EqWizardTestPlots.Draw(session, selectedBand: 1);

        EqBandHandlesAnnotation handles = Handles(model);
        Assert.Equal(session.Bank.Bands, handles.Bands);
        Assert.Equal(1, handles.Selected);
        Assert.Equal([false, true, false], Enumerable.Range(0, 3).Select(handles.Raised));
        Assert.Contains(handles.FaintLayer, model.Annotations);
        Assert.Equal(AnnotationLayer.BelowSeries, handles.FaintLayer.Layer);
        Assert.Equal(AnnotationLayer.AboveSeries, handles.Layer);
        Assert.Empty(Guides(model));
        AreaSeries shape = Assert.Single(
            model.Series.OfType<AreaSeries>(),
            series => series.YAxisKey == EqWizardPlot.EqGainAxisKey);
        Assert.Equal(-6, shape.Points.Min(point => point.Y), 0.2);
        Assert.All(shape.Points2, point => Assert.Equal(0, point.Y));
    }

    [Theory]
    [InlineData("phase")]
    [InlineData("no EQ curve")]
    [InlineData("bypass")]
    public void WhereHandlesAreHidden_TheGuideMarksTheSelectedBand(string view)
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);
        Hide(session, view);

        PlotModel model = EqWizardTestPlots.Draw(session, selectedBand: 1);

        Assert.Empty(Handles(model).Bands);
        LineAnnotation guide = Assert.Single(Guides(model));
        Assert.Equal(1_250.0, guide.X);
        Assert.Equal(LineAnnotationType.Vertical, guide.Type);
    }

    [Fact]
    public void RetuningTheSelectedBand_MovesItsMarkWithIt()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);

        Retune(session, 1, 4_000);

        Assert.Equal(4_000.0, Handles(EqWizardTestPlots.Draw(session, selectedBand: 1)).Bands[1].FrequencyHz);
        Hide(session, "no EQ curve");
        Assert.Equal(4_000.0, Assert.Single(Guides(EqWizardTestPlots.Draw(session, selectedBand: 1))).X);
    }

    [Fact]
    public void WithoutASelectionThereIsNoGuide()
    {
        EqWizardSession session = ThreeBands();
        Retune(session, 1, 1_250);
        Hide(session, "no EQ curve");

        // This OxyPlot has no annotation Visible, so hidden means removed from the collection.
        Assert.Empty(Guides(EqWizardTestPlots.Draw(session, selectedBand: null)));
    }

    private static EqWizardSession ThreeBands()
    {
        var session = new EqWizardSession();
        session.Bank.SetCount(3);
        return session;
    }

    private static void Retune(EqWizardSession session, int index, double frequencyHz, double gainDb = 0) =>
        session.Bank.Edit(index, session.Bank.Bands[index] with { FrequencyHz = frequencyHz, GainDb = gainDb });

    private static void Hide(EqWizardSession session, string view)
    {
        switch (view)
        {
            case "phase":
                session.SetPhaseMode(true);
                break;
            case "no EQ curve":
                session.SetShowEqCurve(false);
                break;
            default:
                session.SetBypass(true);
                break;
        }
    }

    private static EqBandHandlesAnnotation Handles(PlotModel model) =>
        model.Annotations.OfType<EqBandHandlesAnnotation>().Single();

    // The Auto Tune window's edges are vertical lines too; the guide is the one inside it.
    private static IReadOnlyList<LineAnnotation> Guides(PlotModel model) =>
        model.Annotations
            .OfType<LineAnnotation>()
            .Where(line => line.Type == LineAnnotationType.Vertical && line.LineStyle == LineStyle.Dot)
            .ToList();
}
