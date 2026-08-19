using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Resolves EQ Wizard import/export targets and executes either text-profile or
/// tuning-sheet I/O. WinForms owns only the dialogs and result presentation.
/// </summary>
internal sealed class EqWizardImportExportCoordinator
{
    private readonly IReadOnlyList<EqWizardImportTarget> importTargets;
    private readonly IReadOnlyList<EqWizardExportTarget> exportTargets;
    private readonly Func<string, string> readAllText;
    private readonly Action<string, string> writeAllText;
    private readonly Action<EqWizardTuningSheetRequest> exportTuningSheet;

    public EqWizardImportExportCoordinator()
        : this(
            EqProfileFormats.Importable,
            EqProfileFormats.Exportable,
            File.ReadAllText,
            AtomicFile.WriteAllText,
            request => TuningSheetPdf.Export(
                request.Path,
                request.Title,
                request.Curve,
                request.MinHz,
                request.MaxHz,
                request.SampleRate,
                request.Stats,
                request.QConvention))
    {
    }

    internal EqWizardImportExportCoordinator(
        IReadOnlyList<IEqProfileFormat> importFormats,
        IReadOnlyList<IEqProfileFormat> exportFormats,
        Func<string, string> readAllText,
        Action<string, string> writeAllText,
        Action<EqWizardTuningSheetRequest> exportTuningSheet)
    {
        ArgumentNullException.ThrowIfNull(importFormats);
        ArgumentNullException.ThrowIfNull(exportFormats);
        this.readAllText = readAllText ?? throw new ArgumentNullException(nameof(readAllText));
        this.writeAllText = writeAllText ?? throw new ArgumentNullException(nameof(writeAllText));
        this.exportTuningSheet = exportTuningSheet ??
            throw new ArgumentNullException(nameof(exportTuningSheet));

        importTargets = importFormats
            .Select(format => new EqWizardImportTarget(format))
            .ToArray();
        exportTargets = exportFormats
            .Select(format => new EqWizardExportTarget(format))
            .Append(EqWizardExportTarget.TuningSheet())
            .ToArray();
        if (importTargets.Count == 0 || exportTargets.Count == 1)
        {
            throw new ArgumentException("At least one text EQ format is required.");
        }
    }

    public string ImportFilter => BuildFilter(importTargets);
    public string ExportFilter => BuildFilter(exportTargets);
    public string DefaultExportExtension => exportTargets[0].Extension;

    public EqWizardImportTarget ResolveImportTarget(int filterIndex) =>
        importTargets[ResolveIndex(filterIndex, importTargets.Count)];

    public EqWizardExportTarget ResolveExportTarget(int filterIndex) =>
        exportTargets[ResolveIndex(filterIndex, exportTargets.Count)];

