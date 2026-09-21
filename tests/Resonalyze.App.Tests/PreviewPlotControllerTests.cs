using System.Windows.Forms;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze.App.Tests;

public sealed class PreviewPlotControllerTests
{
    public static TheoryData<string> Dialogs => new()
    {
        nameof(FROptions),
        nameof(GDOpt),
        nameof(PROpt),
        nameof(BDOpt),
        nameof(WaterfallOptions),
        nameof(VirtualCrossoverGateDialog),
        nameof(MeasurementHistoryWindow),
        nameof(AngleCalibrationDialog),
        nameof(OverlayTargetSettingsDialog)
    };

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void EveryPreviewPlot_RunsOnTheSharedGestureController(string dialog)
    {
        using Form form = Create(dialog);

        List<PlotView> plots = PlotViews(form).ToList();

        Assert.NotEmpty(plots);
        Assert.All(plots, plot => Assert.IsType<PlotGestureController>(plot.Controller));
    }

    private static Form Create(string dialog) => dialog switch
    {
        nameof(FROptions) => new FROptions(),
        nameof(GDOpt) => new GDOpt(),
        nameof(PROpt) => new PROpt(),
        nameof(BDOpt) => new BDOpt(),
        nameof(WaterfallOptions) => new WaterfallOptions(),
        nameof(VirtualCrossoverGateDialog) => new VirtualCrossoverGateDialog(),
        nameof(MeasurementHistoryWindow) => new MeasurementHistoryWindow(),
        nameof(AngleCalibrationDialog) => new AngleCalibrationDialog(
            new MicrophoneCalibrationDefinition { Id = "cal1", Name = "90", Kind = MicrophoneCalibrationKind.Angle },
            []),
        _ => new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            TargetPreset.Flat,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            3.0,
            TargetDeviationMode.Deviation,
            System.Drawing.Color.White,
            2.0,
            OverlayLineStyle.Solid,
            100,
            0,
            [],
            null,
            isolatedTarget: true)
    };

    private static IEnumerable<PlotView> PlotViews(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is PlotView plot)
            {
                yield return plot;
            }

            foreach (PlotView nested in PlotViews(child))
            {
                yield return nested;
            }
        }
    }
}
