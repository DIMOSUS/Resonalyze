using System.IO;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>
/// Manages the calibrations beside the microphone's own 0° file: further files
/// and curves estimated for an angle of incidence. Edits are made on a working
/// copy and only handed back when the dialog is accepted.
/// </summary>
internal sealed partial class MicrophoneCalibrationsDialog : Form
{
    private readonly List<MicrophoneCalibrationDefinition> definitions;
    private readonly string? zeroDegreePath;
    private readonly Func<string?, string?> selectCalibrationFile;

    public MicrophoneCalibrationsDialog(
        IReadOnlyList<MicrophoneCalibrationDefinition> definitions,
        string? zeroDegreePath,
        Func<string?, string?> selectCalibrationFile)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(selectCalibrationFile);
        this.definitions = definitions
            .Select(definition => definition.Clone())
            .ToList();
        this.zeroDegreePath = zeroDegreePath;
        this.selectCalibrationFile = selectCalibrationFile;
        InitializeComponent();

        buttonAddFile.Click += (_, _) => AddFile();
        buttonAddAngle.Click += (_, _) => AddAngle();
        buttonEdit.Click += (_, _) => EditSelected();
        buttonRename.Click += (_, _) => RenameSelected();
        buttonRemove.Click += (_, _) => RemoveSelected();
        listViewCalibrations.SelectedIndexChanged += (_, _) => UpdateButtonState();
        listViewCalibrations.DoubleClick += (_, _) => EditSelected();
        listViewCalibrations.AfterLabelEdit += ListViewAfterLabelEdit;
        // Column headers are drawn by the system and ignore the control's dark
        // colours; only they are taken over, the rows keep the default drawing.
        listViewCalibrations.OwnerDraw = true;
        listViewCalibrations.DrawColumnHeader += DrawColumnHeader;
        listViewCalibrations.DrawItem += (_, e) => e.DrawDefault = true;
        listViewCalibrations.DrawSubItem += (_, e) => e.DrawDefault = true;
        RefreshList(selectedId: null);
    }

    /// <summary>The edited list; valid once the dialog returned OK.</summary>
    public IReadOnlyList<MicrophoneCalibrationDefinition> Definitions => definitions;

    private void AddFile()
    {
        string? path = selectCalibrationFile(null);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var definition = new MicrophoneCalibrationDefinition
        {
            Id = MicrophoneCalibrationDefinition.CreateId(definitions),
            Name = Path.GetFileNameWithoutExtension(path),
            Kind = MicrophoneCalibrationKind.File,
            Path = path
        };
        definition.Normalize();
        definitions.Add(definition);
        RefreshList(definition.Id);
    }

    private void AddAngle()
    {
        var definition = new MicrophoneCalibrationDefinition
        {
            Id = MicrophoneCalibrationDefinition.CreateId(definitions),
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = 90.0
        };
        definition.Name = MicrophoneCalibrationDefinition.FormatAngleName(
            definition.AngleDegrees);
        if (!EditAngle(definition))
        {
            return;
        }

        definitions.Add(definition);
        RefreshList(definition.Id);
    }

    private void EditSelected()
    {
        if (SelectedDefinition is not { } definition)
        {
            return;
        }

        if (definition.Kind == MicrophoneCalibrationKind.Angle)
        {
            if (EditAngle(definition))
            {
                RefreshList(definition.Id);
            }

            return;
        }

        string? path = selectCalibrationFile(definition.Path);
        if (string.IsNullOrWhiteSpace(path) || path == definition.Path)
        {
            return;
        }

        definition.Path = path;
        definition.Normalize();
        RefreshList(definition.Id);
    }

    private bool EditAngle(MicrophoneCalibrationDefinition definition)
    {
        // Only file-backed entries may be a base, and never the entry itself, so
        // an estimate can never be derived from another estimate.
        List<MicrophoneCalibrationDefinition> baseCandidates = definitions
            .Where(candidate =>
                candidate.Kind == MicrophoneCalibrationKind.File &&
                !string.Equals(candidate.Id, definition.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        using var dialog = new AngleCalibrationDialog(definition, baseCandidates);
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void RenameSelected()
    {
        if (listViewCalibrations.SelectedItems.Count == 1)
        {
            listViewCalibrations.SelectedItems[0].BeginEdit();
        }
    }

    private void RemoveSelected()
    {
        if (SelectedDefinition is not { } definition)
        {
            return;
        }

        // Removing a base would leave the estimates derived from it pointing at
        // nothing; they fall back to the microphone's own 0° calibration, which
        // is what an estimate means when it names no base of its own.
        foreach (MicrophoneCalibrationDefinition derived in definitions)
        {
            if (string.Equals(derived.BaseId, definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                derived.BaseId = null;
            }
        }

        definitions.Remove(definition);
        RefreshList(selectedId: null);
    }

    private void ListViewAfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        string? name = e.Label?.Trim();
        if (string.IsNullOrEmpty(name) ||
            listViewCalibrations.Items[e.Item].Tag is not string id ||
            Find(id) is not { } definition)
        {
            // Rejecting the edit restores the previous text, so an empty rename
            // cannot leave a nameless entry in the selectors.
            e.CancelEdit = true;
            return;
        }

        definition.Name = name;
    }

    private MicrophoneCalibrationDefinition? SelectedDefinition =>
        listViewCalibrations.SelectedItems.Count == 1 &&
        listViewCalibrations.SelectedItems[0].Tag is string id
            ? Find(id)
            : null;

    private MicrophoneCalibrationDefinition? Find(string id) =>
        definitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));

    private void RefreshList(string? selectedId)
    {
        listViewCalibrations.BeginUpdate();
        try
        {
            listViewCalibrations.Items.Clear();
            foreach (MicrophoneCalibrationDefinition definition in definitions)
            {
                var item = new ListViewItem(definition.Name) { Tag = definition.Id };
                item.SubItems.Add(
                    definition.Kind == MicrophoneCalibrationKind.Angle ? "Angle" : "File");
                item.SubItems.Add(Describe(definition));
                item.SubItems.Add(DescribeStatus(definition));
                item.Selected = string.Equals(
                    definition.Id,
                    selectedId,
                    StringComparison.OrdinalIgnoreCase);
                listViewCalibrations.Items.Add(item);
            }

            // Widths follow the content instead of a fixed pixel count, so the
            // columns stay readable at any DPI and in any font scaling.
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

    private string Describe(MicrophoneCalibrationDefinition definition)
    {
        if (definition.Kind == MicrophoneCalibrationKind.File)
        {
            return definition.Path is { } path ? Path.GetFileName(path) : "no file";
        }

        string baseName = definition.BaseId is { } baseId && Find(baseId) is { } baseDefinition
            ? baseDefinition.Name
            : "0°";
        string model = definition.Reference == MicrophoneAngleReference.SonarworksXref20
            ? "Sonarworks XREF 20"
            : $"{definition.FrontDiameterMm:0.##} mm, {DescribeGrid(definition.Grid)}";
        return $"{definition.AngleDegrees:0.#}° from {baseName} · {model}";
    }

    private string DescribeStatus(MicrophoneCalibrationDefinition definition)
    {
        if (definition.Kind == MicrophoneCalibrationKind.File)
        {
            if (string.IsNullOrWhiteSpace(definition.Path))
            {
                return "no file selected";
            }

            var probe = new CalibrationFile(definition.Path);
            return probe.HasData ? "ready" : "unusable file";
        }

        string? basePath = definition.BaseId is { } baseId
            ? Find(baseId)?.Path
            : zeroDegreePath;
        if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath))
        {
            return "base calibration missing";
        }

        return new CalibrationFile(basePath).HasData
            ? "estimated"
            : "unusable base file";
    }

    private void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(Color.FromArgb(45, 50, 60));
        e.Graphics.FillRectangle(background, e.Bounds);
        using var separator = new Pen(Color.FromArgb(70, 76, 92));
        e.Graphics.DrawLine(
            separator,
            e.Bounds.Right - 1,
            e.Bounds.Top + 2,
            e.Bounds.Right - 1,
            e.Bounds.Bottom - 3);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            e.Font ?? listViewCalibrations.Font,
            Rectangle.Inflate(e.Bounds, -6, 0),
            Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static string DescribeGrid(MicrophoneProtectionGrid grid) => grid switch
    {
        MicrophoneProtectionGrid.Fitted => "grid fitted",
        MicrophoneProtectionGrid.Removed => "grid removed",
        _ => "grid unknown"
    };
}
