using Resonalyze.Dsp;

namespace Resonalyze;

// A strip's index in `peqSlots` IS its filter number, grid cell and export position: structural changes end in
// LayoutSlots(), and order is part of the undo state.
public partial class EqWizardPanel
{
    // Narrow and mid-band: a deliberate correction to drag into place, not a wide bell colouring half the spectrum.
    private const double AddedBandFrequencyHz = 1000;
    private const double AddedBandQ = 5;

    // Target curve's default shelf corners, with the steepest monotonic knee.
    private const double AddedLowShelfFrequencyHz = 100;
    private const double AddedHighShelfFrequencyHz = 5000;
    private const double AddedShelfQ = 0.7;

    // Q is the first-order band's sentinel too: the order has no Q, but project-file validators require a positive one.
    private const double AddedAllPassFrequencyHz = 2000;
    private const double AddedAllPassQ = 1.0;

    // The timer restarts on every change, so a whole fader drag is one undo step.
    private const int BankEditIdleMilliseconds = 600;

    private readonly List<PeqSlotControl> peqSlots = new();
    private readonly PeqBankHistory bankHistory = new();
    private readonly System.Windows.Forms.Timer bankEditTimer = new()
    {
        Interval = BankEditIdleMilliseconds
    };

    private TableLayoutPanel peqSlotTable = null!;
    private PeqAddSlotControl addSlotTile = null!;
    // Rebuilt per open, so the last one is not owned by the designer container (see Dispose).
    private ContextMenuStrip? bandTypeMenu;
    private PeqBankState committedBankState = PeqBankState.Empty;
    private PeqSlotControl? selectedSlot;
    private PeqSlotControl? draggedSlot;
    private int draggedSlotOrigin;
    private bool draggedSlotDropped;
    private bool draggedSlotCancelled;
    private bool restoringBank;
    private bool suppressBandCountSync;

    // ISO 266 1/3-octave centres, 16 Hz..20 kHz: 32 values match the maximum bank; used for whole-bank spreads.
    private static readonly double[] IsoThirdOctaveCentersHz =
    {
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
        630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000,
        10000, 12500, 16000, 20000
    };

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

    private void InitializeBandsComboBox()
    {
        darkComboBoxBands.Items.Clear();
        for (int count = 0; count <= MaxPeqSlotCount; count++)
        {
            darkComboBoxBands.Items.Add(count);
        }

        darkComboBoxBands.SelectedIndexChanged += ThemedComboBoxBandsSelectedIndexChanged;
        SyncBandCountCombo();
    }

    private void ThemedComboBoxBandsSelectedIndexChanged(object? sender, EventArgs e)
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
            darkComboBoxBands.SelectedIndex = peqSlots.Count;
        }
        finally
        {
            suppressBandCountSync = false;
        }
    }

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

    private static readonly (PeqBandType Type, string Label)[] BandTypeChoices =
    {
        (PeqBandType.Peaking, "Peaking (bell)"),
        (PeqBandType.HighShelf, "High shelf"),
        (PeqBandType.LowShelf, "Low shelf"),
        (PeqBandType.AllPassFirstOrder, "All-pass, 1st order (phase only)"),
        (PeqBandType.AllPassSecondOrder, "All-pass, 2nd order (phase only)")
    };

    // Keeps frequency, Q and gain: a bell and a shelf at the same corner are what a tuner compares.
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

    // The caller lays the grid out, so a batch of inserts costs one layout pass.
    private PeqSlotControl InsertSlot(int index, PeqBand band)
    {
        var slot = new PeqSlotControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1)
        };
        slot.SetGainRange(numericGainMin.Value, numericGainMax.Value);
        slot.SampleRateHz = EqProcessorSampleRate;
        // Values before handlers: a fresh strip must not arm the undo timer or redraw.
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

    // Called repeatedly while dragging; no-op when already in place.
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

        DrawSelectedCurves();
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

        DrawSelectedCurves();
    }

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

    // Pure UI: undo-history consequences are the caller's.
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

    // Also called before every structural change so a half-typed field does not ride along with it.
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
        RaiseSettingsChanged();
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

    // Restored settings are not an edit anyone should undo into.
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
        // What the strips hold after clamping, or the next commit would record a phantom step and drop the redo trail.
        committedBankState = CaptureBankState();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        buttonUndo.Enabled = bankHistory.CanUndo;
        buttonRedo.Enabled = bankHistory.CanRedo;
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
        if (peqSlots.Count == 0 && NumericGain.Value == 0)
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

        CommitBankChange();
        SetBank(PeqBankState.Empty);
        CommitBankChange();
    }

    /// <summary>
    /// Carries the replaced bank's all-pass bands over into a tuned bank (the tuner emits bells only). On overflow the
    /// FITTED bands give way: they can be regenerated, a hand-aligned all-pass cannot.
    /// </summary>
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

    private void ApplyEqualizationCurve(EqualizationCurve curve)
    {
        CommitBankChange();
        SetBank(new PeqBankState(
            curve.Bands.Take(MaxPeqSlotCount),
            curve.PreampDb));
        CommitBankChange();
    }

    // Restored bank becomes the history baseline. Old files carry only a count and rebuild the ISO-centred spread.
    private void ApplyPersistedBank(MeasurementSettingsFile.EqWizardSettings settings)
    {
        IEnumerable<PeqBand> bands = settings.Bands != null
            ? settings.Bands
                .Take(MaxPeqSlotCount)
                .Select(band => new PeqBand(
                    band.FrequencyHz,
                    band.Q,
                    band.GainDb,
                    // An undefined enum number becomes a bell HERE, where it enters the app.
                    Enum.IsDefined(band.Type) ? band.Type : PeqBandType.Peaking))
            : Enumerable
                .Range(0, Math.Clamp(settings.BandCount, 0, MaxPeqSlotCount))
                .Select(index => new PeqBand(DefaultBandFrequencyHz(index), DefaultBandQ, 0));

        // Strips clamp corrupt values (see WriteBand), so a hand-edited file loses a value, not the bank.
        SetBank(new PeqBankState(bands, settings.PreampDb));
        ResetBankHistory();
    }

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
