using System.Diagnostics;
using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record EqWizardSlotOption(int Slot, string Title, string Description);

/// <summary>Turns a user's pick into an <see cref="EqWizardCurveSource"/>; UI-free so eligibility and point hygiene are testable.</summary>
internal sealed class EqWizardSourceResolver
{
    // Live Spectrum shares Frequency Response's slots, so RTA captures are found here too.
    private const Mode SlotMode = Mode.FrequencyResponse;

    private readonly string? overlayRootDirectory;

    public EqWizardSourceResolver(string? overlayRootDirectory = null)
    {
        this.overlayRootDirectory = overlayRootDirectory;
    }

    /// <summary>Eligible captured slots in order. Unloadable slots are skipped, not quarantined: the overlay UI owns them.</summary>
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

    /// <summary>Captured magnitude on the dB axis with a kind; untagged (legacy) slots are refused, unlike text files.</summary>
    internal static bool IsEligible(OverlayFile file)
    {
        // The dB axis appears as no key (Live Spectrum) or the named key (sweep modes); only another axis disqualifies.
        return file.Kind == OverlayKind.Captured &&
            file.CapturedYAxisKey is null or PlotModelFactory.DecibelAxisKey &&
            file.CapturedCurveKind is not null &&
            IsEqualizableResponse(role: null, file.CapturedCurveKind) &&
            file.Points.Length >= 2;
    }

    /// <summary>
    /// Shared slot/text rule: role is a response and kind is Primary or InputSpectrum, or unstated (foreign files).
    /// Harmonic/THD/phase/coherence traces and deviation/EQ/target/calculated roles are refused.
    /// </summary>
    internal static bool IsEqualizableResponse(
        OverlayCurveRole? role,
        AnalysisCurveKind? curveKind)
    {
        bool roleIsResponse = role is null or OverlayCurveRole.Response;
        bool kindIsFullRangeMagnitude = curveKind is null
            or AnalysisCurveKind.Primary
            or AnalysisCurveKind.InputSpectrum;
        return roleIsResponse && kindIsFullRangeMagnitude;
    }

