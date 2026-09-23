using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What leaves the tool: the sum as a Captured FR overlay and the tuning sheet.</summary>
public partial class VirtualCrossoverPanel
{
    // Describes the sheet being printed, not the project, so it is session state.
    private PeqQConvention? sheetQConvention;

    private async Task CaptureSumToOverlayAsync()
    {
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> processed = render.Channels;
        if (processed.Count < 2 || OverlayCaptureRequested == null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        int overlayAnchor = processed.Min(item => item.PeakIndex);
        MagnitudeGateSnapshot overlayGate = session.MagnitudeGate;
        AnalysisCurve sumCurve = overlayGate.MeasuredSum(
            processed,
            overlayAnchor,
            overlayGate.ResolveGateOffsetMs(
                oppositeSide: false, overlayAnchor, processed[0].SampleRate),
            session.Calibration.For).Display;

        string title = "vDSP Sum " + string.Join(
            "+",
            processed.Select(item => item.Channel.Name));
        OverlayPoint[] points = sumCurve.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();

        int? slot = OverlayCaptureRequested(title, points);
        if (slot.HasValue)
        {
            MessageBox.Show(
                FindForm(),
                $"The virtual sum was saved as overlay slot {slot.Value} in " +
                "Frequency Response.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        else
        {
            ShowError(
                "No free overlay slot.",
                "All twelve Frequency Response overlay slots are occupied; " +
                "clear one and try again.");
        }
    }

    private async Task ExportTuningSheetAsync()
    {
        PeqQConvention? qConvention = AskSheetQConvention();
        if (qConvention == null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "pdf",
            FileName = "virtual-dsp",
            Filter = "Tuning sheet (PDF) (*.pdf)|*.pdf|Tuning sheet (text) (*.txt)|*.txt",
            Title = "Export Virtual DSP tuning sheet"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        List<ProcessedChannel> metricChannels = render.Channels;
        (_, _, List<SignalPoint>? metricLoss) =
            metrics.BuildCurves(metricChannels, session.MagnitudeGate.SmoothingInverseOctaves);
        string metricLine = VirtualCrossoverMetric.FormatLabel(
            metrics.BuildEntries(metricChannels, metricLoss));
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