    public EqWizardFileResult<EqualizationCurve> Import(EqWizardImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            string text = readAllText(request.Path);

            // The parsers are readers, not validators, and do not throw on
            // rubbish — so the format has to say whether it recognised the file.
            // Band count cannot stand in for that: a profile carrying only a
            // "Preamp:" line is valid and has none. Treating it as failure used
            // to be the destructive path, since the panel cleared bypass and
            // applied the empty curve over whatever the user had just tuned.
            if (!request.Target.Format.TryImport(text, out EqualizationCurve curve))
            {
                return EqWizardFileResult<EqualizationCurve>.Failed(
                    new InvalidDataException(
                        "No equalizer settings were found. Check that the file really is a " +
                        $"{request.Target.Name} profile."));
            }

            return EqWizardFileResult<EqualizationCurve>.Succeeded(curve);
        }
        catch (Exception exception)
        {
            return EqWizardFileResult<EqualizationCurve>.Failed(exception);
        }
    }

    public EqWizardFileResult Export(EqWizardExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            ValidateExportRequest(request);
            if (request.Target.IsTuningSheet)
            {
                exportTuningSheet(new EqWizardTuningSheetRequest(
                    request.Path,
                    request.Title,
                    request.Curve,
                    request.MinHz,
                    request.MaxHz,
                    request.SampleRate,
                    request.Stats,
                    request.QConvention));
            }
            else
            {
                IEqProfileFormat format = request.Target.Format!;
                IEqProfileFormat effectiveFormat = format is GraphicEqFormat
                    ? new GraphicEqFormat(request.SampleRate)
                    : format;
                // Dropping the shelves here as well as in the UI is deliberate: the
                // warning is the user's decision, this is the guarantee. A format
                // that cannot state our shelf must never receive one written as
                // something its reader would realize differently.
                EqualizationCurve curve = request.Target.SupportsShelvingFilters
                    ? request.Curve
                    : WithoutShelvingBands(request.Curve);
                writeAllText(request.Path, effectiveFormat.Export(curve));
            }
            return EqWizardFileResult.Succeeded();
        }
        catch (Exception exception)
        {
            return EqWizardFileResult.Failed(exception);
        }
    }

    /// <summary>
    /// How many shelving filters an export to <paramref name="target"/> would have
    /// to leave out. Zero when the format carries them, or when there are none —
    /// the panel asks only when the answer is going to cost the user something.
    /// </summary>
    internal static int CountShelvingBandsDroppedBy(
        EqWizardExportTarget target,
        EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(curve);

        return target.SupportsShelvingFilters
            ? 0
            : curve.Bands.Count(band => band.Type.IsShelving());
    }

    /// <summary>
    /// The preamp an export to <paramref name="target"/> would leave behind, in dB.
    /// Zero when the format carries it, and zero when there is none to lose — the
    /// panel asks only when the answer is going to cost the user something.
    /// </summary>
    /// <remarks>
    /// A format without a preamp slot (a car DSP's per-channel bank: the gain is a
    /// separate control on the device) writes the bands only. The whole curve is
    /// then quietly that many dB off the tune on screen, which is worse than a
    /// dropped band: nothing in the exported file hints at it, so the user has to
    /// be told which gain to enter by hand.
    /// </remarks>
    internal static double PreampDroppedBy(
        EqWizardExportTarget target,
        EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(curve);

        return target.CarriesPreamp || curve.PreampDb == 0
            ? 0
            : curve.PreampDb;
    }

    internal static EqualizationCurve WithoutShelvingBands(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        return new EqualizationCurve(
            curve.Bands.Where(band => !band.Type.IsShelving()),
            curve.PreampDb);
    }

    private static string BuildFilter<TTarget>(IReadOnlyList<TTarget> targets)
        where TTarget : IEqWizardFileTarget =>
        string.Join(
            "|",
            targets.Select(target =>
                $"{target.Name} (*.{target.Extension})|*.{target.Extension}"));

    private static int ResolveIndex(int filterIndex, int targetCount) =>
        Math.Clamp(filterIndex - 1, 0, targetCount - 1);

    private static void ValidateExportRequest(EqWizardExportRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.Curve);
        if (!double.IsFinite(request.SampleRate) || request.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.SampleRate));
        }
        if (request.Target.IsTuningSheet &&
            (!double.IsFinite(request.MinHz) || !double.IsFinite(request.MaxHz) ||
             request.MinHz <= 0 || request.MaxHz <= request.MinHz))
        {
            throw new ArgumentException("The tuning-sheet fit range is invalid.");
        }
    }
}

internal interface IEqWizardFileTarget
{
    string Name { get; }
    string Extension { get; }
}

internal sealed class EqWizardImportTarget : IEqWizardFileTarget
{
    internal EqWizardImportTarget(IEqProfileFormat format)
    {
        Format = format;
    }

    internal IEqProfileFormat Format { get; }
    public string Name => Format.Name;
    public string Extension => Format.Extension;
}

internal sealed class EqWizardExportTarget : IEqWizardFileTarget
{
    private EqWizardExportTarget(IEqProfileFormat? format, string name, string extension)
    {
        Format = format;
        Name = name;
        Extension = extension;
    }

    internal EqWizardExportTarget(IEqProfileFormat format)
        : this(format, format.Name, format.Extension)
    {
    }

    internal IEqProfileFormat? Format { get; }
    public string Name { get; }
    public string Extension { get; }
    public bool IsTuningSheet => Format == null;

    // The tuning sheet prints shelves in a table of their own, so it carries them.
    internal bool SupportsShelvingFilters => Format?.SupportsShelvingFilters ?? true;

    // ... and prints the preamp with them, so it carries that too.
    internal bool CarriesPreamp => Format?.CarriesPreamp ?? true;

    internal static EqWizardExportTarget TuningSheet() =>
        new(null, "Tuning sheet (PDF)", "pdf");
}

internal sealed record EqWizardImportRequest(
    string Path,
    EqWizardImportTarget Target);

internal sealed record EqWizardExportRequest(
    string Path,
    EqWizardExportTarget Target,
    EqualizationCurve Curve,
    double SampleRate,
    string Title,
    double MinHz,
    double MaxHz,
    EqTuneStats? Stats,
    // Only the tuning sheet honours this: the profile formats are read back by
    // software that defines Q the RBJ way, so restating theirs would corrupt them.
    PeqQConvention QConvention = PeqQConvention.Rbj);

internal sealed record EqWizardTuningSheetRequest(
    string Path,
    string Title,
    EqualizationCurve Curve,
    double MinHz,
    double MaxHz,
    double SampleRate,
    EqTuneStats? Stats,
    PeqQConvention QConvention = PeqQConvention.Rbj);

internal sealed record EqWizardFileResult(bool Success, Exception? Exception)
{
    public static EqWizardFileResult Succeeded() => new(true, null);
    public static EqWizardFileResult Failed(Exception exception) => new(false, exception);
}

internal sealed record EqWizardFileResult<T>(bool Success, T? Value, Exception? Exception)
{
    public static EqWizardFileResult<T> Succeeded(T value) => new(true, value, null);
    public static EqWizardFileResult<T> Failed(Exception exception) => new(false, default, exception);
}
