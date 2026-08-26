using Resonalyze.Dsp;

namespace Resonalyze;

// The filter-bank half of the EQ Wizard: the PEQ strips, the grid they live in,
// the drag-and-drop that reorders and removes them, and the undo history over
// the whole bank.
//
// The bank starts empty and grows by the "+" tile. A strip's index in
// `peqSlots` IS its filter number, its cell in the grid and its position in an
// exported profile, so every structural change ends in LayoutSlots(), and the
// order is part of the undo state rather than a display detail.
public partial class EqWizardPanel
{
    // A bell added by the "+" tile: mid-band and narrow. A new filter starts as
    // a deliberate, tight correction the user then drags into place, not as a
    // wide bell that colours half the spectrum on the first nudge.
    private const double AddedBandFrequencyHz = 1000;
    private const double AddedBandQ = 5;

    // A new shelf starts at the corner the target curve's own shelves default to,
    // with the steepest knee that stays monotonic — the neutral shelf, before the
    // user decides it should overshoot.
    private const double AddedLowShelfFrequencyHz = 100;
    private const double AddedHighShelfFrequencyHz = 5000;
    private const double AddedShelfQ = 0.7;

    // A new all-pass starts where the Virtual DSP channel card's stage did: an
    // all-pass is placed on a crossover region, so mid-band with a gentle turn is
    // the neutral start the user then drags onto the junction. Q is also what a
    // first-order band carries as its sentinel — the order has no Q, but every
    // validator on the way to a project file requires a positive one.
    private const double AddedAllPassFrequencyHz = 2000;
    private const double AddedAllPassQ = 1.0;

    // How long the bank must sit still before a burst of field or fader edits is
    // recorded as one undo step. The timer restarts on every change, so a whole
    // fader drag — however long — collapses into a single step.
    private const int BankEditIdleMilliseconds = 600;

    private readonly List<PeqSlotControl> peqSlots = new();
    private readonly PeqBankHistory bankHistory = new();
    private readonly System.Windows.Forms.Timer bankEditTimer = new()
    {
        Interval = BankEditIdleMilliseconds
    };

    private TableLayoutPanel peqSlotTable = null!;
    private PeqAddSlotControl addSlotTile = null!;
    // Rebuilt on every open (the checkmarks follow the strip it was opened on), so
    // the last one is not owned by the designer container — see Dispose.
    private ContextMenuStrip? bandTypeMenu;
    private PeqBankState committedBankState = PeqBankState.Empty;
    private PeqSlotControl? selectedSlot;
    private PeqSlotControl? draggedSlot;
    private int draggedSlotOrigin;
    private bool draggedSlotDropped;
    private bool draggedSlotCancelled;
    private bool restoringBank;
    private bool suppressBandCountSync;

    // ISO 266 preferred 1/3-octave centre frequencies, the ones a 31/32-band
    // graphic EQ is built on. 32 values (16 Hz .. 20 kHz) match the maximum bank
    // exactly: the standard 31-band 20 Hz..20 kHz set plus 16 Hz below it. They
    // are the starting spread when a whole bank is created at once; a single
    // band added by hand starts at AddedBandFrequencyHz instead.
    private static readonly double[] IsoThirdOctaveCentersHz =
    {
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
        630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000,
        10000, 12500, 16000, 20000
    };

    // The neutral Q a whole-bank spread starts on.
    private const double DefaultBandQ = 1.0;

    private static double DefaultBandFrequencyHz(int index) =>
        IsoThirdOctaveCentersHz[
            Math.Clamp(index, 0, IsoThirdOctaveCentersHz.Length - 1)];

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

    // The EQ Filters selector creates or trims a whole bank at once: picking N
    // brings the bank to N filters, appending them spread over the ISO centres
    // or dropping the trailing ones. It stays in step with what the "+" tile and
    // drag-removal do, so it always reads as the current filter count.
    private void InitializeBandsComboBox()
    {
        darkComboBoxBands.Items.Clear();
        for (int count = 0; count <= MaxPeqSlotCount; count++)
        {
            darkComboBoxBands.Items.Add(count);
        }

        darkComboBoxBands.SelectedIndexChanged += DarkComboBoxBandsSelectedIndexChanged;
        SyncBandCountCombo();
    }

