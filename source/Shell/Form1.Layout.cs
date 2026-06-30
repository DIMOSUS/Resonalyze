namespace Resonalyze;

public partial class Form1
{
    private const int MainContentMargin = 12;

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        ApplyMainContentLayout();
    }

    private void ApplyMainContentLayout()
    {
        if (plotView1 == null || inputLevelMeterPanel == null)
        {
            return;
        }

        int margin = ScaleLayout(MainContentMargin);
        int top = ScaleLayout(ChromeTitleBar.BarHeight) + margin;
        int rightEdge = GetCentralContentRightEdge(margin);
        int width = Math.Max(1, rightEdge - margin);
        int height = Math.Max(1, ClientSize.Height - top - margin);
        var bounds = new Rectangle(margin, top, width, height);

        plotView1.Bounds = bounds;
        timeAlignmentController?.SetLayoutBounds(bounds);
        eqWizardPanel.Bounds = bounds;
        irComparerPanel.Bounds = bounds;
    }

    private int GetCentralContentRightEdge(int margin)
    {
        int rightPanelLeft = ClientSize.Width - ScaleLayout(162);
        foreach (Control control in GetRightSideControls())
        {
            if (!control.IsDisposed)
            {
                rightPanelLeft = Math.Min(rightPanelLeft, control.Left);
            }
        }

        int minimumContentWidth = ScaleLayout(480);
        return Math.Max(margin + minimumContentWidth, rightPanelLeft - margin);
    }

    private Control[] GetRightSideControls() =>
    [
        inputLevelMeterPanel,
        panel1,
        buttonCurrentModeSettings,
        buttonHistory,
        buttonOverlayShowAll,
        buttonOverlayHideAll,
        overlays
    ];

    private int ScaleLayout(int value) =>
        Math.Max(1, (int)Math.Round(value * DeviceDpi / 96.0));
}
