using Button = System.Windows.Forms.Button;
using CheckBox = System.Windows.Forms.CheckBox;

namespace Resonalyze;

/// <summary>One overlay slot's controls: shows the session's slot and hands the user's edits back to the session.</summary>
internal sealed partial class OverlaySlotView
{
    private readonly OverlayPanel owner;
    private readonly OverlaySlot slot;
    private readonly Panel panel;
    private readonly Button captureButton;
    private readonly ThemedNumericUpDown offsetControl;
    private readonly CheckBox checkBox;
    private readonly Label nameLabel;
    private readonly WrappingToolTip toolTip;
    private readonly System.Windows.Forms.Timer offsetSaveTimer;
    private bool presenting;
    private string? presentedTitle;

    public OverlaySlotView(
        OverlayPanel owner,
        OverlaySlot slot,
        Panel panel,
        Button captureButton,
        ThemedNumericUpDown offsetControl,
        CheckBox checkBox,
        Label nameLabel,
        WrappingToolTip toolTip)
    {
        this.owner = owner;
        this.slot = slot;
        this.panel = panel;
        this.captureButton = captureButton;
        this.offsetControl = offsetControl;
        this.checkBox = checkBox;
        this.nameLabel = nameLabel;
        this.toolTip = toolTip;

        captureMenu = BuildCaptureMenu(
            out captureCurveMenuItem,
            out exportDeviationMenuItem,
            out targetMenuItem,
            out settingsMenuItem,
            out clearSlotMenuItem);

        // Long press (>0.5 s) opens settings; a click opens the capture menu.
        longPressTimer = new System.Windows.Forms.Timer { Interval = 500 };
        longPressTimer.Tick += LongPressTimerTick;

        offsetSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        offsetSaveTimer.Tick += OffsetSaveTimerTick;

        toolTip.SetToolTip(offsetControl, "Overlay vertical offset (dB)");
        toolTip.SetToolTip(checkBox, "Show / hide this overlay");
        toolTip.SetToolTip(
            captureButton,
            "Click for the overlay menu; hold to open this slot's settings");

        checkBox.CheckedChanged += CheckBoxChanged;
        captureButton.Click += (_, _) => OpenCaptureMenu();
        captureButton.MouseDown += CaptureButtonMouseDown;
        captureButton.MouseUp += CaptureButtonMouseUp;
        offsetControl.ValueChanged += OffsetValueChanged;

        Present();
    }

    private OverlaySession Session => owner.Session;

    public void Present()
    {
        OverlaySlotState state = slot.State;
        presenting = true;
        try
        {
            checkBox.Checked = slot.Checked;
            checkBox.Enabled = slot.CheckEnabled;
            offsetControl.Enabled = slot.OffsetEnabled;
            // Assigning rewrites the field's text even at an equal value, which would drop a half-typed offset.
            if (offsetControl.Value != state.Offset)
            {
                offsetControl.Value = state.Offset;
            }

            SetPanelColor(state.Appearance.Color);
            nameLabel.Text = OverlaySlotName.Shorten(state.Title, slot.Index);
            if (presentedTitle != state.Title)
            {
                presentedTitle = state.Title;
                toolTip.SetToolTip(nameLabel, state.Title.Length > 0 ? state.Title : "Empty overlay slot");
            }

            captureButton.Text = state.Kind switch
            {
                OverlayKind.Operation => $"{slot.Index}ƒ",
                OverlayKind.Target => $"{slot.Index}△",
                _ => $"{slot.Index}"
            };
        }
        finally
        {
            presenting = false;
        }
    }

    // Name drawn in black or white, whichever stays legible on the user-editable slot colour.
    private void SetPanelColor(Color color)
    {
        panel.BackColor = color;
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        nameLabel.ForeColor = luminance > 0.55 ? Color.Black : Color.White;
    }

    private void CheckBoxChanged(object? sender, EventArgs e)
    {
        if (!presenting)
        {
            Session.SetShown(slot, checkBox.Checked);
        }
    }

    private void OffsetValueChanged(object? sender, EventArgs e)
    {
        if (presenting)
        {
            return;
        }

        if (Session.SetOffset(slot, offsetControl.Value))
        {
            offsetSaveTimer.Stop();
            offsetSaveTimer.Start();
        }
    }

    private void OffsetSaveTimerTick(object? sender, EventArgs e)
    {
        offsetSaveTimer.Stop();
        Session.FlushPendingSave(slot);
    }
}
