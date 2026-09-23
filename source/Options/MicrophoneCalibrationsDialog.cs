using Resonalyze.Ui;

namespace Resonalyze.Options;

/// <summary>Edits a working copy of the additional calibrations (<see cref="MicrophoneCalibrationsSession"/>), handed
/// back on OK.</summary>
internal sealed partial class MicrophoneCalibrationsDialog : Form
{
    private readonly MicrophoneCalibrationsSession session;
    private readonly CalibrationFileProbe probe = new();
    private readonly Func<string?, string?> selectCalibrationFile;

    public MicrophoneCalibrationsDialog(
        IReadOnlyList<MicrophoneCalibrationDefinition> definitions,
        string? zeroDegreePath,
        Func<string?, string?> selectCalibrationFile)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(selectCalibrationFile);
        session = new MicrophoneCalibrationsSession(definitions, zeroDegreePath);
        this.selectCalibrationFile = selectCalibrationFile;
        InitializeComponent();

        buttonAddFile.Click += (_, _) => AddFile();
        buttonAddAngle.Click += (_, _) => AddAngle();
        buttonEdit.Click += (_, _) => EditSelected();
        buttonRename.Click += (_, _) => RenameSelected();
        buttonRemove.Click += (_, _) => RemoveSelected();
        listViewCalibrations.SelectedIndexChanged += (_, _) => UpdateButtonState();
        listViewCalibrations.DoubleClick += (_, _) => EditSelected();
        listViewCalibrations.AfterLabelEdit += (_, e) =>
            e.CancelEdit = !session.Rename(listViewCalibrations.Items[e.Item].Tag as string, e.Label);
        ThemedListViewHeaders.Apply(listViewCalibrations);
        RefreshList(selectedId: null);
    }

    public IReadOnlyList<MicrophoneCalibrationDefinition> Definitions => session.Definitions;

    private void AddFile()
    {
        if (session.AddFile(selectCalibrationFile(null)) is not { } definition)
        {
            return;
        }

        RefreshList(definition.Id);
        // The file name is only a suggestion (the maker's download name), so open straight into rename.
        BeginRename(definition.Id);
    }

    private void AddAngle()
    {
        MicrophoneCalibrationDefinition definition = session.NewAngle();
        if (!EditAngle(definition))
        {
            return;
        }

        session.Add(definition);
        RefreshList(definition.Id);
    }

    private void EditSelected()
    {
        if (SelectedDefinition is not { } definition)
        {
            return;
        }

        bool changed = definition.Kind == MicrophoneCalibrationKind.Angle
            ? EditAngle(definition)
            : session.SetPath(definition, selectCalibrationFile(definition.Path));
        if (changed)
        {
            RefreshList(definition.Id);
        }
    }

    private bool EditAngle(MicrophoneCalibrationDefinition definition)
    {
        using var dialog = new AngleCalibrationDialog(definition, session.BaseCandidates(definition));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void RenameSelected()
    {
        if (listViewCalibrations.SelectedItems.Count == 1)
        {
            BeginRename(listViewCalibrations.SelectedItems[0]);
        }
    }

    private void BeginRename(string id)
    {
        foreach (ListViewItem item in listViewCalibrations.Items)
        {
            if (item.Tag is string itemId &&
                string.Equals(itemId, id, StringComparison.OrdinalIgnoreCase))
            {
                BeginRename(item);
                return;
            }
        }
    }

    private void BeginRename(ListViewItem item)
    {
        // The edit box belongs to the list, and callers arrive with focus elsewhere.
        listViewCalibrations.Focus();
        item.BeginEdit();
    }

    private void RemoveSelected()
    {
        if (SelectedDefinition is not { } definition)
        {
            return;
        }

        session.Remove(definition);
        RefreshList(selectedId: null);
    }

    private MicrophoneCalibrationDefinition? SelectedDefinition =>
        listViewCalibrations.SelectedItems.Count == 1
            ? session.Find(listViewCalibrations.SelectedItems[0].Tag as string)
            : null;

    private void RefreshList(string? selectedId)
    {
        listViewCalibrations.BeginUpdate();
        try
        {
            listViewCalibrations.Items.Clear();
            foreach (MicrophoneCalibrationRow row in MicrophoneCalibrationRows.Read(session, probe))
            {
                var item = new ListViewItem(row.Name) { Tag = row.Id };
                item.SubItems.Add(row.Kind);
                item.SubItems.Add(row.Details);
                item.SubItems.Add(row.Status);
                item.Selected = string.Equals(row.Id, selectedId, StringComparison.OrdinalIgnoreCase);
                listViewCalibrations.Items.Add(item);
            }

            foreach (ColumnHeader column in listViewCalibrations.Columns)
            {
                column.Width = -2;
            }
        }
        finally
        {
            listViewCalibrations.EndUpdate();
        }

        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        bool hasSelection = listViewCalibrations.SelectedItems.Count == 1;
        buttonEdit.Enabled = hasSelection;
        buttonRename.Enabled = hasSelection;
        buttonRemove.Enabled = hasSelection;
    }
}
