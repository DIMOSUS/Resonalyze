using System.Reflection;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

public sealed class EqWizardBandGuideTests
{
    [Fact]
    public void SelectingABand_PutsTheGuideOnItsFrequency()
    {
        using var panel = new EqWizardPanel();
        SetBandCount(panel, 3);
        object slot = Slots(panel)[1];
        SetFrequency(slot, 1_250m);

        SelectSlot(panel, slot);

        LineAnnotation guide = Guide(panel);
        Assert.Contains(guide, Model(panel).Annotations);
        Assert.Equal(1_250.0, guide.X);
        Assert.Equal(LineAnnotationType.Vertical, guide.Type);
    }

    [Fact]
    public void RetuningTheSelectedBand_MovesTheGuideWithIt()
    {
        using var panel = new EqWizardPanel();
        SetBandCount(panel, 3);
        object slot = Slots(panel)[1];
        SetFrequency(slot, 1_250m);
        SelectSlot(panel, slot);

        SetFrequency(slot, 4_000m);

        Assert.Equal(4_000.0, Guide(panel).X);
    }

    [Fact]
    public void DeselectingTakesTheGuideOffThePlot()
    {
        using var panel = new EqWizardPanel();
        SetBandCount(panel, 3);
        object slot = Slots(panel)[1];
        SetFrequency(slot, 1_250m);
        SelectSlot(panel, slot);

        Invoke(panel, "DeselectBand");

        // This OxyPlot has no annotation Visible, so hidden means removed from the collection.
        Assert.DoesNotContain(Guide(panel), Model(panel).Annotations);
    }

    private static LineAnnotation Guide(EqWizardPanel panel) =>
        Field<LineAnnotation>(panel, "bandMarker");

    private static PlotModel Model(EqWizardPanel panel) =>
        Field<PlotView>(panel, "plotWizard").Model!;

    private static IReadOnlyList<PeqSlotControl> Slots(EqWizardPanel panel) =>
        Field<List<PeqSlotControl>>(panel, "peqSlots");

    private static void SetBandCount(EqWizardPanel panel, int count) =>
        Invoke(panel, "SetBandCount", count);

    private static void SelectSlot(EqWizardPanel panel, object slot) =>
        Invoke(panel, "SelectSlot", slot);

    private static void SetFrequency(object slot, decimal frequencyHz) =>
        ((PeqSlotControl)slot).FrequencyInput.Value = frequencyHz;

    private static void Invoke(EqWizardPanel panel, string name, params object[] arguments) =>
        typeof(EqWizardPanel)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, arguments);

    private static T Field<T>(EqWizardPanel panel, string name) =>
        (T)typeof(EqWizardPanel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
}
