namespace Resonalyze.Dsp;

/// <summary>Third-party EQ profile layout. Import never throws on malformed input; a format may support one direction only.</summary>
public interface IEqProfileFormat
{
    string Name { get; }

    /// <summary>Without the dot (e.g. "txt").</summary>
    string Extension { get; }

    bool CanImport { get; }
    bool CanExport { get; }

    /// <summary>False: shelves must be dropped from an export (the caller reports it), not written as a differently realized filter.</summary>
    bool SupportsShelvingFilters => true;

    /// <summary>Per order, since support splits there (Equalizer APO's AP is second-order only). False: drop from export and report.</summary>
    bool SupportsAllPass(PeqBandType type) => true;

    /// <summary>False: the target keeps the gain elsewhere (a car DSP channel gain); export omits it, import reads 0, caller reports it.</summary>
    bool CarriesPreamp => true;

    string Export(EqualizationCurve curve);

    /// <summary>Returns whether the text was recognised; an empty curve alone is not a failure (a preamp-only profile is valid).</summary>
    bool TryImport(string text, out EqualizationCurve curve);

    EqualizationCurve Import(string text) =>
        TryImport(text, out EqualizationCurve curve)
            ? curve
            : new EqualizationCurve(Array.Empty<PeqBand>());
}
