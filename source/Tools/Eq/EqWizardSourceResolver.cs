using System.Diagnostics;
using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One overlay slot offered as an EQ Wizard source.</summary>
internal sealed record EqWizardSlotOption(int Slot, string Title, string Description);

/// <summary>
/// Turns the things a user can pick — an overlay slot, a text file, an impulse response —
/// into an <see cref="EqWizardCurveSource"/>. UI-free so the eligibility rules and the
/// point hygiene are testable; the panel only supplies the choice.
/// </summary>
internal sealed class EqWizardSourceResolver
{
    // Overlay slots live under the Frequency Response mode: Live Spectrum shares its
    // slots and storage, so an RTA captured there is found here too.
    private const Mode SlotMode = Mode.FrequencyResponse;

    private readonly string? overlayRootDirectory;

    /// <param name="overlayRootDirectory">
    /// Overlay storage root; null uses the application's. Tests point it at a temp folder.
    /// </param>
    public EqWizardSourceResolver(string? overlayRootDirectory = null)
    {
        this.overlayRootDirectory = overlayRootDirectory;
    }

    /// <summary>
    /// The captured overlay slots that hold an equalizable magnitude response, in slot
    /// order. A slot file that fails to load is skipped and left alone: quarantining a
    /// damaged slot belongs to the overlay UI that owns it, and the wizard is only a
    /// reader — moving the file here would surprise the user in a different screen.
    /// </summary>
    public IReadOnlyList<EqWizardSlotOption> ListEligibleSlots()
    {
        var options = new List<EqWizardSlotOption>();
        for (int slot = 1; slot <= OverlayFile.MaximumSlotCount; slot++)
        {
            OverlayFile? file;
            try
            {
                file = OverlayFile.Load(SlotMode, slot, overlayRootDirectory);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"EQ Wizard skipped unreadable overlay slot {slot}: {exception}");
                continue;
            }

            if (file == null || !IsEligible(file))
            {
                continue;
            }

            options.Add(new EqWizardSlotOption(
                slot,
                file.Title,
                DescribeSlot(file)));
        }

