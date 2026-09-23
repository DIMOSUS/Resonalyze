using System.Windows.Forms;

namespace Resonalyze.Options;

/// <summary>Edits further array microphones on the measurement's own interface (no driver, rate or clock to pick); the
/// working copy and the editor are an <see cref="ArrayMicrophonesSession"/>.</summary>
internal sealed partial class ArrayMicrophonesDialog : Form
{
    private readonly ArrayMicrophonesSession session;
    private bool presenting;
    private int shownListVersion = -1;
    private int shownEditorVersion = -1;
    private IReadOnlyList<int>? shownChannels;
    private IReadOnlyList<MicrophoneCalibrationOption>? shownOptions;

    /// <param name="availableChannels">Includes the two measurement inputs; filtered here so the reason a channel is missing shows.</param>
    public ArrayMicrophonesDialog(
        IReadOnlyList<ArrayMicrophoneDefinition> microphones,
        IReadOnlyList<MicrophoneCalibrationEntry> calibrations,
        IReadOnlyList<int> availableChannels,
        int microphoneChannel,
        int? loopbackChannel,
        string channelSourceHint)
    {
        session = new ArrayMicrophonesSession(
            microphones,
            calibrations,
            availableChannels,
            microphoneChannel,
            loopbackChannel,
            channelSourceHint);
        InitializeComponent();

        buttonAdd.Click += (_, _) => Edit(() => session.Add());
        buttonUpdate.Click += (_, _) => Edit(() => session.Update());
        buttonRemove.Click += (_, _) => Edit(() => session.Remove());
        comboBoxInput.SelectedIndexChanged += (_, _) =>
            Edit(() => session.SetEditorChannel((comboBoxInput.SelectedItem as InputChannelOption)?.Offset));
        comboBoxCalibration.SelectedIndexChanged += (_, _) =>
            Edit(() => session.SetCalibrationIndex(comboBoxCalibration.SelectedIndex));
        textBoxNote.TextChanged += (_, _) => Edit(() => session.SetNote(textBoxNote.Text));
        listViewMicrophones.SelectedIndexChanged += (_, _) => Edit(() => session.Select(
            listViewMicrophones.SelectedIndices.Count > 0 ? listViewMicrophones.SelectedIndices[0] : null));
        // System-drawn column headers ignore dark colours; only they are owner-drawn.
        listViewMicrophones.OwnerDraw = true;
        listViewMicrophones.DrawColumnHeader += DrawColumnHeader;
        listViewMicrophones.DrawItem += (_, e) => e.DrawDefault = true;
        listViewMicrophones.DrawSubItem += (_, e) => e.DrawDefault = true;
        Present();
    }

    public IReadOnlyList<ArrayMicrophoneDefinition> Microphones => session.Microphones;

    private void Edit(Action change)
    {
        if (presenting)
        {
            return;
        }

        change();
        Present();
    }

    private void Present()
    {
        presenting = true;
        try
        {
            if (shownListVersion != session.ListVersion)
            {
                PresentList();
                shownListVersion = session.ListVersion;
            }

            PresentEditor();
            buttonUpdate.Enabled = session.Selected != null;
            buttonRemove.Enabled = session.Selected != null;
            buttonAdd.Enabled = session.CanAdd;
            labelStatus.Text = ArrayMicrophoneRows.Status(session);
        }
        finally
        {
            presenting = false;
        }
    }

    private void PresentList()
    {
        listViewMicrophones.BeginUpdate();
        listViewMicrophones.Items.Clear();
        foreach (ArrayMicrophoneRow row in ArrayMicrophoneRows.Read(session))
        {
            listViewMicrophones.Items.Add(new ListViewItem([row.Input, row.Calibration, row.Note]));
        }

        listViewMicrophones.EndUpdate();
        if (session.Selected is int selected)
        {
            listViewMicrophones.Items[selected].Selected = true;
            listViewMicrophones.Items[selected].Focused = true;
        }
    }

    private void PresentEditor()
    {
        if (!ReferenceEquals(shownChannels, session.EditorChannels))
        {
            comboBoxInput.Items.Clear();
            foreach (int channel in session.EditorChannels)
            {
                comboBoxInput.Items.Add(new InputChannelOption(channel, ArrayMicrophoneRows.InputLabel(channel)));
            }

            shownChannels = session.EditorChannels;
        }

        int input = -1;
        for (int i = 0; i < session.EditorChannels.Count; i++)
        {
            if (session.EditorChannels[i] == session.EditorChannel)
            {
                input = i;
                break;
            }
        }

        comboBoxInput.SelectedIndex = input;
        comboBoxInput.Enabled = session.EditorChannels.Count > 0;
        if (!ReferenceEquals(shownOptions, session.CalibrationOptions))
        {
            comboBoxCalibration.Items.Clear();
            comboBoxCalibration.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (MicrophoneCalibrationOption option in session.CalibrationOptions)
            {
                comboBoxCalibration.Items.Add(option);
            }

            shownOptions = session.CalibrationOptions;
        }

        comboBoxCalibration.SelectedIndex = session.CalibrationIndex;
        comboBoxCalibration.Enabled = session.CalibrationOptions.Count > 1;
        if (shownEditorVersion != session.EditorVersion)
        {
            textBoxNote.Text = session.Note;
            shownEditorVersion = session.EditorVersion;
        }
    }

    private void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(UiPalette.AppBackground);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var separator = new Pen(UiPalette.BorderMuted);
        e.Graphics.DrawLine(
            separator,
            e.Bounds.Right - 1,
            e.Bounds.Top + 2,
            e.Bounds.Right - 1,
            e.Bounds.Bottom - 3);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            e.Font ?? listViewMicrophones.Font,
            Rectangle.Inflate(e.Bounds, -6, 0),
            UiPalette.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
