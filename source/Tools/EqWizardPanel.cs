namespace Resonalyze;

public partial class EqWizardPanel : UserControl
{
    private const int MaxPeqSlotCount = 32;
    private const int PeqColumnCount = 4;
    private const int PeqRowCount = 8;

    private readonly List<PeqSlotControl> peqSlots = new();
    private TableLayoutPanel peqSlotTable = null!;

    public EqWizardPanel()
    {
        InitializeComponent();
        InitializePeqSlotTable();
        InitializeBandsComboBox();
    }

    internal IReadOnlyList<PeqSlotControl> PeqSlots => peqSlots;

    internal TargetOverlayOption? SelectedTargetOverlay =>
        darkComboBoxSource.SelectedItem as TargetOverlayOption;

    public void SetPeqSlotCount(int slotCount)
    {
        int clampedSlotCount = Math.Clamp(slotCount, 0, MaxPeqSlotCount);
        peqSlotTable.SuspendLayout();
        try
        {
            peqSlotTable.Controls.Clear();
            peqSlots.Clear();

            for (int index = 0; index < clampedSlotCount; index++)
            {
                var slot = new PeqSlotControl
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    SlotNumber = index + 1
                };
                int column = index / PeqRowCount;
                int row = index % PeqRowCount;
                peqSlots.Add(slot);
                peqSlotTable.Controls.Add(slot, column, row);
            }
        }
        finally
        {
            peqSlotTable.ResumeLayout();
        }
    }

    internal void SetTargetOverlayOptions(IReadOnlyList<TargetOverlayOption> options)
    {
        int? previousSlot = SelectedTargetOverlay?.Slot;
        darkComboBoxSource.Items.Clear();
        foreach (TargetOverlayOption option in options)
        {
            darkComboBoxSource.Items.Add(option);
        }

        darkComboBoxSource.Enabled = options.Count > 0;
        if (options.Count == 0)
        {
            darkComboBoxSource.SelectedIndex = -1;
            return;
        }

        int selectedIndex = 0;
        if (previousSlot.HasValue)
        {
            for (int index = 0; index < options.Count; index++)
            {
                if (options[index].Slot == previousSlot.Value)
                {
                    selectedIndex = index;
                    break;
                }
            }
        }

        darkComboBoxSource.SelectedIndex = selectedIndex;
    }

    internal bool SelectTargetOverlaySlot(int slot)
    {
        for (int index = 0; index < darkComboBoxSource.Items.Count; index++)
        {
            if (darkComboBoxSource.Items[index] is TargetOverlayOption option &&
                option.Slot == slot)
            {
                darkComboBoxSource.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    private void InitializePeqSlotTable()
    {
        peqSlotTable = new TableLayoutPanel
        {
            BackColor = panelPEQ.BackColor,
            ColumnCount = PeqColumnCount,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4),
            RowCount = PeqRowCount
        };

        for (int column = 0; column < PeqColumnCount; column++)
        {
            peqSlotTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / PeqColumnCount));
        }
        for (int row = 0; row < PeqRowCount; row++)
        {
            peqSlotTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / PeqRowCount));
        }

        panelPEQ.Controls.Add(peqSlotTable);
    }

    private void InitializeBandsComboBox()
    {
        darkComboBoxBands.Items.Clear();
        for (int count = 1; count <= MaxPeqSlotCount; count++)
        {
            darkComboBoxBands.Items.Add(count);
        }

        darkComboBoxBands.SelectedIndexChanged += DarkComboBoxBandsSelectedIndexChanged;
        darkComboBoxBands.SelectedIndex = 0;
    }

    private void DarkComboBoxBandsSelectedIndexChanged(object? sender, EventArgs e)
    {
        int slotCount = darkComboBoxBands.SelectedItem is int selectedCount
            ? selectedCount
            : 1;
        SetPeqSlotCount(slotCount);
    }
}
