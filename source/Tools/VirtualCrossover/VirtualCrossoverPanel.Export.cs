using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What leaves the tool: a curve of the upper plot as a Captured FR overlay, and the tuning sheet.</summary>
public partial class VirtualCrossoverPanel
{
    // Describes the sheet being printed, not the project, so it is session state.
    private PeqQConvention? sheetQConvention;
    private string? overlayCurveKey;

    private async Task CaptureToOverlayAsync()
    {
        List<VirtualCrossoverOverlayCurve>? curves = await ReadOverlayCurvesAsync();
        if (OverlaySlots == null || curves is not { Count: > 0 })
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        using var dialog = new VirtualCrossoverOverlayExportDialog(
            curves, OverlaySlots.ReadFrequencyResponseOverlaySlots(), overlayCurveKey);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.SelectedCurve is not { } curve ||
            dialog.SelectedSlot is not { } slot)
        {
            return;
        }

        try
        {
            OverlaySlots.SaveFrequencyResponseOverlay(
                slot,
                curve.Title,
                [.. curve.Points.Select(point => new OverlayPoint(point.X, point.Y))],
                curve.SmoothingCode);
            overlayCurveKey = curve.Key;
        }
        catch (Exception exception)
        {
            ShowError("The overlay could not be saved.", exception.Message);
        }
    }

    // A redraw requested meanwhile (an edit, a FIR headroom read landing) drops the other side's curves from a read; read
    // again instead of offering a partial list. Null when edits keep coming.
    private async Task<List<VirtualCrossoverOverlayCurve>?> ReadOverlayCurvesAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            VirtualCrossoverViewState view = CaptureViewState();
            if (await ProcessChannelsAsync() is not { } render)
            {
                continue;
            }

            List<VirtualCrossoverOverlayCurve> curves =
                await overlayExport.CurvesAsync(render.Channels, render.Revision, view);
            if (processingCoordinator.IsCurrent(render.Revision))
            {
                return curves;
            }
        }

        return null;
    }

    private async Task ExportTuningSheetAsync()
    {
        PeqQConvention? qConvention = AskSheetQConvention();
        if (qConvention == null)
        {
            return;
        }

        (string? folder, string? sessionFile) = session.Project.SaveDialogStart();
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "pdf",
            // The stem alone: the dialog adds the extension of whichever format is chosen.
            FileName = Path.GetFileNameWithoutExtension(sessionFile) is { Length: > 0 } stem
                ? stem
                : "virtual-dsp",
            InitialDirectory = folder ?? string.Empty,
            Filter = "Tuning sheet (PDF) (*.pdf)|*.pdf|Tuning sheet (text) (*.txt)|*.txt",
            Title = "Export Virtual DSP tuning sheet"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        VirtualCrossoverGroupView groupView = SelectedGroupView;
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        string metricLine = VirtualCrossoverMetric.FormatLabel(
            VirtualCrossoverFrame.Of(render.Channels, groupView)
                .ReadSum(metrics, session.MagnitudeGate.SmoothingInverseOctaves).Entries);
        // The chain graph shows the filters, so it uses the processor's rate, not the measurement rate.
        int sampleRate = session.ProcessorSampleRateHz;
        try
        {
            if (dialog.FilterIndex == 1)
            {
                VirtualCrossoverSheetPdf.Export(
                    dialog.FileName, session.Project, metricLine, sampleRate, qConvention.Value);
            }
            else
            {
                AtomicFile.WriteAllText(
                    dialog.FileName,
                    VirtualCrossoverSheet.FormatText(
                        session.Project, metricLine, qConvention.Value));
            }

            // Remember the answer only once a sheet reached the disk.
            sheetQConvention = qConvention;
        }
        catch (Exception exception)
        {
            ShowError("The tuning sheet could not be exported.", exception.Message);
        }
    }

    // A known model states its Q convention; a Custom profile asks, pre-selected with the last exported answer.
    // Null when cancelled.
    private PeqQConvention? AskSheetQConvention()
    {
        DspProcessorProfile profile = session.ProcessorProfile;
        if (!profile.IsCustom)
        {
            return profile.QConvention;
        }

        using var dialog = new TuningSheetQConventionDialog(
            sheetQConvention ?? profile.QConvention);
        return dialog.ShowDialog(FindForm()) == DialogResult.OK
            ? dialog.SelectedConvention
            : null;
    }
}
