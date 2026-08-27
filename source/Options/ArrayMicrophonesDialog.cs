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
        buttonAdd.Enabled = free.Count > 0;
    }

    private void Add()
    {
        if (comboBoxInput.SelectedItem is not InputChannelOption { Offset: int channel })
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
                    $"Input {microphone.ChannelOffset + 1}",
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
        int free = FreeChannels(SelectedIndex).Count;
        labelStatus.Text = availableChannels.Count == 0
            ? $"No inputs to record an array from ({channelSourceHint})."
            : free > 0
                ? $"{microphones.Count} configured, {free} further input(s) free ({channelSourceHint})."
                : $"{microphones.Count} configured; every input is in use ({channelSourceHint}).";
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
