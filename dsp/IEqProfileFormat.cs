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

    /// <summary>
    /// Whether the format can carry a shelving band (<see cref="PeqBandType"/>) as
    /// a shelf. False means the layout has no shelf of the same parameterisation,
    /// so shelves must be dropped from an export rather than written as something
    /// the target would realize differently — the caller is expected to say so.
    /// </summary>
    /// <remarks>
    /// Defaults to true: the formats that carry coefficients or a sampled curve
    /// realize any band shape by construction, and the parametric ones that name a
    /// shelf with a Q realize ours exactly.
    /// </remarks>
    bool SupportsShelvingFilters => true;

    /// <summary>Serialises the curve. Only valid when <see cref="CanExport"/>.</summary>
    string Export(EqualizationCurve curve);

    /// <summary>
    /// Parses defensively and reports whether the text was recognised as this
    /// format at all. Only valid when <see cref="CanImport"/>.
    /// </summary>
    /// <remarks>
    /// An empty curve is NOT a failure signal: a recognised profile may
    /// legitimately carry no bands — an Equalizer APO file holding only a
    /// <c>Preamp:</c> line is a valid neutral profile. Callers that must tell a
    /// real profile from an unrecognised file have to use the return value.
    /// </remarks>
    bool TryImport(string text, out EqualizationCurve curve);

    /// <summary>
    /// Parses a curve defensively, yielding an empty curve for input this format
    /// does not recognise. Only valid when <see cref="CanImport"/>.
    /// </summary>
    EqualizationCurve Import(string text) =>
        TryImport(text, out EqualizationCurve curve)
            ? curve
            : new EqualizationCurve(Array.Empty<PeqBand>());
}
