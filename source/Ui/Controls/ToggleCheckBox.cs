using System.ComponentModel;

namespace Resonalyze;

/// <summary>A flat button-style check box whose frame and fill say whether it is on: lit (accent fill and frame) only
/// while ticked and in force, a faint frame otherwise. The frame no longer follows the text, so a coloured label
/// (a channel's curve, the Hybrid reminder) does not light an unticked toggle.</summary>
public sealed class ToggleCheckBox : ReleaseClickCheckBox
{
    private bool muted;

    public ToggleCheckBox()
    {
        PaintState();
    }

    /// <summary>Keeps the tick, ignores the user's toggling (AutoCheck), leaves the tab order and drops the lit look: the
    /// toggle does not apply here. Click still fires, so handlers belong on CheckedChanged.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Muted
    {
        get => muted;
        set
        {
            muted = value;
            AutoCheck = !value;
            TabStop = !value;
            PaintState();
        }
    }

    private bool Lit => Checked && Enabled && !muted;

    protected override void OnCheckedChanged(EventArgs e)
    {
        PaintState();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        PaintState();
        base.OnEnabledChanged(e);
    }

    private void PaintState()
    {
        FlatAppearance.CheckedBackColor = Lit ? UiPalette.ToggleCheckedFill : UiPalette.ToggleCheckedFillMuted;
        FlatAppearance.BorderColor = Lit ? UiPalette.AccentMark : UiPalette.BorderMuted;
        // WinForms blends a hovered ticked fill toward the unticked one, which reads as off right after the click.
        FlatAppearance.MouseOverBackColor = Checked ? FlatAppearance.CheckedBackColor : Color.Empty;
    }
}
