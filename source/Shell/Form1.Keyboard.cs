namespace Resonalyze;

public partial class Form1
{
    // Plain keys, so never while a field holds the caret: typing an L into a field must stay an L.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ModeCatalog.For(modeController.ActiveTab).ShowsVirtualCrossoverPanel &&
            !KeyboardFocus.IsTyping(this) &&
            virtualCrossoverPanel.HandleSideKey(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
