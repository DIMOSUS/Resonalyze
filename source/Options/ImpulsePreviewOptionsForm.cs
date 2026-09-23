using OxyPlot.WindowsForms;

namespace Resonalyze.Options;

/// <summary>Base of the panels with a live IR preview: draws what the session reads when it changes.</summary>
public class ImpulsePreviewOptionsForm : ModeSettingsForm
{
    private ImpulsePreviewInput? shownPreview;

    private protected virtual PlotView? PreviewView => null;

    private protected virtual ImpulsePreviewInput? PreviewInput => null;

    private protected override void OnPresented() => RenderPreview();

    /// <summary>Presents the session and redraws the preview whether or not what it reads changed.</summary>
    private protected void Redraw()
    {
        shownPreview = null;
        Present();
    }

    private void RenderPreview()
    {
        if (PreviewView is not { } view || PreviewInput is not { } input || input == shownPreview)
        {
            return;
        }

        shownPreview = input;
        switch (input)
        {
            case SampleWindowPreview window:
                ImpulseWindowPreview.Update(
                    view, window.Result, window.Window, window.Left, window.Right, window.Offset, window.Source);
                break;
            case GatePreview gated:
                ImpulseWindowPreview.UpdateGated(
                    view,
                    gated.Result,
                    gated.OffsetMs,
                    gated.LeftMs,
                    gated.PlateauMs,
                    gated.RightMs,
                    IrPreviewSource.Primary,
                    gated.Compare);
                break;
        }
    }
}
