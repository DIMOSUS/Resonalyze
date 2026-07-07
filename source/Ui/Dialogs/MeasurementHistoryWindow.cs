using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.History;
using Resonalyze.Ui;

namespace Resonalyze.Ui.Dialogs;

internal partial class MeasurementHistoryWindow : Form
{
    private readonly List<Guid> rowEntryIds = [];
    private readonly Font activeEntryFont;
    private readonly ToolTip historyToolTip = new() { ShowAlways = true };
    private Guid? activeEntryId;
    private bool suppressSelectionEvents;

    public event Action<Guid>? EntryActivated;
    public event Action<Guid>? SaveRequested;
    public event Action<Guid>? DeleteRequested;
    public event Action? NewSessionRequested;

    public MeasurementHistoryWindow()
    {
        InitializeComponent();
        activeEntryFont = new Font(Font, FontStyle.Bold);
        StartPosition = FormStartPosition.CenterParent;
        ConfigureNewSessionButton();
        ConfigureGrid();
        FormClosed += (_, _) =>
        {
            historyToolTip.Dispose();
            activeEntryFont.Dispose();
        };
        historyDataGridView.SelectionChanged += HistoryDataGridView_SelectionChanged;
        historyDataGridView.CellContentClick += HistoryDataGridView_CellContentClick;
        historyDataGridView.CellDoubleClick += HistoryDataGridView_CellDoubleClick;
        Resize += (_, _) => ConfigureGridColumns();
    }

    public Guid? SelectedEntryId =>
        historyDataGridView.CurrentRow is { Index: >= 0 } currentRow &&
        currentRow.Index < rowEntryIds.Count
            ? rowEntryIds[currentRow.Index]
            : null;

    public void SetEntries(
        IReadOnlyList<MeasurementHistoryEntry> entries,
        Guid? selectedEntryId = null,
        Guid? activeEntryId = null)
    {
        this.activeEntryId = activeEntryId;
        historyDataGridView.SuspendLayout();
        // Rows.Clear/Add/SelectRow each raise SelectionChanged, and every one
        // rebuilt the whole preview plot; the explicit pass below is enough.
        suppressSelectionEvents = true;
        try
        {
            historyDataGridView.Rows.Clear();
            rowEntryIds.Clear();

            foreach (MeasurementHistoryEntry entry in entries)
            {
                int rowIndex = historyDataGridView.Rows.Add(
                    entry.IsFileBacked ? "FILE" : "RAM",
                    entry.FileNameOrDisplayName,
                    entry.CanSave ? "Save" : "-",
                    "Delete");
                DataGridViewRow row = historyDataGridView.Rows[rowIndex];
                row.Tag = entry;
                row.Cells[0].ToolTipText = entry.IsFileBacked
                    ? "Saved IR file remembered across sessions."
                    : "In-memory snapshot from this session only.";
                row.Cells[1].ToolTipText = entry.Metadata.BuildToolTipText(entry.Timestamp);
                row.Cells[2].ToolTipText = entry.CanSave
                    ? "Save this measurement snapshot to an IR JSON file."
                    : "This history entry is already backed by a file.";
                row.Cells[3].ToolTipText = "Remove this item from history. The file on disk is not deleted.";
                rowEntryIds.Add(entry.Id);
            }

            SelectRow(selectedEntryId ?? entries.FirstOrDefault()?.Id);
            ApplyRowStyles();
        }
        finally
        {
            suppressSelectionEvents = false;
            historyDataGridView.ResumeLayout();
        }

        UpdatePreview();
    }

    public void RemoveEntry(Guid entryId)
    {
        int rowIndex = rowEntryIds.FindIndex(id => id == entryId);
        if (rowIndex < 0)
        {
            return;
        }

        int nextSelection = Math.Min(rowIndex, historyDataGridView.Rows.Count - 2);
        suppressSelectionEvents = true;
        try
        {
            historyDataGridView.Rows.RemoveAt(rowIndex);
            rowEntryIds.RemoveAt(rowIndex);

            if (nextSelection >= 0 && nextSelection < historyDataGridView.Rows.Count)
            {
                historyDataGridView.ClearSelection();
                historyDataGridView.Rows[nextSelection].Selected = true;
                historyDataGridView.CurrentCell = historyDataGridView.Rows[nextSelection].Cells[0];
            }
        }
        finally
        {
            suppressSelectionEvents = false;
        }

        ApplyRowStyles();
        UpdatePreview();
    }

    private void ConfigureNewSessionButton()
    {
        buttonNewSession.FlatAppearance.BorderColor = UiPalette.DialogBorder;
        buttonNewSession.BackColor = UiPalette.ControlSurface;
        buttonNewSession.ForeColor = UiPalette.TextPrimary;
        buttonNewSession.FlatAppearance.MouseOverBackColor = UiPalette.ButtonPressedBackground;
        historyToolTip.SetToolTip(
            buttonNewSession,
            "Start a fresh session: reset all mode settings to defaults and clear " +
            "the current measurement and overlays. Audio device and routing " +
            "settings are kept. The active history entry is saved first; the " +
            "history list and saved files are not removed.");
    }

    private void ButtonNewSession_Click(object? sender, EventArgs e) =>
        NewSessionRequested?.Invoke();

