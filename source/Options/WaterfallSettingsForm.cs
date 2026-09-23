using OxyPlot.WindowsForms;

namespace Resonalyze.Options;

/// <summary>Base of the Waterfall and Burst Decay panels: binds the fields they share to a
/// <see cref="WaterfallSettingsSession"/>.</summary>
public class WaterfallSettingsForm : ImpulsePreviewOptionsForm
{
    private WaterfallSettingsSession session = WaterfallSettingsSession.ForWaterfall();
    private ThemedNumericUpDown? sampleRate;
    private ThemedNumericUpDown? window;
    private ThemedNumericUpDown? left;
    private ThemedNumericUpDown? right;
    private ThemedNumericUpDown? captureTime;
    private ThemedNumericUpDown? dbRange;
    private ThemedComboBox? smoothing;
    private ThemedNumericUpDown? offset;
    private PlotView? preview;

    private protected WaterfallSettingsSession Session => session;

    private protected void BindWaterfall(
        WaterfallSettingsSession waterfallSession,
        ThemedNumericUpDown sampleRateField,
        ThemedNumericUpDown windowField,
        ThemedNumericUpDown leftFade,
        ThemedNumericUpDown rightFade,
        ThemedNumericUpDown captureTimeField,
        ThemedNumericUpDown dbRangeField,
        ThemedComboBox smoothingList,
        ThemedNumericUpDown offsetField,
        PlotView previewView)
    {
        session = waterfallSession;
        (sampleRate, window, left, right, captureTime) = (sampleRateField, windowField, leftFade, rightFade, captureTimeField);
        (dbRange, smoothing, offset, preview) = (dbRangeField, smoothingList, offsetField, previewView);
        PlotInteraction.Enable(previewView);
        sampleRateField.ApplyFieldRange(ModeSettingsLimits.SampleRate);
        windowField.ApplyFieldRange(ModeSettingsLimits.WaterfallWindow);
        leftFade.ApplyFieldRange(ModeSettingsLimits.TukeyFade);
        rightFade.ApplyFieldRange(ModeSettingsLimits.TukeyFade);
        captureTimeField.ApplyFieldRange(ModeSettingsLimits.CaptureTimeMs);
        dbRangeField.ApplyFieldRange(ModeSettingsLimits.DbRange);
        offsetField.ApplyFieldRange(ModeSettingsLimits.SampleOffset);
        // Width presets only for burst decay: it has no magnitude grid, so psychoacoustic would silently alias 1/6.
        smoothingList.FillSmoothingPresets(includePsychoacoustic: !session.IsBurstDecay);
        Bind(windowField, value => session.SetWindow((int)value));
        Bind(leftFade, value => session.Fades.SetLeft((int)value));
        Bind(rightFade, value => session.Fades.SetRight((int)value));
        Bind(dbRangeField, value => session.DbRange = (int)value);
        BindItem<int>(smoothingList, value => session.SmoothingInverseOctaves = value);
        Bind(offsetField, value => session.Offset = (int)value);
    }

    private protected void InitWaterfall(AnalyzerDocument document, int configuredSampleRate, WaterfallGenerateOptions options)
    {
        Follow(document, configuredSampleRate);
        session.Follow(OpenMeasurement);
        session.Load(options);
        Redraw();
    }

    private protected void WriteWaterfall(WaterfallGenerateOptions options)
    {
        session.WriteTo(options);
        Redraw();
    }

    private protected override void OnMeasurementChanged()
    {
        session.Follow(OpenMeasurement);
        Redraw();
    }

    private protected override PlotView? PreviewView => preview;

    private protected override ImpulsePreviewInput PreviewInput => session.Preview;

    private protected override void PresentControls()
    {
        if (sampleRate == null || window == null || left == null || right == null || captureTime == null ||
            dbRange == null || smoothing == null || offset == null)
        {
            return;
        }

        Show(sampleRate, session.SampleRateShown);
        Show(window, session.Fades.Window);
        Show(left, session.Fades.Left, session.Fades.LeftMaximum);
        Show(right, session.Fades.Right, session.Fades.RightMaximum);
        Show(captureTime, session.CaptureTimeMs);
        Show(dbRange, session.DbRange);
        ShowItem(smoothing, session.SmoothingInverseOctaves);
        Show(offset, session.Offset);
    }
}
