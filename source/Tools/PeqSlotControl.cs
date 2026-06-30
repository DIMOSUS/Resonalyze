using System.ComponentModel;

namespace Resonalyze;

public partial class PeqSlotControl : UserControl
{
    private int slotNumber = 1;

    public PeqSlotControl()
    {
        InitializeComponent();
    }

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
