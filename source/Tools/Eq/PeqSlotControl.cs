using System.ComponentModel;

namespace Resonalyze;

public partial class PeqSlotControl : UserControl
{
    private static readonly Color NormalBackColor = Color.FromArgb(44, 50, 60);
    private static readonly Color SelectedBackColor = Color.FromArgb(58, 66, 86);

    private int slotNumber = 1;
    private bool suppressGainSync;

    public PeqSlotControl()
    {
        InitializeComponent();
        WireGainFader();
        HookActivation(this);
    }

    // Raised when the user clicks the slot or focuses any of its fields, so the
    // host can show this band's individual contribution.
    public event EventHandler? Activated;

    public void SetSelected(bool selected)
    {
        Color color = selected ? SelectedBackColor : NormalBackColor;
        BackColor = color;
        slotLayout.BackColor = color;
        // The fader paints its background from the strip colour, so it must be
        // told to repaint when the selection tint changes. It also gates its
        // click-to-drag on whether this band is the selected one.
        fader.StripActive = selected;
        fader.BackColor = color;
        fader.Invalidate();
    }

    // Keeps the vertical fader and the gain field in lock-step: the numeric field
    // stays the source of truth (the host reads it), the fader is a view over it.
    private void WireGainFader()
    {
        fader.Minimum = (double)gainInput.Minimum;
        fader.Maximum = (double)gainInput.Maximum;
        fader.Increment = (double)gainInput.Increment;
        fader.Value = (double)gainInput.Value;

        gainInput.ValueChanged += (_, _) =>
        {
            if (suppressGainSync)
            {
                return;
            }

            suppressGainSync = true;
            try
            {
                fader.Value = (double)gainInput.Value;
            }
            finally
            {
                suppressGainSync = false;
            }
        };
        fader.ValueChanged += (_, _) =>
        {
            if (suppressGainSync)
            {
                return;
            }

            suppressGainSync = true;
            try
            {
                gainInput.Value = gainInput.ClampValue(fader.Value);
            }
            finally
            {
                suppressGainSync = false;
            }
        };
    }

    private void HookActivation(Control control)
    {
        control.Click += RaiseActivated;
        control.Enter += RaiseActivated;
        foreach (Control child in control.Controls)
        {
            HookActivation(child);
        }
    }

    private void RaiseActivated(object? sender, EventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    [DefaultValue(1)]
    public int SlotNumber
    {
        get => slotNumber;
        set
        {
            slotNumber = Math.Max(1, value);
            slotLabel.Text = slotNumber.ToString();
        }
    }

    // Applies a new gain range to both the numeric field and the fader so they
    // keep sharing one scale. The min is <= 0 <= max, so ordering never inverts;
    // the field clamps its value and the fader is re-mirrored to match.
    internal void SetGainRange(decimal minimum, decimal maximum)
    {
        gainInput.Minimum = minimum;
        gainInput.Maximum = maximum;
        fader.Minimum = (double)minimum;
        fader.Maximum = (double)maximum;
        fader.Value = (double)gainInput.Value;
    }

    internal DarkNumericUpDown FrequencyInput => frequencyInput;

    internal DarkNumericUpDown QInput => qInput;

    internal DarkNumericUpDown GainInput => gainInput;
}
