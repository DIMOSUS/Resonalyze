namespace Resonalyze.Dsp;

/// <summary>
/// A named import/export format for a parametric EQ profile
/// (<see cref="EqualizationCurve"/>). Implementations map to and from a specific
/// third-party text layout. Import must be defensive (never throw on malformed
/// input); a format may support only one direction.
/// </summary>
public interface IEqProfileFormat
{
    /// <summary>Human-readable format name shown in the file dialog.</summary>
    string Name { get; }

    /// <summary>Default file extension without the dot (e.g. "txt", "csv").</summary>
    string Extension { get; }

    bool CanImport { get; }
    bool CanExport { get; }

    /// <summary>Serialises the curve. Only valid when <see cref="CanExport"/>.</summary>
    string Export(EqualizationCurve curve);

    /// <summary>Parses a curve defensively. Only valid when <see cref="CanImport"/>.</summary>
    EqualizationCurve Import(string text);
}
