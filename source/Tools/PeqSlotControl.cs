using System.ComponentModel;

namespace Resonalyze;

public partial class PeqSlotControl : UserControl
{
    private static readonly Color NormalBackColor = Color.FromArgb(44, 50, 60);
    private static readonly Color SelectedBackColor = Color.FromArgb(58, 66, 86);

    private int slotNumber = 1;

    public PeqSlotControl()
    {
        InitializeComponent();
        HookActivation(this);
    }

    // Raised when the user clicks the slot or focuses any of its fields, so the
    // host can show this band's individual contribution.
    public event EventHandler? Activated;

    public void SetSelected(bool selected)
    {
        BackColor = selected ? SelectedBackColor : NormalBackColor;
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

    internal DarkNumericUpDown FrequencyInput => frequencyInput;

    internal DarkNumericUpDown QInput => qInput;

    internal DarkNumericUpDown GainInput => gainInput;
}
