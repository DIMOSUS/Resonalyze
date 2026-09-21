using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
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

    [Fact]
    public void TheTargetPreview_ShowsItsZoomButtons()
    {
        using OverlayTargetSettingsDialog dialog = TargetDialog();
        PlotView preview = PlotViews(dialog).Single();

        Render(preview);

        Assert.True(
            PlotZoomButtons.Layout(preview.Model!).Count == 4,
            $"plot area {preview.Model!.PlotArea.Width:0} x {preview.Model.PlotArea.Height:0}");
    }

    [Fact]
    public void ATargetPreviewZoom_SurvivesAnEdit()
    {
        using OverlayTargetSettingsDialog dialog = TargetDialog();
        PlotView preview = PlotViews(dialog).Single();
        Render(preview);
        preview.Model!.Axes.Single(axis => axis.IsHorizontal()).Zoom(100, 1_000);

        ThemedNumericUpDown bass = (ThemedNumericUpDown)typeof(OverlayTargetSettingsDialog)
            .GetField("bassGainInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
        PlotModel before = preview.Model;
        bass.Value += 0.5m;
        Render(preview);

        Assert.NotSame(before, preview.Model);
        Axis frequency = preview.Model!.Axes.Single(axis => axis.IsHorizontal());
        Assert.Equal(100, frequency.ActualMinimum, 1);
        Assert.Equal(1_000, frequency.ActualMaximum, 1);
    }

    private static void Render(PlotView preview)
    {
        var exporter = new PngExporter { Width = preview.Width, Height = preview.Height };
        using var stream = new MemoryStream();
        exporter.Export(preview.Model, stream);
    }

    private static OverlayTargetSettingsDialog TargetDialog() => new(
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
        isolatedTarget: true);

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
        nameof(OverlayTargetSettingsDialog) => TargetDialog(),
        _ => throw new ArgumentOutOfRangeException(nameof(dialog), dialog, "No factory for this dialog.")
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
