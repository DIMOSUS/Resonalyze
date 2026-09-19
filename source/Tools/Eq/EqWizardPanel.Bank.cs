using Resonalyze.Dsp;

namespace Resonalyze;

// One strip per band of the session's bank, in bank order: a strip's index IS its filter number, grid cell and export
// position. Structural changes go to the bank and end in PresentBank().
public partial class EqWizardPanel
{
    // The timer restarts on every change, so a whole fader drag is one undo step.
    private const int BankEditIdleMilliseconds = 600;

    private readonly List<PeqSlotControl> peqSlots = new();
    private readonly System.Windows.Forms.Timer bankEditTimer = new()
    {
        Interval = BankEditIdleMilliseconds
    };

    private TableLayoutPanel peqSlotTable = null!;
    private PeqAddSlotControl addSlotTile = null!;
    // Rebuilt per open, so the last one is not owned by the designer container (see Dispose).
    private ContextMenuStrip? bandTypeMenu;
    private PeqSlotControl? selectedSlot;
    private PeqSlotControl? draggedSlot;
    private int draggedSlotOrigin;
    private bool draggedSlotDropped;
    private bool draggedSlotCancelled;

    private void InitializePeqSlotTable()
    {
        peqSlotTable = new DoubleBufferedTableLayoutPanel
        {
            BackColor = panelPEQ.BackColor,
            ColumnCount = PeqColumnCount,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(2),
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

        peqSlotTable.Click += (_, _) => DeselectBand();
        peqSlotTable.AllowDrop = true;
        peqSlotTable.DragEnter += SlotDragOver;
        peqSlotTable.DragOver += SlotDragOver;
        peqSlotTable.DragDrop += SlotDragDrop;

        addSlotTile = new PeqAddSlotControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            AllowDrop = true
        };
        addSlotTile.AddRequested += (_, args) => AddBand(args.Type);
        addSlotTile.DragEnter += SlotDragOver;
        addSlotTile.DragOver += SlotDragOver;
        addSlotTile.DragDrop += SlotDragDrop;
        SetTip(addSlotTile,
            "Add a filter: PK a peaking bell, HS a high shelf, LS a low shelf, " +
            "AP1/AP2 a first- or second-order all-pass (moves phase only). Drag " +
            "a filter by its number to reorder it, or out of the bank to remove it; " +
            "right-click the number to change its type. Ctrl+Z undoes any of it.");

        panelPEQ.Controls.Add(peqSlotTable);
        bankEditTimer.Tick += (_, _) => CommitBankChange();
        LayoutSlots();
    }

    private void InitializeBandsComboBox()
    {
        darkComboBoxBands.Items.Clear();
        for (int count = 0; count <= EqWizardLimits.MaxBands; count++)
        {
            darkComboBoxBands.Items.Add(count);
        }

        darkComboBoxBands.SelectedIndexChanged += (_, _) =>
        {
            if (!presenting)
            {
                SetBandCount(darkComboBoxBands.SelectedItem is int count ? count : 0);
            }
        };
        PresentBandCount();
    }

    private void PresentBandCount() => Present(() => darkComboBoxBands.SelectedIndex = peqSlots.Count);

    // Each structural change lands the pending edit as its own step first (in the bank), so the edit timer stops only once one happened.
    private void SetBandCount(int count)
    {
        if (session.Bank.SetCount(count))
        {
            bankEditTimer.Stop();
            PresentBank(keepSelection: false);
            Redraw();
        }
    }

    private static readonly (PeqBandType Type, string Label)[] BandTypeChoices =
    {
        (PeqBandType.Peaking, "Peaking (bell)"),
        (PeqBandType.HighShelf, "High shelf"),
        (PeqBandType.LowShelf, "Low shelf"),
        (PeqBandType.AllPassFirstOrder, "All-pass, 1st order (phase only)"),
        (PeqBandType.AllPassSecondOrder, "All-pass, 2nd order (phase only)")
    };

    private void ShowBandTypeMenu(PeqSlotControl slot, Point screenPoint)
    {
        if (!peqSlots.Contains(slot))
        {
            return;
        }

        bandTypeMenu?.Dispose();
        bandTypeMenu = new ContextMenuStrip();
        foreach ((PeqBandType type, string label) in BandTypeChoices)
        {
            PeqBandType chosen = type;
            var item = new ToolStripMenuItem(label, null, (_, _) => SetBandType(slot, chosen))
            {
                Checked = slot.BandType == type
            };
            bandTypeMenu.Items.Add(item);
        }

        DropDownMenu.ShowAt(this, bandTypeMenu, screenPoint);
    }

