namespace Resonalyze;

/// <summary>Tools... → Capture to overlay: which curve of the upper plot, into which Frequency Response slot.</summary>
internal sealed partial class VirtualCrossoverOverlayExportDialog : Form
{
    private readonly IReadOnlyList<OverlaySlotOccupant> slots;
    // The slot follows the curve (VirtualCrossoverOverlayExport.DefaultSlot) until the user picks one.
    private bool slotPicked;

    /// <param name="curveKey">The curve picked last time; the shown side's Sum when it is not offered.</param>
    public VirtualCrossoverOverlayExportDialog(
        IReadOnlyList<VirtualCrossoverOverlayCurve> curves,
        IReadOnlyList<OverlaySlotOccupant> slots,
        string? curveKey)
    {
        InitializeComponent();
        this.slots = slots;
        comboBoxCurve.Format += (_, args) =>
        {
            if (args.ListItem is VirtualCrossoverOverlayCurve curve)
            {
                args.Value = curve.Label;
            }
        };
        comboBoxSlot.Format += (_, args) =>
        {
            if (args.ListItem is OverlaySlotOccupant slot)
            {
                args.Value = VirtualCrossoverOverlayExport.SlotLabel(slot);
            }
        };
        comboBoxCurve.Items.AddRange([.. curves]);
        comboBoxSlot.Items.AddRange([.. slots]);
        comboBoxCurve.SelectedItem =
            curves.FirstOrDefault(curve => curve.Key == curveKey) ??
            curves.FirstOrDefault(curve => curve.Key == VirtualCrossoverOverlayExport.ShownSumKey) ??
            curves.FirstOrDefault();
        comboBoxCurve.SelectedIndexChanged += (_, _) => FollowCurve();
        comboBoxSlot.SelectionChangeCommitted += (_, _) => slotPicked = true;
        comboBoxSlot.SelectedIndexChanged += (_, _) => ShowReplaced();
        FollowCurve();
    }

    public VirtualCrossoverOverlayCurve? SelectedCurve => comboBoxCurve.SelectedItem as VirtualCrossoverOverlayCurve;

    public int? SelectedSlot => (comboBoxSlot.SelectedItem as OverlaySlotOccupant)?.Slot;

    private void FollowCurve()
    {
        if (!slotPicked && SelectedCurve is { } curve)
        {
            int? slot = VirtualCrossoverOverlayExport.DefaultSlot(slots, curve.Title);
            comboBoxSlot.SelectedIndex = slots.ToList().FindIndex(occupant => occupant.Slot == slot);
        }

        ShowReplaced();
    }

    private void ShowReplaced()
    {
        OverlaySlotOccupant? slot = comboBoxSlot.SelectedItem as OverlaySlotOccupant;
        labelReplaces.Text = slot is not { IsFree: false }
            ? string.Empty
            : slot.Title is { } title
                ? $"Replaces “{OverlaySlotName.Shorten(title, slot.Slot)}”."
                : "Replaces the unreadable file.";
        buttonSave.Enabled = slot != null && SelectedCurve != null;
    }
}
