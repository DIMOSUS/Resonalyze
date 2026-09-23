namespace Resonalyze.Options;

/// <summary>The working copy of the additional calibrations the list dialog edits, handed back on OK. See
/// docs/tech/sweep-measurement.md#calibration-dialogs-code-map.</summary>
internal sealed class MicrophoneCalibrationsSession
{
    private readonly List<MicrophoneCalibrationDefinition> definitions;

    public MicrophoneCalibrationsSession(
        IReadOnlyList<MicrophoneCalibrationDefinition> definitions,
        string? zeroDegreePath)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = definitions
            .Select(definition => definition.Clone())
            .ToList();
        ZeroDegreePath = zeroDegreePath;
    }

    public IReadOnlyList<MicrophoneCalibrationDefinition> Definitions => definitions;

    public string? ZeroDegreePath { get; }

    public MicrophoneCalibrationDefinition? Find(string? id) =>
        id == null
            ? null
            : definitions.FirstOrDefault(definition =>
                string.Equals(definition.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Named after the file, a suggestion the dialog opens straight into rename; null when no file was chosen.</summary>
    public MicrophoneCalibrationDefinition? AddFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
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
        return definition;
    }

    /// <summary>Not in the list until <see cref="Add"/>: the estimate dialog may be cancelled.</summary>
    public MicrophoneCalibrationDefinition NewAngle()
    {
        var definition = new MicrophoneCalibrationDefinition
        {
            Id = MicrophoneCalibrationDefinition.CreateId(definitions),
            Kind = MicrophoneCalibrationKind.Angle,
            AngleDegrees = 90.0
        };
        definition.Name = MicrophoneCalibrationDefinition.FormatAngleName(definition.AngleDegrees);
        return definition;
    }

    public void Add(MicrophoneCalibrationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definitions.Add(definition);
    }

    /// <summary>False when nothing changed: no file chosen, or the same one.</summary>
    public bool SetPath(MicrophoneCalibrationDefinition definition, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == definition.Path)
        {
            return false;
        }

        definition.Path = path;
        definition.Normalize();
        return true;
    }

    /// <summary>False refuses the edit, so the list keeps its previous text rather than a nameless entry.</summary>
    public bool Rename(string? id, string? label)
    {
        string? name = label?.Trim();
        if (string.IsNullOrEmpty(name) || Find(id) is not { } definition)
        {
            return false;
        }

        definition.Name = name;
        return true;
    }

    /// <summary>Estimates built on the removed entry fall back to the 0° calibration.</summary>
    public void Remove(MicrophoneCalibrationDefinition definition)
    {
        foreach (MicrophoneCalibrationDefinition derived in definitions)
        {
            if (string.Equals(derived.BaseId, definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                derived.BaseId = null;
            }
        }

        definitions.Remove(definition);
    }

    /// <summary>Only file-backed entries, never the estimate itself, so estimates never derive from estimates.</summary>
    public IReadOnlyList<MicrophoneCalibrationDefinition> BaseCandidates(MicrophoneCalibrationDefinition definition) =>
        definitions
            .Where(candidate =>
                candidate.Kind == MicrophoneCalibrationKind.File &&
                !string.Equals(candidate.Id, definition.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