        return options;
    }

    /// <summary>
    /// Whether a slot holds something the wizard can equalize: a captured magnitude
    /// response on the plot's own dB axis, tagged with what it is. A calculated or target
    /// slot is a derived shape, a coherence trace is not a level, and an untagged legacy
    /// capture cannot be told apart from either — none of them are offered.
    /// </summary>
    internal static bool IsEligible(OverlayFile file)
    {
        return file.Kind == OverlayKind.Captured &&
            file.CapturedYAxisKey == null &&
            file.CapturedCurveKind is
                AnalysisCurveKind.Primary or AnalysisCurveKind.InputSpectrum &&
            file.Points.Length >= 2;
    }

    /// <summary>
    /// Imports a slot as a source snapshot. Returns null when the slot has become
    /// unreadable or ineligible since the menu was built.
    /// </summary>
    public EqWizardCurveSource? TryCreateFromOverlaySlot(int slot)
    {
        OverlayFile? file;
        try
        {
            file = OverlayFile.Load(SlotMode, slot, overlayRootDirectory);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"EQ Wizard could not read overlay slot {slot}: {exception}");
            return null;
        }

        return file != null && IsEligible(file) ? CreateFromOverlayFile(file) : null;
    }

    internal static EqWizardCurveSource CreateFromOverlayFile(OverlayFile file)
    {
        // The slot's own offset is a display device for pulling curves apart on the plot;
        // equalization needs the level as measured, so it is deliberately not applied.
        IReadOnlyList<SignalPoint> points = NormalizePoints(
            file.Points.Select(point => new SignalPoint(point.X, point.Y)));
        IReadOnlyList<SignalPoint>? raw = file.RawSpectrum.Length >= 2
            ? file.RawSpectrum.Select(point => new SignalPoint(point.X, point.Y)).ToArray()
            : null;

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.OverlaySlot,
            DisplayName = file.Title,
            Description = DescribeSlot(file),
            RawSpectrum = raw,
            OwnCalibrationCorrectionDb = file.RawCalibrationCorrectionDb.ToArray(),
            Points = points,
            Scale = file.CapturedMagnitudeScale,
            SampleRateHz = file.SampleRateHz,
            CurveKind = file.CapturedCurveKind
        };
    }

    /// <summary>
    /// Imports a text curve. Throws <see cref="InvalidDataException"/> when the file
    /// declares itself a deviation or an EQ correction: those are differences against a
    /// target, and equalizing one would correct the error of the error.
    /// </summary>
    public static EqWizardCurveSource CreateFromTextCurve(
        OverlayTextCurve curve,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Metadata.Role is OverlayCurveRole.Deviation or OverlayCurveRole.EqCorrection)
        {
            throw new InvalidDataException(
                $"This file holds a {DescribeRole(curve.Metadata.Role.Value)} curve, " +
                "which is a difference from a target rather than a measured response. " +
                "Load the response it was derived from instead.");
        }

        IReadOnlyList<SignalPoint> points = NormalizePoints(
            curve.Points.Select(point => new SignalPoint(point.X, point.Y)));
        if (points.Count < 2)
        {
            throw new InvalidDataException(
                "The file contains fewer than two usable frequency points.");
        }

        string name = string.IsNullOrWhiteSpace(curve.Metadata.Title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : curve.Metadata.Title!;
        // A foreign file states no unit; the plot's own relative dB is the safe reading,
        // and the wizard fits a shape, so a constant unit error only shifts the target.
        MagnitudeScale scale = curve.Metadata.Scale ?? MagnitudeScale.Relative;

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.TextCurve,
            DisplayName = name,
            Description = $"{filePath}\r\n{DescribeCurve(scale, curve.Metadata.SampleRateHz)}",
            Points = points,
            Scale = scale,
            SampleRateHz = curve.Metadata.SampleRateHz,
            CurveKind = curve.Metadata.CurveKind
        };
    }

    /// <summary>
    /// Wraps an impulse response as a source. Mirrors the history preview: a
    /// loopback-transfer file equalizes its transfer IR and carries the per-frequency
    /// coherence (γ²) that gates Auto Tune boosts; everything else uses the sweep
    /// deconvolution, which has none.
    /// </summary>
    public static EqWizardCurveSource CreateFromImpulseResponse(
        ImpulseResponseFile file,
        string displayName,
        string description)
    {
        ArgumentNullException.ThrowIfNull(file);

        Complex[]? transfer = file.GetTransferImpulseResponse();
        bool useTransfer =
            file.MeasurementMode == SweepMeasurementMode.LoopbackTransfer &&
            transfer is { Length: > 0 } &&
            file.TransferPeakIndex is not null;

        IImpulseMeasurement measurement = useTransfer
            ? new ImpulseMeasurementView(
                transfer!, file.TransferPeakIndex!.Value, file.SampleRate)
            : new ImpulseMeasurementView(
                file.GetSweepDeconvolutionImpulseResponse(),
                file.SweepDeconvolutionPeakIndex,
                file.SampleRate);

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.ImpulseResponse,
            DisplayName = displayName,
            Description = description,
            Measurement = measurement,
            Coherence = useTransfer ? ExtractTransferCoherence(file) : null,
            SampleRateHz = file.SampleRate > 0 ? file.SampleRate : null,
            CurveKind = AnalysisCurveKind.Primary
        };
    }

    /// <summary>
    /// Converts the raw half-spectrum coherence bins stored with a loopback-transfer
    /// measurement into an ascending (Hz, γ²) curve, dropping the DC bin (undefined on
    /// a log axis). Returns null when the file carries no coherence.
    /// </summary>
    internal static IReadOnlyList<SignalPoint>? ExtractTransferCoherence(
        ImpulseResponseFile file)
    {
        if (file.TransferCoherence is not { Length: > 1 } coherence || file.SampleRate <= 0)
        {
            return null;
        }

        int fftLength = (coherence.Length - 1) * 2;
        var points = new List<SignalPoint>(coherence.Length - 1);
        for (int k = 1; k < coherence.Length; k++)
        {
            double frequency = (double)k * file.SampleRate / fftLength;
            double gammaSquared = coherence[k];
            if (double.IsFinite(frequency) && frequency > 0 && double.IsFinite(gammaSquared))
            {
                points.Add(new SignalPoint(frequency, gammaSquared));
            }
        }

        return points.Count >= 2 ? points : null;
    }

    /// <summary>
    /// Puts imported points into the ascending, single-valued order every consumer
    /// assumes. A non-finite frequency has no place on the axis and duplicates would make
    /// resampling order-dependent, so both go; a NaN LEVEL stays, because that is how a
    /// curve records a band it could not measure (below the coherence threshold) and the
    /// fitter reads those gaps rather than bridging them.
    /// </summary>
    internal static IReadOnlyList<SignalPoint> NormalizePoints(
        IEnumerable<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<SignalPoint>();
        foreach (SignalPoint point in points
            .Where(point => double.IsFinite(point.X) && point.X > 0 &&
                !double.IsInfinity(point.Y))
            .OrderBy(point => point.X))
        {
            if (result.Count > 0 && result[^1].X == point.X)
            {
                continue;
            }

            result.Add(point);
        }

        return result;
    }

    private static string DescribeSlot(OverlayFile file) =>
        $"Overlay slot {file.Slot}: {file.Title}\r\n" +
        DescribeCurve(file.CapturedMagnitudeScale, file.SampleRateHz);

    private static string DescribeCurve(MagnitudeScale scale, int? sampleRateHz)
    {
        string unit = scale == MagnitudeScale.SoundPressureLevel ? "dB SPL" : "dB";
        string rate = sampleRateHz is { } value
            ? $"{value / 1000.0:0.###} kHz"
            : "sample rate not stated";
        return $"{unit}, {rate}";
    }

    private static string DescribeRole(OverlayCurveRole role) => role switch
    {
        OverlayCurveRole.EqCorrection => "EQ correction",
        OverlayCurveRole.Deviation => "deviation",
        _ => "response"
    };
}
