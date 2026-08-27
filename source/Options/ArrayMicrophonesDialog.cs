using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>
/// Edits the array of further microphones recorded alongside the measurement
/// one. Works on a copy; <see cref="Microphones"/> is the edited list once the
/// dialog returned OK.
/// </summary>
/// <remarks>
/// It offers a channel and a calibration and nothing else, and that is the whole
/// point of the array being channels of the SAME interface: there is no driver
/// to pick, no sample rate to agree on and no clock to reconcile, because every
/// array microphone rides the measurement's own session.
/// </remarks>
internal sealed partial class ArrayMicrophonesDialog : Form
{
    private readonly List<ArrayMicrophoneDefinition> microphones;
    private readonly IReadOnlyList<MicrophoneCalibrationEntry> calibrations;
    private readonly IReadOnlyList<int> availableChannels;
    private readonly int microphoneChannel;
    private readonly int? loopbackChannel;
    private readonly string channelSourceHint;
    private bool refreshing;

    /// <param name="availableChannels">
    /// Every input the current backend can actually record, INCLUDING the two the
    /// measurement already uses — they are filtered here, so the reason a channel
    /// is missing can be told apart from the device simply not having it.
    /// </param>
    /// <param name="channelSourceHint">
    /// Where that channel count came from, for the status line. A user whose
    /// interface presents itself to WASAPI as a stereo pair needs to be told that
    /// its further inputs are unreachable this way rather than left guessing.
    /// </param>
    public ArrayMicrophonesDialog(
        IReadOnlyList<ArrayMicrophoneDefinition> microphones,
        IReadOnlyList<MicrophoneCalibrationEntry> calibrations,
        IReadOnlyList<int> availableChannels,
        int microphoneChannel,
        int? loopbackChannel,
        string channelSourceHint)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        ArgumentNullException.ThrowIfNull(calibrations);
        ArgumentNullException.ThrowIfNull(availableChannels);
        this.microphones = microphones
            .Select(microphone => microphone.Clone())
            .ToList();
        this.calibrations = calibrations;
        this.availableChannels = availableChannels;
        this.microphoneChannel = microphoneChannel;
        this.loopbackChannel = loopbackChannel;
        this.channelSourceHint = channelSourceHint ?? string.Empty;
        InitializeComponent();

        buttonAdd.Click += (_, _) => Add();
        buttonUpdate.Click += (_, _) => UpdateSelected();
        buttonRemove.Click += (_, _) => RemoveSelected();
        comboBoxInput.SelectedIndexChanged += (_, _) => UpdateAddAvailability();
        listViewMicrophones.SelectedIndexChanged += (_, _) => LoadSelectionIntoEditor();
        // Column headers are drawn by the system and ignore the control's dark
        // colours; only they are taken over, the rows keep the default drawing.
        listViewMicrophones.OwnerDraw = true;
        listViewMicrophones.DrawColumnHeader += DrawColumnHeader;
        listViewMicrophones.DrawItem += (_, e) => e.DrawDefault = true;
        listViewMicrophones.DrawSubItem += (_, e) => e.DrawDefault = true;

