using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal sealed record ArrayMicrophoneRow(string Input, string Calibration, string Note);

/// <summary>The array list and its status line as the dialog shows them.</summary>
internal static class ArrayMicrophoneRows
{
    public static IReadOnlyList<ArrayMicrophoneRow> Read(ArrayMicrophonesSession session) =>
        session.Microphones
            .Select(microphone => new ArrayMicrophoneRow(
                DescribeInput(session, microphone.ChannelOffset),
                DescribeCalibration(session, microphone.CalibrationId),
                microphone.Note ?? string.Empty))
            .ToList();

    public static string InputLabel(int channelOffset) => $"Input {channelOffset + 1}";

    /// <summary>What a new microphone could take: the status is read as the Add button's answer.</summary>
    public static string Status(ArrayMicrophonesSession session)
    {
        int free = session.FreeChannels(excludingIndex: null).Count;
        int conflicting = session.Microphones.Count(session.Conflicts);
        string conflict = conflicting == 0
            ? string.Empty
            : conflicting == 1
                ? " 1 of them cannot be recorded — see the list."
                : $" {conflicting} of them cannot be recorded — see the list.";
        string hint = session.ChannelSourceHint;
        return session.AvailableChannels.Count == 0
            ? $"No inputs to record an array from ({hint})."
            : free > 0
                ? $"{session.Microphones.Count} configured, {free} further input(s) free ({hint}).{conflict}"
                : $"{session.Microphones.Count} configured; every input is in use ({hint}).{conflict}";
    }

    private static string DescribeInput(ArrayMicrophonesSession session, int channelOffset)
    {
        string input = InputLabel(channelOffset);
        if (channelOffset == session.MicrophoneChannel)
        {
            return $"{input} (the measurement microphone)";
        }

        return channelOffset == session.LoopbackChannel ? $"{input} (the loopback)" : input;
    }

    private static string DescribeCalibration(ArrayMicrophonesSession session, string? calibrationId)
    {
        if (MicrophoneCalibrationIds.IsOff(calibrationId))
        {
            return "Off";
        }

        MicrophoneCalibrationEntry? entry = session.Calibrations
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                calibrationId,
                StringComparison.OrdinalIgnoreCase));
        // A removed calibration shows as missing, not "None": different fix.
        return entry?.Name ?? $"{calibrationId} (missing)";
    }
}