    private void SetBandType(PeqSlotControl slot, PeqBandType type)
    {
        int index = peqSlots.IndexOf(slot);
        if (index < 0)
        {
            return;
        }

        if (session.Bank.SetType(index, type))
        {
            bankEditTimer.Stop();
            PresentBank(keepSelection: false);
            SelectSlot(slot);
            Redraw();
        }
    }

    private void AddBand(PeqBandType type)
    {
        int index = session.Bank.Add(type);
        if (index < 0)
        {
            return;
        }

        bankEditTimer.Stop();
        PresentBank(keepSelection: false);
        SelectSlot(peqSlots[index]);
        Redraw();
    }

    // The caller lays the grid out, so a batch of inserts costs one layout pass.
    private PeqSlotControl InsertSlot(int index, PeqBand band)
    {
        var slot = new PeqSlotControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1)
        };
        slot.SetGainRange(session.GainMinDb, session.GainMaxDb);
        slot.SampleRateHz = session.ProcessorSampleRateHz;
        // Values before handlers: a fresh strip must not arm the undo timer or redraw.
        WriteBand(slot, band);
        slot.FrequencyInput.ValueChanged += (_, _) => StripValueChanged(slot);
        slot.QInput.ValueChanged += (_, _) => StripValueChanged(slot);
        slot.GainInput.ValueChanged += (_, _) => StripValueChanged(slot);
        SetTip(slot.FrequencyInput, FrequencyTip);
        SetTip(slot.QInput, QTip);
        SetTip(slot.GainInput, GainTip);
        SetTip(slot.GroupDelayReadout, AllPassGroupDelayTip);
        SetTip(slot.SlotLabel,
            "Filter number and type, and the drag handle: drag it to reorder the " +
            "filter or out of the bank to remove it, right-click to switch between " +
            "a bell, a shelf and an all-pass.");
        slot.Activated += (sender, _) => SelectSlot((PeqSlotControl)sender!);
        slot.TypeMenuRequested += (sender, args) =>
            ShowBandTypeMenu((PeqSlotControl)sender!, args.ScreenPoint);
        slot.DragStartRequested += (sender, _) => BeginSlotDrag((PeqSlotControl)sender!);
        slot.EnableDropTarget(SlotDragOver, SlotDragDrop);

        peqSlots.Insert(index, slot);
        peqSlotTable.Controls.Add(slot);
        return slot;
    }

    private void RemoveSlot(PeqSlotControl slot)
    {
        if (!peqSlots.Remove(slot))
        {
            return;
        }

        if (selectedSlot == slot)
        {
            selectedSlot = null;
        }

        peqSlotTable.Controls.Remove(slot);
        slot.Dispose();
    }

    // The session holds the band as the strip shows it, so writing it back changes no value.
    private static void WriteBand(PeqSlotControl slot, PeqBand band)
    {
        slot.BandType = band.Type;
        slot.FrequencyInput.Value = (decimal)band.FrequencyHz;
        slot.QInput.Value = (decimal)band.Q;
        slot.GainInput.Value = (decimal)band.GainDb;
    }

    private static PeqBand ReadBand(PeqSlotControl slot) => new(
        (double)slot.FrequencyInput.Value,
        (double)slot.QInput.Value,
        (double)slot.GainInput.Value,
        slot.BandType);

    /// <summary>
    /// Makes the strips show the session's bank: trailing strips added or removed, every value written, the grid laid out.
    /// </summary>
    /// <param name="keepSelection">Keeps the selected filter NUMBER, for a whole new bank written over the old one.</param>
    private void PresentBank(bool keepSelection)
    {
        int selectedIndex = selectedSlot == null ? -1 : peqSlots.IndexOf(selectedSlot);
        IReadOnlyList<PeqBand> bands = session.Bank.Bands;
        Present(() =>
        {
            while (peqSlots.Count > bands.Count)
            {
                RemoveSlot(peqSlots[^1]);
            }

            for (int index = 0; index < bands.Count; index++)
            {
                if (index < peqSlots.Count)
                {
                    // The range first, or a gain outside the old one would be clamped on its way in.
                    peqSlots[index].SetGainRange(session.GainMinDb, session.GainMaxDb);
                    WriteBand(peqSlots[index], bands[index]);
                }
                else
                {
                    InsertSlot(index, bands[index]);
                }
            }

            NumericGain.Value = (decimal)session.Bank.PreampDb;
            LayoutSlots();
        });
        PresentBandCount();
        UpdateUndoRedoButtons();
        if (keepSelection)
        {
            RestoreSelection(selectedIndex);
        }
    }

    // Called repeatedly while dragging; no-op when already in place.
    private void MoveSlot(PeqSlotControl slot, int index)
    {
        int current = peqSlots.IndexOf(slot);
        if (current < 0 || current == index)
        {
            return;
        }

        session.Bank.Move(current, index);
        peqSlots.RemoveAt(current);
        peqSlots.Insert(Math.Clamp(index, 0, peqSlots.Count), slot);
        LayoutSlots();
    }

    // Layout is suspended so the transient two-controls-in-one-cell states are never laid out.
    private void LayoutSlots()
    {
        peqSlotTable.SuspendLayout();
        try
        {
            for (int index = 0; index < peqSlots.Count; index++)
            {
                peqSlots[index].SlotNumber = index + 1;
                SetCell(peqSlots[index], index);
            }

            if (peqSlots.Count < EqWizardLimits.MaxBands)
            {
                if (!peqSlotTable.Controls.Contains(addSlotTile))
                {
                    peqSlotTable.Controls.Add(addSlotTile);
                }

                SetCell(addSlotTile, peqSlots.Count);
            }
            else if (peqSlotTable.Controls.Contains(addSlotTile))
            {
                // An invisible control still holds its cell and would push the 32nd strip out.
                peqSlotTable.Controls.Remove(addSlotTile);
            }
        }
        finally
        {
            peqSlotTable.ResumeLayout();
        }
    }

    private void SetCell(Control control, int index)
    {
        (int column, int row) = PeqSlotGrid.CellOf(index, PeqColumnCount);
        peqSlotTable.SetCellPosition(
            control,
            new TableLayoutPanelCellPosition(column, row));
    }

    private void SelectSlot(PeqSlotControl slot)
    {
        if (slot == selectedSlot || !peqSlots.Contains(slot))
        {
            return;
        }

        selectedSlot = slot;
        foreach (PeqSlotControl other in peqSlots)
        {
            other.SetSelected(other == slot);
        }

        Redraw();
    }

    private void DeselectBand()
    {
        if (selectedSlot == null)
        {
            return;
        }

        selectedSlot = null;
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.SetSelected(false);
        }

        Redraw();
    }

    private void RestoreSelection(int index)
    {
        if (index < 0 || index >= peqSlots.Count)
        {
            DeselectBand();
            return;
        }

        selectedSlot = null;
        SelectSlot(peqSlots[index]);
    }

    private void StripValueChanged(PeqSlotControl slot)
    {
        int index = peqSlots.IndexOf(slot);
        if (presenting || index < 0)
        {
            return;
        }

        session.Bank.Edit(index, ReadBand(slot));
        ArmBankEditTimer();
        Redraw();
    }

    private void PreampValueChanged()
    {
        if (presenting)
        {
            return;
        }

        session.Bank.EditPreamp((double)NumericGain.Value);
        ArmBankEditTimer();
        Redraw();
    }

    private void ArmBankEditTimer()
    {
        bankEditTimer.Stop();
        bankEditTimer.Start();
    }

    private void CommitBankChange()
    {
        bankEditTimer.Stop();
        if (session.Bank.Commit())
        {
            UpdateUndoRedoButtons();
        }
    }

    /// <summary>
    /// Lands in-flight edits before persisting: editors first (typed text commits only on focus loss/Enter, and an OS
    /// shutdown flushes with the caret still in the box), then the pending bank step.
    /// </summary>
    internal void CommitPendingBankEdit()
    {
        NumericGain.CommitText();
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.CommitPendingText();
        }

        CommitBankChange();
    }

    private void UndoBankChange()
    {
        bankEditTimer.Stop();
        if (session.Bank.Undo())
        {
            PresentBank(keepSelection: true);
            Redraw();
        }

        UpdateUndoRedoButtons();
    }

    private void RedoBankChange()
    {
        bankEditTimer.Stop();
        if (session.Bank.Redo())
        {
            PresentBank(keepSelection: true);
            Redraw();
        }

        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        buttonUndo.Enabled = session.Bank.CanUndo;
        buttonRedo.Enabled = session.Bank.CanRedo;
    }

    // Bound at the panel so a text box's own Ctrl+Z does not undo a keystroke instead of a filter (deliberate trade).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Z:
                UndoBankChange();
                return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z:
                RedoBankChange();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // The bank re-orders live under the pointer; what remains is a drag that did NOT land on the bank.
    private void BeginSlotDrag(PeqSlotControl slot)
    {
        if (draggedSlot != null || !peqSlots.Contains(slot))
        {
            return;
        }

        CommitBankChange();
        draggedSlot = slot;
        draggedSlotOrigin = peqSlots.IndexOf(slot);
        draggedSlotDropped = false;
        draggedSlotCancelled = false;
        slot.SetDragging(true);
        try
        {
            DoDragDrop(slot, DragDropEffects.Move);
        }
        finally
        {
            draggedSlot = null;
            slot.SetDragging(false);
        }

        if (draggedSlotCancelled)
        {
            MoveSlot(slot, draggedSlotOrigin);
        }
        else if (!draggedSlotDropped)
        {
            // Dropped outside the bank: the others kept their relative order, so removal is the whole change.
            session.Bank.Remove(peqSlots.IndexOf(slot));
            RemoveSlot(slot);
            LayoutSlots();
            PresentBandCount();
        }

        Redraw();
        CommitBankChange();
    }

    private void SlotDragOver(object? sender, DragEventArgs e)
    {
        if (draggedSlot == null)
        {
            // May be an Explorer file drop, registered on these controls too; refusing it would block drops on the bank.
            if (!FileDropTarget.CarriesFiles(e.Data))
            {
                e.Effect = DragDropEffects.None;
            }

            return;
        }

        e.Effect = DragDropEffects.Move;
        MoveSlot(draggedSlot, TargetIndexAt(new Point(e.X, e.Y)));
    }

    private void SlotDragDrop(object? sender, DragEventArgs e)
    {
        if (draggedSlot == null)
        {
            return;
        }

        e.Effect = DragDropEffects.Move;
        draggedSlotDropped = true;
    }

    // Empty cells past the end and the "+" tile all mean "last".
    private int TargetIndexAt(Point screenPoint)
    {
        if (peqSlots.Count == 0)
        {
            return 0;
        }

        Point client = peqSlotTable.PointToClient(screenPoint);
        Padding padding = peqSlotTable.Padding;
        int index = PeqSlotGrid.IndexAt(
            peqSlotTable.GetColumnWidths(),
            peqSlotTable.GetRowHeights(),
            new Point(client.X - padding.Left, client.Y - padding.Top));
        return Math.Clamp(index, 0, peqSlots.Count - 1);
    }

    // The pointer feedback IS the removal warning.
    protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);
        if (draggedSlot == null)
        {
            return;
        }

        e.UseDefaultCursors = false;
        Cursor.Current = IsOverBank(Cursor.Position)
            ? Cursors.SizeAll
            : TrashCursor.Instance;
    }

    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        base.OnQueryContinueDrag(e);
        if (e.EscapePressed)
        {
            draggedSlotCancelled = true;
        }
    }

    private bool IsOverBank(Point screenPoint) =>
        peqSlotTable.RectangleToScreen(peqSlotTable.ClientRectangle).Contains(screenPoint);

    // Source, target and Auto Tune settings are deliberately untouched: this clears the tune, not the setup.
    private void ResetBands()
    {
        if (session.Bank.IsEmpty)
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                "Reset the whole filter bank?" +
                Environment.NewLine + Environment.NewLine +
                "Every filter is removed and the preamp returns to 0 dB. The source " +
                "curve, the target and the Auto Tune settings are kept, and Ctrl+Z " +
                "brings the filters back.",
                "EQ Wizard",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        bankEditTimer.Stop();
        session.Bank.Clear();
        PresentBank(keepSelection: true);
        Redraw();
    }

    private void ApplyEqualizationCurve(EqualizationCurve curve)
    {
        bankEditTimer.Stop();
        session.Bank.Replace(curve);
        PresentBank(keepSelection: true);
        Redraw();
    }
}
