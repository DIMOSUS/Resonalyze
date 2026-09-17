using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Edits further array microphones on the measurement's own interface (no driver, rate or clock to pick).</summary>
internal sealed partial class ArrayMicrophonesDialog : Form
{
    private readonly List<ArrayMicrophoneDefinition> microphones;
    private readonly IReadOnlyList<MicrophoneCalibrationEntry> calibrations;
    private readonly IReadOnlyList<int> availableChannels;
    private readonly int microphoneChannel;
    private readonly int? loopbackChannel;
    private readonly string channelSourceHint;
    private bool refreshing;

    /// <param name="availableChannels">Includes the two measurement inputs; filtered here so the reason a channel is missing shows.</param>
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
        // System-drawn column headers ignore dark colours; only they are owner-drawn.
        listViewMicrophones.OwnerDraw = true;
        listViewMicrophones.DrawColumnHeader += DrawColumnHeader;
        listViewMicrophones.DrawItem += (_, e) => e.DrawDefault = true;
        listViewMicrophones.DrawSubItem += (_, e) => e.DrawDefault = true;

        MicrophoneCalibrationComboHelper.Configure(comboBoxCalibration, null, calibrations);
        RefreshList(selectedIndex: -1);
    }

    public IReadOnlyList<ArrayMicrophoneDefinition> Microphones => microphones;

    // A channel used twice would weigh double in the spatial average. excludingIndex = the microphone being edited;
    // null for a new one (see <see cref="UpdateAddAvailability"/>).
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

    /// <remarks>The selected row's own input is on offer for editing, so a second Add made a duplicate that settings silently drop.</remarks>
    private void UpdateAddAvailability() =>
        buttonAdd.Enabled =
            comboBoxInput.SelectedItem is InputChannelOption { Offset: int channel } &&
            FreeChannels(excludingIndex: null).Contains(channel);

    private void Add()
    {
        if (comboBoxInput.SelectedItem is not InputChannelOption { Offset: int channel } ||
            // The check is the invariant; the disabled button is only its display.
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
        // What a new microphone could take: the status is read as the Add button's answer.
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

    /// <remarks>Arrived at by moving the mic or loopback later; the measurement layer drops it, so name it here.</remarks>
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
            return "Off";
        }

        MicrophoneCalibrationEntry? entry = calibrations
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                calibrationId,
                StringComparison.OrdinalIgnoreCase));
        // A removed calibration shows as missing, not "None": different fix.
        return entry?.Name ?? $"{calibrationId} (missing)";
    }

    private static string? NormalizeNote(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
