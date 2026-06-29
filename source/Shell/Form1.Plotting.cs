using OxyPlot;
using OxyPlot.Annotations;

namespace Resonalyze;

public partial class Form1
{
    private void DrawSelectedMode(bool includeCurves)
    {
        ModeDescriptor descriptor = GetActiveModeDescriptor();
        if (descriptor.CreatePlotModel == null)
        {
            if (descriptor.ShowsTimeAlignmentPanel)
            {
                timeAlignmentController.RefreshConfiguration();
            }
            return;
        }

        if (descriptor.Mode == Mode.LiveSpectrum)
        {
            // While running, the live timer owns the plot. When idle, restore the
            // last curve, peak hold and overlays instead of an empty plot.
            if (!liveSpectrumController.InProgress)
            {
                liveSpectrumController.RestoreLastCurve();
            }
            return;
        }

        bool shouldIncludeCurves = includeCurves && descriptor.SupportsCurveDrawing;
        ShowPlotModel(
            descriptor.CreatePlotModel(shouldIncludeCurves),
            shouldIncludeCurves,
            descriptor.ShowOverlayCurves);
    }

    private void ShowPlotModel(
        PlotModel model,
        bool includeCurves,
        bool showOverlay)
    {
        plotView1.Model = model;
        UpdatePeakInfo();

        if (includeCurves && showOverlay)
        {
            overlayCollection.Show(CurrentMode);
        }
        else
        {
            UpdatePlotLabelsPanel();
        }
    }

    private void UpdateOverlayAvailability()
    {
        bool available = OverlaysAvailableForCurrentMode();
        overlays.Enabled = available;
        RefreshOverlayButtons();
        if (!available)
        {
            overlayCollection.HideAll();
        }
    }

    private bool OverlaysAvailableForCurrentMode() =>
        OverlayCollection.SupportsMode(CurrentMode);

    // The bulk overlay buttons act only when the current mode actually has
    // populated overlay slots to show or hide.
    private void RefreshOverlayButtons()
    {
        bool hasOverlays = OverlaysAvailableForCurrentMode() &&
            overlayCollection.HasOverlays(CurrentMode);
        buttonOverlayShowAll.Enabled = hasOverlays;
        buttonOverlayHideAll.Enabled = hasOverlays;
    }

    private void buttonOverlayShowAll_Click(object? sender, EventArgs e)
    {
        overlayCollection.Show(CurrentMode);
        UpdatePlotLabelsPanel();
    }

    private void buttonOverlayHideAll_Click(object? sender, EventArgs e)
    {
        overlayCollection.HideAll();
        UpdatePlotLabelsPanel();
    }

    private Task SelectModeAsync(ModeTab tab) => modeController.SelectAsync(tab);

    private Dictionary<ModeTab, Action> CreateModeTabActions() =>
        new()
        {
            [ModeTab.Impulse] = () => _ = SelectModeAsync(ModeTab.Impulse),
            [ModeTab.Frequency] = () => _ = SelectModeAsync(ModeTab.Frequency),
            [ModeTab.Phase] = () => _ = SelectModeAsync(ModeTab.Phase),
            [ModeTab.GroupDelay] = () => _ = SelectModeAsync(ModeTab.GroupDelay),
            [ModeTab.Waterfall] = () => _ = SelectModeAsync(ModeTab.Waterfall),
            [ModeTab.Burst] = () => _ = SelectModeAsync(ModeTab.Burst),
            [ModeTab.LiveSpectrum] = () => _ = SelectModeAsync(ModeTab.LiveSpectrum),
            [ModeTab.Autocorrelation] = () => _ = SelectModeAsync(ModeTab.Autocorrelation),
            [ModeTab.TimeAlignment] = () => _ = SelectModeAsync(ModeTab.TimeAlignment)
        };

    private void SetActiveModeTab(ModeTab activeTab)
    {
        ModeDescriptor descriptor = GetModeDescriptor(activeTab);
        chromeTitleBar.SetActiveModeTab(activeTab);
        UpdateCurrentModeSettingsButton();
        UpdateRecordButtonForCurrentMode();
        ApplyMainContentLayout();
        plotView1.Visible = descriptor.HasPlotView;
        overlays.Visible = descriptor.HasOverlayPanel;
        buttonOverlayShowAll.Visible = descriptor.HasOverlayPanel;
        buttonOverlayHideAll.Visible = descriptor.HasOverlayPanel;
        timeAlignmentController.SetVisible(descriptor.ShowsTimeAlignmentPanel);
        SyncDockedModeSettingsOnModeChange();
        UpdatePlotLabelsPanel();
    }

    private void UpdateRecordButtonForCurrentMode()
    {
        if (modeController.ActiveTab != ModeTab.LiveSpectrum)
        {
            return;
        }

        buttonRecord.Text = liveSpectrumController.InProgress ? "Stop" : "Start";
    }

    private void UpdatePlotLabelsPanel()
    {
        plotLabelsPanelController.Refresh();
        RefreshOverlayButtons();
    }

    private void UpdatePeakInfo()
    {
        PlotModel? model = plotView1.Model;
        if (model == null)
        {
            return;
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (model.Annotations[index] is OverlayTextAnnotation
                {
                    Tag: PeakInfoAnnotationTag
                })
            {
                model.Annotations.RemoveAt(index);
            }
        }

        if (modeController.ActiveTab is not (ModeTab.Phase or ModeTab.GroupDelay))
        {
            model.InvalidatePlot(false);
            return;
        }

        string transferPeak;
        if (expSweepMeasurement.TransferImpulseResponse == null)
        {
            transferPeak = "--";
        }
        else
        {
            int peakSamples = expSweepMeasurement.TransferPeakIndex;
            double peakMs = expSweepMeasurement.SampleRate > 0
                ? peakSamples * 1000.0 / expSweepMeasurement.SampleRate
                : 0;
            transferPeak = $"{peakMs:0.000} ms ({peakSamples} samples)";
        }
        string text = expSweepMeasurement.InProgress
            ? "Peaks: measuring..."
            : "Transfer IR Peak: " + transferPeak;
        model.Annotations.Add(new OverlayTextAnnotation
        {
            Tag = PeakInfoAnnotationTag,
            Text = text,
            TextPosition = new DataPoint(0.01, 0),
            TextFlowDirection = TextFlowDirection.TopDown,
            FontSize = 12,
            FontWeight = 700,
            TextColor = OxyColors.White,
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
        });
        model.InvalidatePlot(false);
    }

    private bool CanDrawCurrentMeasurement() =>
        hasCurrentImpulseResponse && !expSweepMeasurement.InProgress;
}