    private void DarkComboBoxBandsSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (suppressBandCountSync)
        {
            return;
        }

        SetBandCount(darkComboBoxBands.SelectedItem is int count ? count : 0);
    }

    private void SyncBandCountCombo()
    {
        suppressBandCountSync = true;
        try
        {
            // Items are 0..MaxPeqSlotCount in order, so the index IS the count.
            darkComboBoxBands.SelectedIndex = peqSlots.Count;
        }
        finally
        {
            suppressBandCountSync = false;
        }
    }

    // Brings the bank to a given number of filters, keeping the ones already
    // there. Appended filters take the ISO centre of the position they land on.
    private void SetBandCount(int count)
    {
        count = Math.Clamp(count, 0, MaxPeqSlotCount);
        if (count == peqSlots.Count)
        {
            return;
        }

        CommitBankChange();
        suppressRedraw = true;
        try
        {
            while (peqSlots.Count > count)
            {
                RemoveSlot(peqSlots[^1]);
            }

            while (peqSlots.Count < count)
            {
                InsertSlot(
                    peqSlots.Count,
                    new PeqBand(DefaultBandFrequencyHz(peqSlots.Count), DefaultBandQ, 0));
            }

            LayoutSlots();
        }
        finally
        {
            suppressRedraw = false;
        }

        SyncBandCountCombo();
        RaiseSettingsChanged();
        DrawSelectedCurves();
        CommitBankChange();
    }

    // The shapes as the right-click menu offers them. The add tile names them with
    // its own short tokens (PK/HS/LS/AP1/AP2); a menu has room for the full words.
    private static readonly (PeqBandType Type, string Label)[] BandTypeChoices =
    {
        (PeqBandType.Peaking, "Peaking (bell)"),
        (PeqBandType.HighShelf, "High shelf"),
        (PeqBandType.LowShelf, "Low shelf"),
        (PeqBandType.AllPassFirstOrder, "All-pass, 1st order (phase only)"),
        (PeqBandType.AllPassSecondOrder, "All-pass, 2nd order (phase only)")
    };

    // Changing an existing filter's shape keeps its frequency, Q and gain: the
    // alternative is deleting the strip and dialling it in again, and a bell and a
    // shelf at the same corner are exactly what a tuner compares.
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

        DropDownFocusGuard.Attach(bandTypeMenu);
        bandTypeMenu.Show(screenPoint);
    }

    private void SetBandType(PeqSlotControl slot, PeqBandType type)
    {
        if (!peqSlots.Contains(slot) || slot.BandType == type)
        {
            return;
        }

        CommitBankChange();
        slot.BandType = type;
        SelectSlot(slot);
        RaiseSettingsChanged();
        DrawSelectedCurves();
        CommitBankChange();
    }

    // The band a freshly added slot starts on, per shape.
    private static PeqBand NewBand(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf =>
            new PeqBand(AddedLowShelfFrequencyHz, AddedShelfQ, 0, type),
        PeqBandType.HighShelf =>
            new PeqBand(AddedHighShelfFrequencyHz, AddedShelfQ, 0, type),
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            new PeqBand(AddedAllPassFrequencyHz, AddedAllPassQ, 0, type),
        _ => new PeqBand(AddedBandFrequencyHz, AddedBandQ, 0, type)
    };

    // Adds one filter at the end of the bank and selects it, so its curve is
    // highlighted straight away and the next fader move is obviously its own.
    private void AddBand(PeqBandType type)
    {
        if (peqSlots.Count >= MaxPeqSlotCount)
        {
            return;
        }

        CommitBankChange();
        PeqSlotControl slot = InsertSlot(peqSlots.Count, NewBand(type));
        LayoutSlots();
        SyncBandCountCombo();
        SelectSlot(slot);
        RaiseSettingsChanged();
        DrawSelectedCurves();
        CommitBankChange();
    }

    // Builds a strip for a band and puts it in the list. The caller lays the grid
    // out afterwards, so a batch of inserts costs one layout pass.
    private PeqSlotControl InsertSlot(int index, PeqBand band)
    {
        var slot = new PeqSlotControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1)
        };
        slot.SetGainRange(numericGainMin.Value, numericGainMax.Value);
        slot.SampleRateHz = EqProcessorSampleRate;
        // Values first, handlers second: a fresh strip is not an edit of the
        // bank and must not arm the undo timer or redraw the plot three times.
        WriteBand(slot, band);
        slot.FrequencyInput.ValueChanged += BankValueChanged;
        slot.QInput.ValueChanged += BankValueChanged;
        slot.GainInput.ValueChanged += BankValueChanged;
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

    private static void WriteBand(PeqSlotControl slot, PeqBand band)
    {
        slot.BandType = band.Type;
        slot.FrequencyInput.Value = slot.FrequencyInput.ClampValue(band.FrequencyHz);
        slot.QInput.Value = slot.QInput.ClampValue(band.Q);
        slot.GainInput.Value = slot.GainInput.ClampValue(band.GainDb);
    }

    private static PeqBand ReadBand(PeqSlotControl slot) => new(
        (double)slot.FrequencyInput.Value,
        (double)slot.QInput.Value,
        (double)slot.GainInput.Value,
        slot.BandType);

    // Moves a strip to another position in the bank, shifting the ones in
    // between. Called repeatedly while dragging, so it does nothing when the
    // target is where the strip already is.
    private void MoveSlot(PeqSlotControl slot, int index)
    {
        int current = peqSlots.IndexOf(slot);
        if (current < 0 || current == index)
        {
            return;
        }

        peqSlots.RemoveAt(current);
        peqSlots.Insert(Math.Clamp(index, 0, peqSlots.Count), slot);
        LayoutSlots();
    }

    // Re-seats every strip in its cell and renumbers it, then parks the "+" tile
    // after the last one. Cell assignments are made with layout suspended, so
    // the two-controls-in-one-cell states in the middle of the loop are never
    // laid out — the table only ever sees the finished, collision-free set.
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

            if (peqSlots.Count < MaxPeqSlotCount)
            {
                if (!peqSlotTable.Controls.Contains(addSlotTile))
                {
                    peqSlotTable.Controls.Add(addSlotTile);
                }

                SetCell(addSlotTile, peqSlots.Count);
            }
            else if (peqSlotTable.Controls.Contains(addSlotTile))
            {
                // Hiding it is not enough: an invisible control still holds its
                // cell, and the 32nd strip would be pushed out of the grid.
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

    // Selects a band so its individual contribution is highlighted on the plot.
    // Selecting another band replaces the previous highlight.
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

        DrawSelectedCurves();
    }

    // Clears the single-band highlight and removes its curve from the plot.
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

        DrawSelectedCurves();
    }

    // A band or preamp edit is a step in the undo history, but only once the user
    // stops: the timer restarts here and records the whole burst as one step.
    private void BankValueChanged(object? sender, EventArgs e)
    {
        ArmBankEditTimer();
        DrawSelectedCurves();
    }

    private void ArmBankEditTimer()
    {
        if (restoringBank)
        {
            return;
        }

        bankEditTimer.Stop();
        bankEditTimer.Start();
    }

    private PeqBankState CaptureBankState() =>
        new(peqSlots.Select(ReadBand), (double)NumericGain.Value);

    // Rebuilds the strips to match a bank state, reusing the ones already there.
    // Pure UI: what this means for the undo history is the caller's call.
    private void SetBank(PeqBankState state)
    {
        int selectedIndex = selectedSlot == null ? -1 : peqSlots.IndexOf(selectedSlot);

        restoringBank = true;
        suppressRedraw = true;
        try
        {
            while (peqSlots.Count > state.Bands.Count)
            {
                RemoveSlot(peqSlots[^1]);
            }

            for (int index = 0; index < state.Bands.Count; index++)
            {
                if (index < peqSlots.Count)
                {
                    WriteBand(peqSlots[index], state.Bands[index]);
                }
                else
                {
                    InsertSlot(index, state.Bands[index]);
                }
            }

            NumericGain.Value = NumericGain.ClampValue(state.PreampDb);
            LayoutSlots();
        }
        finally
        {
            suppressRedraw = false;
            restoringBank = false;
        }

        bankEditTimer.Stop();
        SyncBandCountCombo();
        RestoreSelection(selectedIndex);
        RaiseSettingsChanged();
        DrawSelectedCurves();
    }

    // Keeps the highlight on the same position in the bank across a rebuild, and
    // drops it when that position no longer exists.
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

    // Records everything edited since the last step as one undo step. Called
    // when the bank goes quiet, and up front by every structural change so a
    // half-typed field never rides along with the add/remove/reorder that
    // follows it.
    private void CommitBankChange()
    {
        bankEditTimer.Stop();
        if (restoringBank)
        {
            return;
        }

        PeqBankState current = CaptureBankState();
        if (current.Equals(committedBankState))
        {
            return;
        }

        bankHistory.Push(committedBankState);
        committedBankState = current;
        UpdateUndoRedoButtons();
        // The bank is persisted, and a step is exactly the granularity worth
        // saving at: a whole fader gesture becomes one write, not one per frame.
        RaiseSettingsChanged();
    }

    /// <summary>
    /// Lands an edit that is still in flight, so a caller about to persist the
    /// panel's settings reads the bank the user can see rather than the one from
    /// before their last keystroke.
    /// </summary>
    /// <remarks>
    /// Two things can be pending, and the text is the earlier of them: a field
    /// carries typed text until it loses focus or takes Enter, and only then does
    /// the value change that starts the coalescing pause. An ordinary close
    /// disables the form first, which takes the focus out of the field and commits
    /// the text on the way; an OS shutdown flushes immediately, with the caret
    /// still in the box. So the editors are landed first and the bank second.
    /// </remarks>
    internal void CommitPendingBankEdit()
    {
        NumericGain.CommitText();
        foreach (PeqSlotControl slot in peqSlots)
        {
            slot.CommitPendingText();
        }

        CommitBankChange();
    }

    // Adopts the current bank as the baseline with no history behind it — the
    // starting point after settings are restored, which is not an edit anyone
    // should be able to undo into.
    private void ResetBankHistory()
    {
        bankEditTimer.Stop();
        bankHistory.Clear();
        committedBankState = CaptureBankState();
        UpdateUndoRedoButtons();
    }

    private void UndoBankChange()
    {
        CommitBankChange();
        if (bankHistory.TryUndo(committedBankState, out PeqBankState previous))
        {
            ApplyHistoryState(previous);
        }
    }

    private void RedoBankChange()
    {
        CommitBankChange();
        if (bankHistory.TryRedo(committedBankState, out PeqBankState next))
        {
            ApplyHistoryState(next);
        }
    }

    private void ApplyHistoryState(PeqBankState state)
    {
        SetBank(state);
        // What the strips ended up holding, not what was asked for. A gain range
        // narrowed since the step was recorded clamps the restored values, and
        // adopting the unclamped state as the baseline would make the very next
        // commit see a change nobody made — recording a phantom step and throwing
        // the redo trail away with it.
        committedBankState = CaptureBankState();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        buttonUndo.Enabled = bankHistory.CanUndo;
        buttonRedo.Enabled = bankHistory.CanRedo;
    }

    // Undo/redo are bound at the panel rather than the focused field: the fields
    // are where the editing happens, and a text box's own Ctrl+Z would otherwise
    // swallow the shortcut and undo a keystroke instead of a filter. Losing
    // in-field text undo is the deliberate trade — a field commits its value on
    // Enter or focus loss, and that value is what the history holds.
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

    // Runs the drag of one strip. The bank re-orders live under the pointer, so
    // by the time the drop lands there is nothing left to apply; what remains is
    // deciding what a drag that did NOT land on the bank meant.
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
            // Escape puts the strip back where it was picked up from.
            MoveSlot(slot, draggedSlotOrigin);
        }
        else if (!draggedSlotDropped)
        {
            // Dropped away from the bank: the strip is thrown away. Moving it
            // through the grid on the way out left the others in their original
            // relative order, so removing it now is the whole of the change.
            RemoveSlot(slot);
            LayoutSlots();
            SyncBandCountCombo();
        }

        RaiseSettingsChanged();
        DrawSelectedCurves();
        CommitBankChange();
    }

    private void SlotDragOver(object? sender, DragEventArgs e)
    {
        if (draggedSlot == null)
        {
            e.Effect = DragDropEffects.None;
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

    // The slot index a screen point falls on, clamped to the filters that exist:
    // the empty cells past the end (and the "+" tile) all mean "last".
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

    // The pointer feedback IS the removal warning: inside the bank the strip is
    // being moved, outside it is being thrown away.
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

    // Clears the filter bank outright: no filters and no preamp. The source, the
    // target and the Auto Tune settings are deliberately untouched — this clears
    // the tune, not the setup it was made against.
    private void ResetBands()
    {
        if (peqSlots.Count == 0 && NumericGain.Value == 0)
        {
            return;
        }

        // A tune can represent a lot of manual work, so the one button that
        // throws all of it away at once asks first — Ctrl+Z or not.
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

        CommitBankChange();
        SetBank(PeqBankState.Empty);
        CommitBankChange();
    }

    /// <summary>
    /// A tuned bank with the all-pass bands of the bank it replaces carried over.
    /// The tuner fits magnitude and emits bells only, so a run would otherwise take
    /// the user's phase work with it — and an all-pass, being flat, is invisible in
    /// the error curve that decided the fit.
    /// </summary>
    /// <remarks>
    /// The kept bands go last, which is also where the slot budget bites: when the
    /// merged bank would overflow, the FITTED bands give way. An all-pass sits on a
    /// junction the user aligned by hand and the tuner cannot propose one, so the
    /// bands it can regenerate on the next run are the cheaper ones to lose.
    /// Deliberately UI-free — the keep-or-clobber decision is the panel's to ask
    /// and arrives here already made.
    /// </remarks>
    internal static EqualizationCurve WithAllPassBands(
        EqualizationCurve tuned,
        IReadOnlyList<PeqBand> allPass)
    {
        ArgumentNullException.ThrowIfNull(tuned);
        ArgumentNullException.ThrowIfNull(allPass);
        if (allPass.Count == 0)
        {
            return tuned;
        }

        return new EqualizationCurve(
            tuned.Bands
                .Take(Math.Max(0, MaxPeqSlotCount - allPass.Count))
                .Concat(allPass),
            tuned.PreampDb);
    }

    // Replaces the bank with a computed or imported one (Auto Tune, Import) as a
    // single undo step, however many filters it holds.
    private void ApplyEqualizationCurve(EqualizationCurve curve)
    {
        CommitBankChange();
        SetBank(new PeqBankState(
            curve.Bands.Take(MaxPeqSlotCount),
            curve.PreampDb));
        CommitBankChange();
    }

    // Restores the bank saved with the settings, and makes it the baseline the
    // history starts from — reopening the app is not an edit anyone should be
    // able to undo past. A file from before the bank was persisted carries only
    // a filter count, and rebuilds the ISO-centred spread those versions showed.
    private void ApplyPersistedBank(MeasurementSettingsFile.EqWizardSettings settings)
    {
        IEnumerable<PeqBand> bands = settings.Bands != null
            ? settings.Bands
                .Take(MaxPeqSlotCount)
                .Select(band => new PeqBand(
                    band.FrequencyHz,
                    band.Q,
                    band.GainDb,
                    // The enum converter accepts a number no member matches, and a
                    // shape nothing recognises must become a bell HERE — the one
                    // place it enters the app — rather than at each of the places
                    // that later ask what it is.
                    Enum.IsDefined(band.Type) ? band.Type : PeqBandType.Peaking))
            : Enumerable
                .Range(0, Math.Clamp(settings.BandCount, 0, MaxPeqSlotCount))
                .Select(index => new PeqBand(DefaultBandFrequencyHz(index), DefaultBandQ, 0));

        // Out-of-range or corrupt numbers are clamped by the strips themselves
        // (see WriteBand), so a hand-edited file loses the odd value, not the bank.
        SetBank(new PeqBankState(bands, settings.PreampDb));
        ResetBankHistory();
    }

    // The bank as the settings file stores it, in slot order.
    private List<MeasurementSettingsFile.PeqBandSettings> CaptureBands() =>
        peqSlots
            .Select(ReadBand)
            .Select(band => new MeasurementSettingsFile.PeqBandSettings
            {
                FrequencyHz = band.FrequencyHz,
                Q = band.Q,
                GainDb = band.GainDb,
                Type = band.Type
            })
            .ToList();
}
