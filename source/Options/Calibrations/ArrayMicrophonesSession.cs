namespace Resonalyze.Options;

/// <summary>The working copy of the array microphones, the selected row and the editor beside the list, handed back on
/// OK. See docs/tech/sweep-measurement.md#calibration-dialogs-code-map.</summary>
internal sealed class ArrayMicrophonesSession
{
    private readonly List<ArrayMicrophoneDefinition> microphones;

    /// <param name="availableChannels">Includes the two measurement inputs; filtered here so the reason a channel is missing shows.</param>
    public ArrayMicrophonesSession(
        IReadOnlyList<ArrayMicrophoneDefinition> microphones,
        IReadOnlyList<MicrophoneCalibrationEntry> calibrations,
        IReadOnlyList<int> availableChannels,
        int microphoneChannel,
        int? loopbackChannel,
        string? channelSourceHint)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        ArgumentNullException.ThrowIfNull(calibrations);
        ArgumentNullException.ThrowIfNull(availableChannels);
        this.microphones = microphones
            .Select(microphone => microphone.Clone())
            .ToList();
        Calibrations = calibrations;
        AvailableChannels = availableChannels;
        MicrophoneChannel = microphoneChannel;
        LoopbackChannel = loopbackChannel;
        ChannelSourceHint = channelSourceHint ?? string.Empty;
        CalibrationOptions = MicrophoneCalibrationChoices.BuildOptions(null, calibrations);
        FillEditorChannels(excludingIndex: null, preferredChannel: null);
    }

    public IReadOnlyList<ArrayMicrophoneDefinition> Microphones => microphones;

    public IReadOnlyList<MicrophoneCalibrationEntry> Calibrations { get; }

    public IReadOnlyList<int> AvailableChannels { get; }

    public int MicrophoneChannel { get; }

    public int? LoopbackChannel { get; }

    public string ChannelSourceHint { get; }

    public int? Selected { get; private set; }

    /// <summary>Changes whenever a microphone is added, updated or removed, so the list is redrawn only then.</summary>
    public int ListVersion { get; private set; }

    /// <summary>The inputs the editor offers: free ones, plus the selected microphone's own.</summary>
    public IReadOnlyList<int> EditorChannels { get; private set; } = [];

    public int? EditorChannel { get; private set; }

    /// <summary>Rebuilt only when a row is loaded into the editor; a gone calibration keeps its own entry.</summary>
    public IReadOnlyList<MicrophoneCalibrationOption> CalibrationOptions { get; private set; }

    public int CalibrationIndex { get; private set; }

    public string Note { get; private set; } = string.Empty;

    /// <summary>Changes whenever a row is loaded into the editor, so the note field is rewritten only then.</summary>
    public int EditorVersion { get; private set; }

    /// <summary>The Add button's answer: the editor's input must be free for a new microphone, not only for the selected
    /// one, or a second Add makes a duplicate the settings drop.</summary>
    public bool CanAdd => EditorChannel is int channel && FreeChannels(excludingIndex: null).Contains(channel);

    /// <summary>Taken are the measurement's own inputs and every other microphone's; excludingIndex is the one being edited.</summary>
    public IReadOnlyList<int> FreeChannels(int? excludingIndex)
    {
        var taken = new HashSet<int>();
        for (int i = 0; i < microphones.Count; i++)
        {
            if (i != excludingIndex)
            {
                taken.Add(microphones[i].ChannelOffset);
            }
        }

        return AvailableChannels
            .Where(channel =>
                !ArrayChannelRules.IsMeasurementInput(channel, MicrophoneChannel, LoopbackChannel) &&
                !taken.Contains(channel))
            .ToList();
    }

    /// <summary>A microphone the measurement records over is dropped by the measurement layer; the dialog names it.</summary>
    public bool Conflicts(ArrayMicrophoneDefinition microphone) =>
        ArrayChannelRules.IsMeasurementInput(microphone.ChannelOffset, MicrophoneChannel, LoopbackChannel);

    /// <summary>No row keeps the editor's calibration and note, and offers the inputs a new microphone may take.</summary>
    public void Select(int? index)
    {
        Selected = index is int row && row >= 0 && row < microphones.Count ? row : null;
        LoadEditor();
    }

    public void SetEditorChannel(int? channel) => EditorChannel = channel;

    public void SetCalibrationIndex(int index) => CalibrationIndex = index;

    public void SetNote(string? note) => Note = note ?? string.Empty;

    public bool Add()
    {
        if (!CanAdd || EditorChannel is not int channel)
        {
            return false;
        }

        microphones.Add(new ArrayMicrophoneDefinition
        {
            ChannelOffset = channel,
            CalibrationId = EditorCalibrationId,
            Note = NormalizeNote(Note)
        });
        Reload(microphones.Count - 1);
        return true;
    }

    public bool Update()
    {
        if (Selected is not int index || EditorChannel is not int channel)
        {
            return false;
        }

        microphones[index].ChannelOffset = channel;
        microphones[index].CalibrationId = EditorCalibrationId;
        microphones[index].Note = NormalizeNote(Note);
        Reload(index);
        return true;
    }

    public bool Remove()
    {
        if (Selected is not int index)
        {
            return false;
        }

        microphones.RemoveAt(index);
        Reload(Math.Min(index, microphones.Count - 1));
        return true;
    }

    private string? EditorCalibrationId =>
        CalibrationIndex >= 0 && CalibrationIndex < CalibrationOptions.Count
            ? CalibrationOptions[CalibrationIndex].CalibrationId
            : null;

    private void Reload(int selected)
    {
        ListVersion++;
        Select(selected >= 0 ? selected : null);
    }

    private void LoadEditor()
    {
        if (Selected is not int index)
        {
            FillEditorChannels(excludingIndex: null, preferredChannel: null);
            return;
        }

        ArrayMicrophoneDefinition microphone = microphones[index];
        FillEditorChannels(index, microphone.ChannelOffset);
        CalibrationOptions = MicrophoneCalibrationChoices.BuildOptions(microphone.CalibrationId, Calibrations);
        CalibrationIndex = MicrophoneCalibrationChoices.FindIndex(CalibrationOptions, microphone.CalibrationId);
        Note = microphone.Note ?? string.Empty;
        EditorVersion++;
    }

    private void FillEditorChannels(int? excludingIndex, int? preferredChannel)
    {
        IReadOnlyList<int> free = FreeChannels(excludingIndex);
        EditorChannels = free;
        int index = preferredChannel is int wanted ? IndexOf(free, wanted) : -1;
        EditorChannel = index >= 0 ? free[index] : free.Count > 0 ? free[0] : null;
    }

    private static int IndexOf(IReadOnlyList<int> channels, int channel)
    {
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i] == channel)
            {
                return i;
            }
        }

        return -1;
    }

    private static string? NormalizeNote(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