    private void ConfigureGrid()
    {
        historyDataGridView.EnableHeadersVisualStyles = false;
        historyDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        historyDataGridView.GridColor = UiPalette.DialogBorder;
        historyDataGridView.DefaultCellStyle.BackColor = UiPalette.DialogBackground;
        historyDataGridView.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        historyDataGridView.DefaultCellStyle.SelectionBackColor = UiPalette.ButtonPressedBackground;
        historyDataGridView.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        historyDataGridView.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.ControlSurface;
        historyDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        historyDataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.ControlSurface;
        historyDataGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        historyDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        historyDataGridView.ColumnHeadersHeight = ScaleLogical(26);
        historyDataGridView.RowTemplate.Height = ScaleLogical(24);
        foreach (DataGridViewColumn column in historyDataGridView.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        historyDataGridView.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        historyDataGridView.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        historyDataGridView.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        ConfigureGridColumns();
    }

    private void ConfigureGridColumns()
    {
        if (historyDataGridView.Columns.Count < 4)
        {
            return;
        }

        historyDataGridView.Columns[0].MinimumWidth = ScaleLogical(48);
        historyDataGridView.Columns[0].FillWeight = 14;
        historyDataGridView.Columns[1].MinimumWidth = ScaleLogical(140);
        historyDataGridView.Columns[1].FillWeight = 54;
        historyDataGridView.Columns[2].MinimumWidth = ScaleLogical(60);
        historyDataGridView.Columns[2].FillWeight = 15;
        historyDataGridView.Columns[3].MinimumWidth = ScaleLogical(70);
        historyDataGridView.Columns[3].FillWeight = 17;
    }

    private void SelectRow(Guid? entryId)
    {
        historyDataGridView.ClearSelection();
        if (!entryId.HasValue)
        {
            return;
        }

        int index = rowEntryIds.FindIndex(id => id == entryId.Value);
        if (index < 0 || index >= historyDataGridView.Rows.Count)
        {
            return;
        }

        historyDataGridView.Rows[index].Selected = true;
        historyDataGridView.CurrentCell = historyDataGridView.Rows[index].Cells[0];
    }

    private void HistoryDataGridView_SelectionChanged(object? sender, EventArgs e)
    {
        if (suppressSelectionEvents)
        {
            return;
        }

        ApplyRowStyles();
        UpdatePreview();
    }

    private void HistoryDataGridView_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rowEntryIds.Count)
        {
            return;
        }

        Guid entryId = rowEntryIds[e.RowIndex];
        if (e.ColumnIndex == 2)
        {
            // File-backed rows render "-" here; the click must stay inert.
            if (historyDataGridView.Rows[e.RowIndex].Tag is
                MeasurementHistoryEntry { CanSave: true })
            {
                SaveRequested?.Invoke(entryId);
            }
        }
        else if (e.ColumnIndex == 3)
        {
            DeleteRequested?.Invoke(entryId);
        }
    }

    private void HistoryDataGridView_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= rowEntryIds.Count)
        {
            return;
        }

        EntryActivated?.Invoke(rowEntryIds[e.RowIndex]);
    }

    private void UpdatePreview()
    {
        if (historyDataGridView.CurrentRow?.Tag is not MeasurementHistoryEntry entry)
        {
            FRPlotView.Model = CreateEmptyPlotModel();
            FRPlotView.InvalidatePlot(true);
            return;
        }

        PlotModel model = CreatePreviewPlotModel();
        AddFrequencyAxis(model);
        AddDecibelAxis(model);

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 155, 0),
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };

        foreach (var point in entry.Preview.ToSignalPoints())
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

        model.Series.Add(series);
        FRPlotView.Model = model;
        FRPlotView.InvalidatePlot(true);
    }

    private void ApplyRowStyles()
    {
        for (int i = 0; i < historyDataGridView.Rows.Count && i < rowEntryIds.Count; i++)
        {
            DataGridViewRow row = historyDataGridView.Rows[i];
            if (row.Tag is not MeasurementHistoryEntry entry)
            {
                continue;
            }

            bool isActive = activeEntryId.HasValue && rowEntryIds[i] == activeEntryId.Value;
            row.DefaultCellStyle.BackColor = isActive
                ? UiPalette.AccentBlueMuted
                : UiPalette.DialogBackground;
            row.DefaultCellStyle.ForeColor = isActive
                ? UiPalette.TextBright
                : UiPalette.TextPrimary;
            row.DefaultCellStyle.SelectionBackColor = isActive
                ? UiPalette.AccentBlueStrong
                : UiPalette.ButtonPressedBackground;
            row.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
            row.DefaultCellStyle.Font = isActive ? activeEntryFont : Font;

            row.Cells[0].Style.ForeColor = entry.IsFileBacked
                ? UiPalette.SuccessGreenAlt
                : UiPalette.WarningAmber;
            row.Cells[0].Style.SelectionForeColor = row.Cells[0].Style.ForeColor;
        }
    }

    private static PlotModel CreateEmptyPlotModel()
    {
        PlotModel model = CreatePreviewPlotModel();
        AddFrequencyAxis(model);
        AddDecibelAxis(model);
        return model;
    }

    private static PlotModel CreatePreviewPlotModel() =>
        new()
        {
            Background = OxyColor.FromRgb(32, 36, 46),
            PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
            TextColor = OxyColors.White
        };

    private static void AddFrequencyAxis(PlotModel model)
    {
        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = 20,
            AbsoluteMaximum = 20000,
            Minimum = 20,
            Maximum = 20000,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White
        });
    }

    private static void AddDecibelAxis(PlotModel model)
    {
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = -120,
            AbsoluteMaximum = 10,
            MajorStep = 10,
            Minimum = -90,
            Maximum = 0,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            Title = "dB",
            IsPanEnabled = false,
            IsZoomEnabled = false
        });
    }

    private int ScaleLogical(int value) =>
        Math.Max(1, (int)Math.Round(value * DeviceDpi / 96.0));
}