    /// <summary>Null when the slot became unreadable or ineligible since the menu was built.</summary>
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
        // The slot's display offset is not applied (EQ needs the measured level); the correction is normalised with its points.
        (IReadOnlyList<SignalPoint> points, double[] pointsCorrection) = NormalizePoints(
            file.Points.Select(point => new SignalPoint(point.X, point.Y)),
            file.PointsCalibrationCorrectionDb);
        IReadOnlyList<SignalPoint>? raw = file.RawSpectrum.Length >= 2
            ? file.RawSpectrum.Select(point => new SignalPoint(point.X, point.Y)).ToArray()
            : null;

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.OverlaySlot,
            DisplayName = file.Title,
            Description = DescribeSlot(file),
            RawSpectrum = raw,
            RawSpectrumBand = new MeasuredBand(
                file.MeasuredLowFrequencyHz, file.MeasuredHighFrequencyHz),
            OwnCalibrationCorrectionDb = file.RawCalibrationCorrectionDb.ToArray(),
            Points = points,
            PointsCalibrationCorrectionDb = pointsCorrection,
            CapturedSmoothingCode = file.CapturedSmoothingCode,
            Scale = file.CapturedMagnitudeScale,
            SampleRateHz = file.SampleRateHz,
            CurveKind = file.CapturedCurveKind
        };
    }

    /// <summary>Throws <see cref="InvalidDataException"/> for anything but a plain response (see <see cref="IsEqualizableResponse"/>).</summary>
    public static EqWizardCurveSource CreateFromTextCurve(
        OverlayTextCurve curve,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (!IsEqualizableResponse(curve.Metadata.Role, curve.Metadata.CurveKind))
        {
            throw new InvalidDataException(DescribeRejection(curve.Metadata));
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
        // No stated unit: relative dB is safe, since a constant unit error only shifts the fitted shape's target.
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

    /// <summary>Loopback-transfer files equalize the transfer IR and carry γ²; others use the sweep deconvolution.</summary>
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

        // Transfer IR is zeroed outside the measured band; sweep deconvolution edges are signal and stay.
        MeasuredBand band = MeasuredBand.Resolve(
            file.ProtectiveHighPass?.ToConfiguration(),
            file.MeasuredLowFrequencyHz > 0
                ? file.MeasuredLowFrequencyHz
                : file.AchievedLowFrequencyHz,
            file.MeasuredHighFrequencyHz > file.MeasuredLowFrequencyHz
                ? file.MeasuredHighFrequencyHz
                : file.AchievedHighFrequencyHz,
            file.SampleRate);
        IImpulseMeasurement measurement = useTransfer
            ? new ImpulseMeasurementView(
                transfer!, file.TransferPeakIndex!.Value, file.SampleRate)
            {
                LowestMeasuredFrequencyHz = band.LowEdgeHz,
                HighestMeasuredFrequencyHz = band.HighEdgeHz
            }
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

    /// <summary>A stored spatial average on the imported-curve path. See docs/tech/eq-auto-tuner.md#spatial-average-import.</summary>
    public static EqWizardCurveSource CreateFromSpatialAverage(
        LiveCaptureDocument document,
        string description)
    {
        ArgumentNullException.ThrowIfNull(document);

        (IReadOnlyList<SignalPoint> points, double[] pointsCorrection) = NormalizePoints(
            document.ToCurvePoints(), document.CalibrationCorrectionDb);
        if (points.Count < 2)
        {
            throw new InvalidDataException(
                "The capture contains fewer than two usable frequency points.");
        }

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.SpatialAverage,
            DisplayName = string.IsNullOrWhiteSpace(document.Title)
                ? "Spatial average"
                : document.Title,
            Description = description,
            Points = points,
            PointsCalibrationCorrectionDb = pointsCorrection,
            CalibrationIsAggregate = document.CalibrationIsAggregate,
            CapturedSmoothingCode = document.Recipe.SmoothingCode,
            Scale = document.Recipe.MagnitudeScale,
            SampleRateHz = document.Recipe.SampleRateHz > 0
                ? document.Recipe.SampleRateHz
                : null,
            CurveKind = AnalysisCurveKind.InputSpectrum
        };
    }

    /// <summary>Max unsmoothed position spread before a boost is refused. See docs/tech/eq-auto-tuner.md#microphone-array-agreement.</summary>
    private const double ArraySpreadBoostLimitDb = 20.0;

    /// <summary>A measurement's own mic-array average in place of the one-position response (a snapshot).</summary>
    public static EqWizardCurveSource? TryCreateFromArray(
        ImpulseResponseFile file,
        string displayName,
        string description)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.ArrayMicrophones is not { } entry)
        {
            return null;
        }

        (LiveCaptureDocument? document, double[]? spreadDb) =
            ArrayCaptureDocument.TryCreateWithSpread(
                entry.ToCurves(),
                file.SampleRate,
                file.ProtectiveHighPass?.ToConfiguration(),
                file.MeasuredAtUtc > DateTimeOffset.UnixEpoch
                    ? file.MeasuredAtUtc
                    : file.SavedAtUtc);
        if (document == null)
        {
            return null;
        }

        return CreateFromSpatialAverage(document, description) with
        {
            // The measurement's name: the button must say WHICH measurement, not how it was taken.
            DisplayName = displayName,
            Coherence = BuildAgreementCurve(document, spreadDb)
        };
    }

    /// <summary>Two-valued boost confidence from array agreement. See docs/tech/eq-auto-tuner.md#microphone-array-agreement.</summary>
    public static IReadOnlyList<SignalPoint>? BuildAgreementCurve(
        LiveCaptureDocument document,
        double[]? spreadDb)
    {
        if (spreadDb == null)
        {
            return null;
        }

        IReadOnlyList<SignalPoint> curve = document.ToCurvePoints();
        if (curve.Count != spreadDb.Length)
        {
            return null;
        }

        var agreement = new List<SignalPoint>(spreadDb.Length);
        for (int band = 0; band < spreadDb.Length; band++)
        {
            agreement.Add(new SignalPoint(
                curve[band].X,
                !double.IsFinite(spreadDb[band])
                    ? double.IsFinite(curve[band].Y) ? 0.0 : double.NaN
                    : spreadDb[band] > ArraySpreadBoostLimitDb ? 0.0 : 1.0));
        }

        return agreement;
    }

    public static string DescribeArray(ImpulseResponseFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        int count = file.ArrayMicrophones?.Microphones.Count ?? 0;
        string microphones = count == 1 ? "1 microphone" : $"{count} microphones";
        return
            $"{path}\r\nMicrophone array spatial average\r\n" +
            $"{microphones} averaged over the listening volume\r\n" +
            $"{file.SampleRate} Hz";
    }

    public static string DescribeSpatialAverage(LiveCaptureDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        LiveCaptureRecipe recipe = document.Recipe;
        string method = document.Method == SpatialAverageMethod.MovingMic
            ? "Moving microphone"
            : document.Method.ToString();
        return
            $"{path}\r\n{method} spatial average" +
            (recipe.IntegratedSeconds > 0
                ? $", {recipe.IntegratedSeconds:0} s integrated"
                : string.Empty) +
            $"\r\n{DescribeCurve(recipe.MagnitudeScale, recipe.SampleRateHz)}";
    }

    /// <summary>Raw half-spectrum γ² bins to an ascending (Hz, γ²) curve without DC; null when absent.</summary>
    internal static IReadOnlyList<SignalPoint>? ExtractTransferCoherence(
        ImpulseResponseFile file) =>
        ExtractTransferCoherence(file.TransferCoherence, file.SampleRate);

    internal static IReadOnlyList<SignalPoint>? ExtractTransferCoherence(
        double[]? transferCoherence, int sampleRate)
    {
        if (transferCoherence is not { Length: > 1 } coherence || sampleRate <= 0)
        {
            return null;
        }

        int fftLength = (coherence.Length - 1) * 2;
        var points = new List<SignalPoint>(coherence.Length - 1);
        for (int k = 1; k < coherence.Length; k++)
        {
            double frequency = (double)k * sampleRate / fftLength;
            double gammaSquared = coherence[k];
            if (double.IsFinite(frequency) && frequency > 0 && double.IsFinite(gammaSquared))
            {
                points.Add(new SignalPoint(frequency, gammaSquared));
            }
        }

        return points.Count >= 2 ? points : null;
    }

    /// <summary>Ascending, unique, finite frequencies; NaN LEVELS stay (unmeasured bands the fitter must not bridge).</summary>
    internal static IReadOnlyList<SignalPoint> NormalizePoints(
        IEnumerable<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return NormalizePoints(points, companion: null).Points;
    }

    /// <summary>Normalises points with a per-point companion so they cannot shift apart; a length mismatch drops the companion.</summary>
    internal static (IReadOnlyList<SignalPoint> Points, double[] Companion) NormalizePoints(
        IEnumerable<SignalPoint> points,
        IReadOnlyList<double>? companion)
    {
        ArgumentNullException.ThrowIfNull(points);

        List<SignalPoint> source = points as List<SignalPoint> ?? points.ToList();
        bool hasCompanion = companion is { Count: > 0 } && companion.Count == source.Count;

        var result = new List<SignalPoint>(source.Count);
        var companionResult = new List<double>(hasCompanion ? source.Count : 0);
        foreach ((SignalPoint point, int index) in source
            .Select((point, index) => (point, index))
            .Where(entry => double.IsFinite(entry.point.X) && entry.point.X > 0 &&
                !double.IsInfinity(entry.point.Y))
            .OrderBy(entry => entry.point.X))
        {
            if (result.Count > 0 && result[^1].X == point.X)
            {
                continue;
            }

            result.Add(point);
            if (hasCompanion)
            {
                companionResult.Add(companion![index]);
            }
        }

        return (result, companionResult.ToArray());
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

    private static string DescribeRejection(OverlayTextMetadata metadata)
    {
        if (metadata.Role is OverlayCurveRole.Deviation or OverlayCurveRole.EqCorrection)
        {
            string role = metadata.Role == OverlayCurveRole.EqCorrection
                ? "EQ correction"
                : "deviation";
            return $"This file holds a {role} curve, which is a difference from a " +
                "target rather than a measured response. Load the response it was " +
                "derived from instead.";
        }

        return $"This file holds a {DescribeKind(metadata.CurveKind)} curve, which is " +
            "not a full-range magnitude response and cannot be equalized. Load a " +
            "measured response (a swept frequency response or an RTA capture) instead.";
    }

    private static string DescribeKind(AnalysisCurveKind? kind) => kind switch
    {
        AnalysisCurveKind.SecondHarmonic or AnalysisCurveKind.ThirdHarmonic
            or AnalysisCurveKind.FourthHarmonic => "harmonic-distortion",
        AnalysisCurveKind.ThdPlusNoise => "THD+N",
        AnalysisCurveKind.MinimumPhase or AnalysisCurveKind.ExcessPhase => "phase",
        { } value => value.ToString(),
        null => "non-response"
    };
}
