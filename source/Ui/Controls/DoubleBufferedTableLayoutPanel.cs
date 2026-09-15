namespace Resonalyze;

/// <summary>DoubleBuffered is protected; live-reassigned grids (PEQ strips while dragging) flicker without it.</summary>
internal sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
{
    public DoubleBufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
    }
}
