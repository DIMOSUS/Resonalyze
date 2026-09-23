using Resonalyze.Dsp;

namespace Resonalyze.Options;

internal enum CalibrationFileState
{
    Missing,
    Unusable,
    Usable
}

/// <summary>Reads each calibration file once while a dialog is open: its state does not change meanwhile, and the list
/// is redrawn after every edit.</summary>
internal sealed class CalibrationFileProbe
{
    private readonly Func<string, CalibrationFileState> read;
    private readonly Dictionary<string, CalibrationFileState> states = new(StringComparer.OrdinalIgnoreCase);

    public CalibrationFileProbe(Func<string, CalibrationFileState>? read = null)
    {
        this.read = read ?? ReadFile;
    }

    public CalibrationFileState State(string path)
    {
        if (!states.TryGetValue(path, out CalibrationFileState state))
        {
            state = read(path);
            states[path] = state;
        }

        return state;
    }

    private static CalibrationFileState ReadFile(string path) =>
        !File.Exists(path)
            ? CalibrationFileState.Missing
            : new CalibrationFile(path).HasData
                ? CalibrationFileState.Usable
                : CalibrationFileState.Unusable;
}

internal sealed record MicrophoneCalibrationRow(string Id, string Name, string Kind, string Details, string Status);

/// <summary>The calibration list as the dialog shows it: what each entry is and whether it can be used.</summary>
internal static class MicrophoneCalibrationRows
{
    public static IReadOnlyList<MicrophoneCalibrationRow> Read(
        MicrophoneCalibrationsSession session,
        CalibrationFileProbe probe) =>
        session.Definitions
            .Select(definition => new MicrophoneCalibrationRow(
                definition.Id,
                definition.Name,
                definition.Kind == MicrophoneCalibrationKind.Angle ? "Angle" : "File",
                Describe(session, definition),
                Status(session, probe, definition)))
            .ToList();

    private static string Describe(MicrophoneCalibrationsSession session, MicrophoneCalibrationDefinition definition)
    {
        if (definition.Kind == MicrophoneCalibrationKind.File)
        {
            return definition.Path is { } path ? Path.GetFileName(path) : "no file";
        }

        string baseName = session.Find(definition.BaseId) is { } baseDefinition ? baseDefinition.Name : "0°";
        string model = definition.Reference == MicrophoneAngleReference.SonarworksXref20
            ? "Sonarworks XREF 20"
            : $"{definition.FrontDiameterMm:0.##} mm, {DescribeGrid(definition.Grid)}";
        return $"{definition.AngleDegrees:0.#}° from {baseName} · {model}";
    }

    private static string Status(
        MicrophoneCalibrationsSession session,
        CalibrationFileProbe probe,
        MicrophoneCalibrationDefinition definition)
    {
        if (definition.Kind == MicrophoneCalibrationKind.File)
        {
            if (string.IsNullOrWhiteSpace(definition.Path))
            {
                return "no file selected";
            }

            return probe.State(definition.Path) == CalibrationFileState.Usable ? "ready" : "unusable file";
        }

        string? basePath = definition.BaseId is { } baseId
            ? session.Find(baseId)?.Path
            : session.ZeroDegreePath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return "base calibration missing";
        }

        return probe.State(basePath) switch
        {
            CalibrationFileState.Missing => "base calibration missing",
            CalibrationFileState.Unusable => "unusable base file",
            _ => "estimated"
        };
    }

    private static string DescribeGrid(MicrophoneProtectionGrid grid) => grid switch
    {
        MicrophoneProtectionGrid.Fitted => "grid fitted",
        MicrophoneProtectionGrid.Removed => "grid removed",
        _ => "grid unknown"
    };
}
