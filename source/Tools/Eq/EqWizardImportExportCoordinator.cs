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
            File.WriteAllText,
            request => TuningSheetPdf.Export(
                request.Path,
                request.Title,
                request.Curve,
                request.MinHz,
                request.MaxHz,
                request.SampleRate,
                request.Stats))
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
            return EqWizardFileResult<EqualizationCurve>.Succeeded(
                request.Target.Format.Import(text));
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
                    request.Stats));
            }
            else
            {
                IEqProfileFormat format = request.Target.Format!;
                IEqProfileFormat effectiveFormat = format is GraphicEqFormat
                    ? new GraphicEqFormat(request.SampleRate)
                    : format;
                writeAllText(request.Path, effectiveFormat.Export(request.Curve));
            }
            return EqWizardFileResult.Succeeded();
        }
        catch (Exception exception)
        {
            return EqWizardFileResult.Failed(exception);
        }
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
    EqTuneStats? Stats);

internal sealed record EqWizardTuningSheetRequest(
    string Path,
    string Title,
    EqualizationCurve Curve,
    double MinHz,
    double MaxHz,
    double SampleRate,
    EqTuneStats? Stats);

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
