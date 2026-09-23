using Resonalyze.Dsp;

namespace Resonalyze;

public partial class FirConstructorPanel
{
    private void LayoutPlots()
    {
        int gap = plotImpulse.Top - plotResponse.Bottom;
        int available = ClientSize.Height - plotResponse.Top * 2 - gap;
        if (available < 2)
        {
            return;
        }

        plotResponse.Height = available / 2;
        plotImpulse.Top = plotResponse.Bottom + gap;
        plotImpulse.Height = available - available / 2;
    }

    // Before is the kernel shown until now: a new one refits the impulse view, the same one redrawn keeps the zoom.
    private void ApplyRendering(FirFilter? before)
    {
        FirConstructorRendering? rendering = session.Rendering;
        magnitudeSeries.Points.Clear();
        targetSeries.Points.Clear();
        phaseSeries.Points.Clear();
        if (rendering != null)
        {
            magnitudeSeries.Points.AddRange(rendering.Magnitude);
            targetSeries.Points.AddRange(rendering.Target);
            phaseSeries.Points.AddRange(rendering.Phase);
        }

        labelLatency.Text = FirConstructorReadout.Latency(session);
        labelDeviation.Text = FirConstructorReadout.Deviation(session);
        UpdateActions();
        responseModel.InvalidatePlot(true);
        ApplyImpulse(rendering, rescale: !ReferenceEquals(before, rendering?.Kernel));
    }

    // A new kernel or scale refits the view; the same kernel redrawn keeps the user's zoom.
    private void ApplyImpulse(FirConstructorRendering? rendering, bool rescale)
    {
        impulseSeries.Points.Clear();
        bool decibels = checkBoxImpulseDb.Checked;
        if (rendering != null)
        {
            impulseSeries.Points.AddRange(decibels ? rendering.ImpulseDb : rendering.Impulse);
        }

        amplitudeAxis.Title = decibels ? "dB" : "Amplitude";
        if (rescale)
        {
            impulseModel.ResetAllAxes();
        }

        impulseModel.InvalidatePlot(true);
    }
}
