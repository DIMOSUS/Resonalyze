namespace Resonalyze;

/// <summary>
/// The menu the Target button drops, in both places that share one target (the EQ
/// Wizard and Virtual DSP). Two entries, because a target has two possible
/// shapes: the parametric one the settings dialog edits, and a curve imported
/// from a file. The tick says which one is being drawn.
/// </summary>
internal static class TargetCurveMenu
{
    public static ContextMenuStrip Build(
        ImportedTargetCurve? imported,
        Action openParametric,
        Action import)
    {
        ArgumentNullException.ThrowIfNull(openParametric);
        ArgumentNullException.ThrowIfNull(import);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(
            "Parametric shape…",
            null,
            (_, _) => openParametric())
        {
            Checked = imported == null,
            ToolTipText = ToolTipTextWrapper.Wrap(
                "A tilt, two shelves and a presence bump, from a preset or edited " +
                "by hand.")
        });

        menu.Items.Add(new ToolStripMenuItem(
            imported == null
                ? "Import from file…"
                : MenuText.Trim($"Imported: {imported.Name}"),
            null,
            (_, _) => import())
        {
            Checked = imported != null,
            ToolTipText = ToolTipTextWrapper.Wrap(imported == null
                ? "A house curve of your own: a text file of \"frequency level\" " +
                    "pairs, one per line — a REW target file, a curve exported from " +
                    "an overlay slot, or a column pair out of a spreadsheet."
                : $"{imported.Describe()}\r\nClick to import another file.")
        });

        return menu;
    }
}