        MicrophoneCalibrationComboHelper.Configure(comboBoxCalibration, null, calibrations);
        RefreshList(selectedIndex: -1);
    }

    /// <summary>The edited array; valid once the dialog returned OK.</summary>
    public IReadOnlyList<ArrayMicrophoneDefinition> Microphones => microphones;

    // The inputs still free: the measurement microphone and the loopback are in
    // use, and one already assigned to another array microphone would enter the
    // spatial average twice and weigh double.
    //
    // excludingIndex names the microphone being EDITED, whose own input is free for
    // it. Pass null for the question a new microphone asks — see
    // <see cref="UpdateAddAvailability"/>, where the difference between the two is
    // what stops Add from making a duplicate.
    private List<int> FreeChannels(int? excludingIndex)
    {
        var taken = new HashSet<int> { microphoneChannel };
        if (loopbackChannel is int loopback)
        {
            taken.Add(loopback);
        }
        for (int i = 0; i < microphones.Count; i++)
        {
            if (i != excludingIndex)
            {
                taken.Add(microphones[i].ChannelOffset);
            }
        }

        return availableChannels.Where(channel => !taken.Contains(channel)).ToList();
    }

    private void FillChannelCombo(int? excludingIndex, int? preferredChannel)
    {
        List<int> free = FreeChannels(excludingIndex);
        comboBoxInput.Items.Clear();
        foreach (int channel in free)
        {
            comboBoxInput.Items.Add(new InputChannelOption(channel, $"Input {channel + 1}"));
        }

        int index = preferredChannel is int wanted ? free.IndexOf(wanted) : -1;
        comboBoxInput.SelectedIndex = index >= 0 ? index : free.Count > 0 ? 0 : -1;
        comboBoxInput.Enabled = free.Count > 0;
        UpdateAddAvailability();
    }

    /// <summary>
    /// Whether the editor's current input may be given to a NEW microphone.
    /// </summary>
    /// <remarks>
    /// Not the same question as whether the combo has anything in it, and the
    /// difference is a duplicate. Adding selects the new row, which puts its OWN
    /// input back on offer so its calibration can be edited without moving it — and
    /// a second Add on that offer produced a second microphone on the same input.
    /// Nothing downstream said so: the settings layer drops a duplicate silently to
    /// stay able to start on its own file, so the panel went on reporting seven
    /// microphones while six were recorded.
    /// </remarks>
    private void UpdateAddAvailability() =>
        buttonAdd.Enabled =
            comboBoxInput.SelectedItem is InputChannelOption { Offset: int channel } &&
            FreeChannels(excludingIndex: null).Contains(channel);

    private void Add()
    {
        if (comboBoxInput.SelectedItem is not InputChannelOption { Offset: int channel } ||
            // The button is disabled in this state; the check stands anyway, because
            // this is the invariant and the button is only its display.
            !FreeChannels(excludingIndex: null).Contains(channel))
        {
            return;
        }

        microphones.Add(new ArrayMicrophoneDefinition
        {
            ChannelOffset = channel,
            CalibrationId = MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(
                comboBoxCalibration),
            Note = NormalizeNote(textBoxNote.Text)
        });
        RefreshList(microphones.Count - 1);
    }

    private void UpdateSelected()
    {
        if (SelectedIndex is not int index ||
            comboBoxInput.SelectedItem is not InputChannelOption { Offset: int channel })
        {
            return;
        }

        microphones[index].ChannelOffset = channel;
        microphones[index].CalibrationId =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        microphones[index].Note = NormalizeNote(textBoxNote.Text);
        RefreshList(index);
    }

    private void RemoveSelected()
    {
        if (SelectedIndex is not int index)
        {
            return;
        }

        microphones.RemoveAt(index);
        RefreshList(Math.Min(index, microphones.Count - 1));
    }

    private int? SelectedIndex => listViewMicrophones.SelectedIndices.Count > 0
        ? listViewMicrophones.SelectedIndices[0]
        : null;

    private void LoadSelectionIntoEditor()
    {
        if (refreshing)
        {
            return;
        }

        if (SelectedIndex is not int index)
        {
            buttonUpdate.Enabled = false;
            buttonRemove.Enabled = false;
            FillChannelCombo(excludingIndex: null, preferredChannel: null);
            return;
        }

        ArrayMicrophoneDefinition microphone = microphones[index];
        buttonUpdate.Enabled = true;
        buttonRemove.Enabled = true;
        // The selected microphone's own channel is free FOR IT, so editing its
        // calibration without moving it is possible.
        FillChannelCombo(index, microphone.ChannelOffset);
        MicrophoneCalibrationComboHelper.Configure(
            comboBoxCalibration,
            microphone.CalibrationId,
            calibrations);
        textBoxNote.Text = microphone.Note ?? string.Empty;
    }

    private void RefreshList(int selectedIndex)
    {
        refreshing = true;
        try
        {
            listViewMicrophones.BeginUpdate();
            listViewMicrophones.Items.Clear();
            foreach (ArrayMicrophoneDefinition microphone in microphones)
            {
                listViewMicrophones.Items.Add(new ListViewItem(
                [
                    DescribeInput(microphone.ChannelOffset),
                    DescribeCalibration(microphone.CalibrationId),
                    microphone.Note ?? string.Empty
                ]));
            }

            listViewMicrophones.EndUpdate();
            if (selectedIndex >= 0 && selectedIndex < listViewMicrophones.Items.Count)
            {
                listViewMicrophones.Items[selectedIndex].Selected = true;
                listViewMicrophones.Items[selectedIndex].Focused = true;
            }
        }
        finally
        {
            refreshing = false;
        }

        UpdateStatus();
        LoadSelectionIntoEditor();
    }

    private void UpdateStatus()
    {
        // What a NEW microphone could take, not what the selected one may keep: the
        // status sits under the Add button and is read as an answer to it.
        int free = FreeChannels(excludingIndex: null).Count;
        int conflicting = microphones.Count(Conflicts);
        string conflict = conflicting == 0
            ? string.Empty
            : conflicting == 1
                ? " 1 of them cannot be recorded — see the list."
                : $" {conflicting} of them cannot be recorded — see the list.";
        labelStatus.Text = availableChannels.Count == 0
            ? $"No inputs to record an array from ({channelSourceHint})."
            : free > 0
                ? $"{microphones.Count} configured, {free} further input(s) free ({channelSourceHint}).{conflict}"
                : $"{microphones.Count} configured; every input is in use ({channelSourceHint}).{conflict}";
    }

    /// <summary>
    /// Whether this microphone sits on an input the measurement itself has taken.
    /// </summary>
    /// <remarks>
    /// Impossible to configure here, and perfectly possible to arrive at: the array
    /// is stored per backend and the measurement microphone or the loopback can be
    /// moved onto one of its inputs afterwards, in a different part of the panel.
    /// The measurement layer then drops it — one recorded position fewer than the
    /// button promises — so the collision is named where it can be acted on rather
    /// than left to be inferred from a curve that never appeared.
    /// </remarks>
    private bool Conflicts(ArrayMicrophoneDefinition microphone) =>
        microphone.ChannelOffset == microphoneChannel ||
        microphone.ChannelOffset == loopbackChannel;

    private string DescribeInput(int channelOffset)
    {
        string input = $"Input {channelOffset + 1}";
        if (channelOffset == microphoneChannel)
        {
            return $"{input} (the measurement microphone)";
        }

        return channelOffset == loopbackChannel ? $"{input} (the loopback)" : input;
    }

    private string DescribeCalibration(string? calibrationId)
    {
        if (MicrophoneCalibrationIds.IsOff(calibrationId))
        {
            // The same word the selector uses for it, so the table and the editor
            // below it are not two vocabularies for one state.
            return "Off";
        }

        MicrophoneCalibrationEntry? entry = calibrations
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                calibrationId,
                StringComparison.OrdinalIgnoreCase));
        // A calibration that has since been removed keeps its id on screen rather
        // than reading "None": the microphone is not uncalibrated, its
        // calibration is missing, and those want different fixes.
        return entry?.Name ?? $"{calibrationId} (missing)";
    }

    private static string? NormalizeNote(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
            e.Font ?? listViewMicrophones.Font,
            Rectangle.Inflate(e.Bounds, -6, 0),
            Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
