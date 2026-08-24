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

    /// <summary>
    /// Whether the format can carry an all-pass band of the given order as an
    /// all-pass. False means the layout has no phase-only filter of that order, so
    /// such bands must be dropped from an export rather than written as something
    /// the target would realize differently — the caller is expected to say so, as
    /// it does for a dropped shelf.
    /// </summary>
    /// <remarks>
    /// Asked per order because support genuinely splits there: Equalizer APO's
    /// <c>AP</c> is second-order only, while CamillaDSP and the Audiotec bank have
    /// both orders and the coefficient formats carry any band by construction.
    /// Defaults to true for both; only ever asked for the two all-pass members.
    /// </remarks>
    bool SupportsAllPass(PeqBandType type) => true;

    /// <summary>
    /// Whether the layout has a place for the whole-profile gain
    /// (<see cref="EqualizationCurve.PreampDb"/>). False means the target keeps that
    /// gain in a control the file never reaches — a car DSP's channel gain — so an
    /// export leaves the preamp out and an import reads it as 0; the caller is
    /// expected to say so, as it does for a dropped shelf.
    /// </summary>
    /// <remarks>
    /// Defaults to true: the formats that carry a preamp line, an output-gain field
    /// or a gain biquad realize it by construction.
    /// </remarks>
    bool CarriesPreamp => true;

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
