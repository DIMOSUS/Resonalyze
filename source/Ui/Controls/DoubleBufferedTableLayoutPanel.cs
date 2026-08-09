namespace Resonalyze;

/// <summary>
/// A <see cref="TableLayoutPanel"/> that paints double-buffered. The stock panel
/// does not, and <see cref="Control.DoubleBuffered"/> is protected, so a grid
/// whose cells are re-assigned live (the PEQ strips during a drag) flickers
/// without this subclass.
/// </summary>
internal sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
{
    public DoubleBufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
    }
}
