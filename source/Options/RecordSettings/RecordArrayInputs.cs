namespace Resonalyze.Options;

/// <summary>The inputs further array microphones can use on the selected route, and which of them a run records.</summary>
internal static class RecordArrayInputs
{
    /// <summary>Recordable inputs and where the list came from, since "no room for an array" has several causes.</summary>
    public static (IReadOnlyList<int> Channels, string Source) InputChannels(RecordSettingsSession session)
    {
        AudioBackend backend = session.SelectedBackend;
        if (backend == AudioBackend.Asio)
        {
            int[] asio = session.AsioDriverInfo.InputChannels
                .Select(channel => channel.Offset)
                .ToArray();
            return (asio, ArrayInputSources.Describe(backend, asio.Length));
        }
        if (backend.IsWasapi())
        {
            int count = session.SelectedCaptureEndpoint?.ChannelCount ?? 0;
            return (Enumerable.Range(0, count).ToArray(), ArrayInputSources.Describe(backend, count));
        }

        return ([0, 1], ArrayInputSources.Describe(backend, 2));
    }

    // Same verdict as the settings, so the panel is not a second opinion.
    public static bool MatchesDevice(RecordSettingsSession session) =>
        MeasurementSettingsFile.SweepMeasurementSettings.ArrayMatchesDevice(
            session.ArrayDeviceId,
            session.CaptureDeviceId);

    public static int MicrophoneChannel(RecordSettingsSession session) =>
        session.SelectedBackend == AudioBackend.Asio ? session.SelectedAsioInputOffset : session.SelectedWaveInputOffset;

    public static int? LoopbackChannel(RecordSettingsSession session) =>
        session.SelectedBackend == AudioBackend.Asio
            ? session.SelectedAsioLoopbackOffset
            : session.SelectedWaveLoopbackOffset;

    /// <summary>Array channels actually recordable on the selected device; none when the array was set up on another.</summary>
    /// <remarks>Must match <c>MeasurementSettingsFile.ResolveArrayChannels</c>, or a probed rate fails at the device.</remarks>
    public static IReadOnlyList<int> ReachableChannels(RecordSettingsSession session)
    {
        if (!MatchesDevice(session))
        {
            return [];
        }

        int microphoneChannel = MicrophoneChannel(session);
        int? loopbackChannel = LoopbackChannel(session);
        var reachable = InputChannels(session).Channels.ToHashSet();
        var channels = new List<int>();
        foreach (ArrayMicrophoneDefinition microphone in session.ArrayMicrophones)
        {
            if (microphone.ChannelOffset >= 0 &&
                microphone.ChannelOffset != microphoneChannel &&
                microphone.ChannelOffset != loopbackChannel &&
                (reachable.Count == 0 || reachable.Contains(microphone.ChannelOffset)) &&
                !channels.Contains(microphone.ChannelOffset))
            {
                channels.Add(microphone.ChannelOffset);
            }
        }

        return channels;
    }

    public static string ButtonText(RecordSettingsSession session)
    {
        int count = session.ArrayMicrophones.Count;
        bool matches = MatchesDevice(session);
        if (count > 0 && !matches)
        {
            // Not a count: none would be recorded; the device name is the whole message.
            return $"{count} on {DescribeDevice(session)}...";
        }

        int usable = ReachableChannels(session).Count;
        string suffix = usable == count ? string.Empty : $" ({count - usable} unusable)";
        return count == 0
            ? "None..."
            : count == 1
                ? $"1 microphone{suffix}..."
                : $"{count} microphones{suffix}...";
    }

    // An ASIO stamp is the driver name; a WASAPI stamp is an unreadable endpoint id, so look up its name.
    private static string DescribeDevice(RecordSettingsSession session)
    {
        string? id = session.ArrayDeviceId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "another device";
        }
        if (session.SelectedBackend == AudioBackend.Asio)
        {
            return id;
        }

        foreach (object item in session.RecordingDevice.Items)
        {
            if (item is AudioEndpointDescriptor endpoint &&
                string.Equals(endpoint.Id, id, StringComparison.Ordinal))
            {
                return endpoint.DisplayName;
            }
        }

        return "another device";
    }
}
