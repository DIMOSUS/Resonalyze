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
    /// capture cannot be told apart from either — none of them are offered. The slot path
    /// is stricter than the text path: a captured slot always carries a kind, so an
    /// untagged (null) one is legacy and rejected, whereas a foreign text file legitimately
    /// declares nothing.
    /// </summary>
    internal static bool IsEligible(OverlayFile file)
    {
        // The plot's own dB axis shows up in a capture in two spellings: no key at all
        // (a Live Spectrum series carries none) or the named dB axis key — sweep modes
        // attach every curve to their dB axis BY KEY. Both are the level axis; only a
        // genuinely different axis (coherence) disqualifies.
        return file.Kind == OverlayKind.Captured &&
            file.CapturedYAxisKey is null or PlotModelFactory.DecibelAxisKey &&
            file.CapturedCurveKind is not null &&
            IsEqualizableResponse(role: null, file.CapturedCurveKind) &&
            file.Points.Length >= 2;
    }

    /// <summary>
    /// The single rule for "is this curve a plain measured response the wizard may
    /// equalize", shared by the overlay-slot and text-import paths so neither can accept
    /// something the other rejects. A curve qualifies only when its declared role is a
    /// response (or unstated) AND its declared kind is a full-range magnitude — the swept
    /// <see cref="AnalysisCurveKind.Primary"/> or the RTA
    /// <see cref="AnalysisCurveKind.InputSpectrum"/> — or unstated. A harmonic, THD, phase
    /// or coherence trace, and any non-response role (a deviation, an EQ correction, a
    /// target or a calculated curve), are all refused: equalizing them would correct the
    /// wrong thing. Unstated (null) is permitted because a file written by another tool
    /// declares nothing; the slot path adds its own non-null requirement on top.
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
        // The points-aligned correction rides along through normalization so it keeps
        // pointing at the frequencies it was frozen on.
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

    /// <summary>
    /// Imports a text curve. Throws <see cref="InvalidDataException"/> when the file
    /// declares itself as anything other than a plain response (see
    /// <see cref="IsEqualizableResponse"/>): a deviation or EQ correction is a difference
    /// against a target, and a harmonic, THD or phase curve is not a level — equalizing
    /// any of them would correct the wrong thing. This closes the text path as an
    /// end-run around the same rule the overlay-slot menu enforces.
    /// </summary>
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

        // The transfer response is zeroed outside what was measured — past the
        // protective high-pass, and outside the sweep's own band — while the sweep
        // deconvolution carries the filter and its own normalization, so its edges
        // are signal and stay.
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

    /// <summary>
    /// Wraps a stored spatial average as a source: a finished band-level response, on
    /// the imported-curve path rather than the impulse-response one.
    /// </summary>
    /// <remarks>
    /// The mapping is deliberately complete rather than convenient, because everything
    /// the wizard is allowed to do to an imported curve is decided from these fields.
    /// The calibration correction travels frozen on the drawn points, which is what
    /// lets the selector switch calibration exactly — those corrections are additive
    /// per frequency, so removing one and applying another loses nothing. The captured
    /// smoothing code comes from the recipe (a capture is taken unsmoothed, and the
    /// mode pins it there), which is what permits re-smoothing here; the curve kind
    /// says it is an analyzer input spectrum, so the re-smoothing that happens is the
    /// analyzer's own second pass over its band levels rather than a near-enough
    /// substitute.
    /// <para>
    /// NaN levels are carried through untouched. Below a protective high-pass the
    /// capture has nothing to say, and that has to reach the fitter as "do not
    /// equalize here" rather than as a level.
    /// </para>
    /// <para>
    /// No coherence: a spatial average is measured with one microphone and no
    /// reference, so there is none to carry, and Auto Tune gates its boosts on what
    /// remains rather than on a fabricated one.
    /// </para>
    /// </remarks>
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
            // What the analyzer produced: a reference-free input spectrum, the same
            // kind an RTA capture carries, which is what makes its stored band levels
            // re-smoothable here.
            CurveKind = AnalysisCurveKind.InputSpectrum
        };
    }

    /// <summary>
    /// How far the microphones of an array may disagree at a frequency before a boost
    /// there is refused.
    /// </summary>
    /// <remarks>
    /// Read off the owner's seven-position sets rather than chosen: after any
    /// smoothing the spread between positions in a car sits at a median of 11 to 12 dB
    /// across the whole band on BOTH a midrange and a tweeter, with a p90 near 15 dB.
    /// Disagreement of that size is the normal state of a listening volume, not a
    /// warning — a gate at 10 or 12 dB would refuse to boost across half of a
    /// midrange and nine tenths of a tweeter, which is a boost ban wearing a
    /// threshold's clothes. 20 dB selects the top few per cent instead: the bands
    /// where the average is carried by whichever position happened to be loudest,
    /// and filling the dip the others measured helps one seat centimetre while
    /// spending the headroom of every other.
    /// <para>
    /// Deliberately on the UNSMOOTHED spread. A single band where the positions part
    /// by 30 dB is exactly the pathology, and smoothing fills its neighbours in until
    /// nothing on the owner's sets exceeds 20 dB at all.
    /// </para>
    /// </remarks>
    private const double ArraySpreadBoostLimitDb = 20.0;

    /// <summary>
    /// A measurement's own microphone array as a source: the spatial average it was
    /// recorded with, in place of the response measured at the one position the
    /// impulse response came from.
    /// </summary>
    /// <remarks>
    /// The point of the whole array feature reaching the one tool that can do harm
    /// with the difference. A point measurement carries dips belonging to its own few
    /// centimetres, and an equalizer fitted to those is fitted to a place nobody's
    /// head occupies.
    /// <para>
    /// A SNAPSHOT like every other import: the curve is built here and nothing points
    /// back at the file, so the source cannot change under a tune in progress.
    /// </para>
    /// </remarks>
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
                file.SavedAtUtc);
        if (document == null)
        {
            return null;
        }

        return CreateFromSpatialAverage(document, description) with
        {
            // The measurement's name, not the document's: the button has to say WHICH
            // measurement is being equalized, and "Array of 7 microphones" says only
            // how it was taken.
            DisplayName = displayName,
            Coherence = BuildAgreementCurve(document, spreadDb)
        };
    }

    /// <summary>
    /// The array's position agreement as the confidence Auto Tune gates boosts on: 1
    /// where the microphones agree closely enough to boost, 0 where they do not.
    /// </summary>
    /// <remarks>
    /// A flat allow/refuse rather than a curve mapped from the spread. Any smooth map
    /// from decibels of disagreement to a confidence between 0 and 1 would be invented
    /// precision — the mask only ever compares it against one floor — and a
    /// two-valued curve says exactly what is known.
    /// <para>
    /// A band where fewer than two microphones had anything to say reads 0 — refuse —
    /// whenever the average itself is a level. There is no disagreement to measure
    /// there, and that is exactly the point: the confidence this curve carries is a
    /// second opinion, and a band with only one has none. Handing back the spread's
    /// NaN would be worse than saying nothing, because the mask reads a non-finite
    /// entry as PERMISSION — so the one case where the array cannot vouch for a dip
    /// would be the case where the gate switched itself off. Where the average is NaN
    /// too, the fit has already excluded the band and this says nothing about it.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// What the source button's tooltip says about a measurement's array.
    /// </summary>
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

    /// <summary>
    /// What the source button's tooltip says about a capture: how it was taken, how
    /// long it integrated, and the recipe facts that decide whether two captures belong
    /// to one set.
    /// </summary>
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

    /// <summary>
    /// Converts the raw half-spectrum coherence bins stored with a loopback-transfer
    /// measurement into an ascending (Hz, γ²) curve, dropping the DC bin (undefined on
    /// a log axis). Returns null when the file carries no coherence.
    /// </summary>
    internal static IReadOnlyList<SignalPoint>? ExtractTransferCoherence(
        ImpulseResponseFile file) =>
        ExtractTransferCoherence(file.TransferCoherence, file.SampleRate);

    /// <summary>
    /// The same conversion from the raw bins themselves, for a caller holding them
    /// without the file — a Virtual DSP channel side keeps its measurement's coherence
    /// in its runtime state.
    /// </summary>
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
        return NormalizePoints(points, companion: null).Points;
    }

    /// <summary>
    /// Normalizes the points together with a per-point companion array (the calibration
    /// correction frozen on those very points), so reordering or dropping a point carries
    /// its companion value along. Normalizing the two separately would let them shift
    /// against each other and silently apply a correction at the wrong frequency. A
    /// companion of a different length is not aligned to these points at all and is
    /// discarded rather than guessed at.
    /// </summary>
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

    // Explains why a text curve was refused, naming whichever of its declared role or
    // kind disqualified it, so the user knows to load the response it was derived from
    // (or that this file is the wrong kind of curve) rather than seeing a bare error.
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
