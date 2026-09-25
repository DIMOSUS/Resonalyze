using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What leaves the tool: the sum as a Captured FR overlay and the tuning sheet.</summary>
public partial class VirtualCrossoverPanel
{
    // Describes the sheet being printed, not the project, so it is session state.
    private PeqQConvention? sheetQConvention;

    private async Task CaptureSumToOverlayAsync()
    {
        VirtualCrossoverGroupView groupView = SelectedGroupView;
        VirtualCrossoverProcessedRender? render = await ProcessChannelsAsync();
        if (render == null)
        {
            return;
        }
        // The Sum as drawn: the group view decides which channels enter it, and a centre never does.
        var frame = VirtualCrossoverFrame.Of(render.Channels, groupView);
        if (OverlayCaptureRequested == null ||
            frame.ReadSum(metrics, session.MagnitudeGate.SmoothingInverseOctaves).Sum is not { } sumCurve)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        string title = "vDSP Sum " + string.Join(
            "+",
            frame.Summed.Select(item => item.Channel.Name));
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
