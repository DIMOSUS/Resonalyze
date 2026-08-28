using System.Reflection;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The vertical guide the wizard draws at the selected band's frequency. The band
/// curve beside it answers what the filter DOES; this answers where it sits, which
/// a curve is bad at — a low-Q bell is a shape an octave wide, and a shelf or an
/// all-pass has no summit to read a centre off at all.
/// </summary>
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

        // This OxyPlot has no Visible on an annotation, so being off the plot is
        // being out of the collection — the Auto Tune range guides stay.
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

    // Through the control the user types into, so the edit travels the path a
    // typed frequency travels: the strip raises its change, the bank redraws.
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
