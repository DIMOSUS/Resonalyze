using System.Reflection;
using OxyPlot.WindowsForms;

namespace Resonalyze.App.Tests;

/// <summary>The large plots draw on the GPU and follow the one setting; a Designer re-serialization that put a stock
/// PlotView back would silently return a tool to GDI+ (the EQ Wizard's band drag then stutters).</summary>
public sealed class PlotAccelerationCoverageTests
{
    [Theory]
    [InlineData(typeof(Form1), "plotView1")]
    [InlineData(typeof(EqWizardPanel), "plotWizard")]
    [InlineData(typeof(VirtualCrossoverPanel), "mainPlotView")]
    public void TheLargePlotsAreAccelerated(Type owner, string field)
    {
        FieldInfo plot = owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Equal(typeof(AcceleratedPlotView), plot.FieldType);
    }

    [Fact]
    public void TheToolsHandTheSettingToTheirPlot() => StaTest.Run(() =>
    {
        using var wizard = new EqWizardPanel();
        using var dsp = new VirtualCrossoverPanel();
        var wizardPlot = (AcceleratedPlotView)typeof(EqWizardPanel)
            .GetField("plotWizard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wizard)!;
        var dspPlot = (AcceleratedPlotView)typeof(VirtualCrossoverPanel)
            .GetField("mainPlotView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dsp)!;

        wizard.GpuAllowed = false;
        dsp.GpuAllowed = false;

        Assert.False(wizardPlot.GpuAllowed);
        Assert.False(dspPlot.GpuAllowed);
        Assert.IsAssignableFrom<PlotView>(wizardPlot);
    });
}
