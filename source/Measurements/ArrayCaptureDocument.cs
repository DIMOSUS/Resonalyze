using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Turns a measurement's array of microphones into the spatial-average document
/// its consumers already understand.
/// </summary>
/// <remarks>
/// The Virtual DSP hybrid and the EQ wizard were built around a
/// <see cref="LiveCaptureDocument"/> because a moving microphone was the only way
/// to produce one. Nothing about them is specific to that ritual — a spatial
/// average is a spatial average — so an array is handed to them in the same
/// shape rather than through a second path they would each have to learn.
/// <para>
/// The stored curves are raw, so the calibration is applied HERE, each microphone
/// through its own: unlike the frequency-response view, which has a calibration
/// switch of its own, a consumer reading this document wants the driver's response
/// and not the microphones' colouring. The document says which calibration was
/// applied only when every microphone shared one; a mixed array can still be
/// averaged correctly, but there is no single curve a reader could undo.
/// </para>
/// </remarks>
internal static class ArrayCaptureDocument
{
    /// <summary>
    /// The spatial average of a measurement's array, or null when it has none — or
    /// when nothing in it could be placed on the anchor's level.
    /// </summary>
    public static LiveCaptureDocument? TryCreate(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        int sampleRateHz,
        ProtectiveHighPassConfiguration? protectiveHighPass) =>
        TryCreateWithSpread(microphones, sampleRateHz, protectiveHighPass).Document;

    /// <summary>
    /// The same average, with the spread between the positions beside it on the same
    /// grid — how far apart the microphones sat at each band. Null spread whenever
    /// the document is null.
    /// </summary>
    /// <remarks>
    /// Beside rather than inside: a spatial average document describes a curve, and a
    /// moving microphone — the other method that produces one — has no per-position
    /// spread to report. Only a caller that knows it is holding an array asks for it.
    /// </remarks>
    public static (LiveCaptureDocument? Document, double[]? SpreadDb) TryCreateWithSpread(
        IReadOnlyList<ArrayMicrophoneCurve> microphones,
        int sampleRateHz,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        if (microphones.Count == 0)
        {
            return (null, null);
        }

        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        var calibrated = new List<IReadOnlyList<double>>(microphones.Count);
        var raw = new List<IReadOnlyList<double>>(microphones.Count);
        int anchorIndex = -1;
        for (int i = 0; i < microphones.Count; i++)
        {
            if (microphones[i].LevelsDb.Length != grid.Count)
            {
                // A curve from a grid this build does not use cannot be placed
                // beside the others without shifting it in frequency.
                return (null, null);
            }

            calibrated.Add(Calibrate(microphones[i], grid));
            raw.Add(microphones[i].LevelsDb);
            if (microphones[i].IsMeasurementMicrophone && anchorIndex < 0)
            {
                anchorIndex = i;
            }
        }

        SpatialAverageResult placed = SpatialAverage.Average(
            calibrated,
            anchorIndex < 0 ? 0 : anchorIndex);
        if (placed.TrimsDb.All(trim => trim == null))
        {
            return (null, null);
        }

        return (new LiveCaptureDocument
        {
            Format = LiveCaptureDocument.CurrentFormat,
            Version = LiveCaptureDocument.CurrentVersion,
            SavedAtUtc = DateTimeOffset.UtcNow,
            // Named after what it is rather than after a file: the button shows
            // this, and "7 microphones" is the thing a user wants to read there.
            Title = microphones.Count == 1
                ? "Array of 1 microphone"
                : $"Array of {microphones.Count} microphones",
            Method = SpatialAverageMethod.MicArray,
            // Each measurement is its own session, and for an array that costs
            // nothing: what a session id guards — that levels were held together by
            // one analyzer run — is guaranteed here by the loopback instead.
            CaptureSessionId = Guid.NewGuid(),
            Recipe = BuildRecipe(sampleRateHz, microphones.Count, protectiveHighPass),
            Calibration = SharedCalibration(microphones),
            CurveDb = placed.AverageDb,
            CalibrationCorrectionDb = CorrectionDb(placed, raw),
            GridStartHz = grid[0],
            GridStopHz = grid[^1]
        },
        placed.SpreadDb);
    }

    /// <remarks>
    /// The analyzer fields are left at their defaults on purpose: an array has no
    /// analyzer. Nothing reads them for this method — the set rules for an array
    /// compare the protective high-pass and nothing else — and inventing plausible
    /// values would only make the document look like a capture it is not.
    /// </remarks>
    private static LiveCaptureRecipe BuildRecipe(
        int sampleRateHz,
        int microphoneCount,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        ProtectiveHighPassConfiguration filter =
            ProtectiveHighPassConfiguration.Normalize(protectiveHighPass);
        return new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.TransferFunction,
            SampleRateHz = sampleRateHz,
            MicrophoneCount = microphoneCount,
            // A swept transfer magnitude, relative to the loopback: it is not an
            // absolute sound pressure level and must not claim to be one.
            MagnitudeScale = MagnitudeScale.Relative,
            SlopeCompensation = false,
            // Stored unsmoothed, which is what lets a consumer re-smooth it.
            SmoothingCode = 0,
            ProtectiveHighPassKind = filter.Kind,
            ProtectiveHighPassFrequencyHz = filter.FrequencyHz,
            ProtectiveHighPassSlopeDbPerOctave = filter.SlopeDbPerOctave
        };
    }

    /// <summary>
    /// The microphone correction baked into the average, per band, in the sign
    /// convention <see cref="LiveCaptureDocument.CalibrationCorrectionDb"/> uses —
    /// the pipeline SUBTRACTS it, so undoing it means adding it back.
    /// </summary>
    /// <remarks>
    /// Measured rather than copied from a calibration file, and that distinction is
    /// the whole point. Each microphone is corrected by its OWN curve before the
    /// averaging, so when they carry different files there is no single correction to
    /// name — and a document that reports none is read as uncalibrated, which makes
    /// every consumer apply the panel's calibration on top of one already there. The
    /// difference between the calibrated average and the raw one IS that correction,
    /// exactly, for a matched array and a mixed one alike.
    /// <para>
    /// On the same trims, deliberately: the placement is a property of the set, and
    /// re-deriving it from the raw curves would let the two averages differ by more
    /// than the calibration they are supposed to differ by.
    /// </para>
    /// </remarks>
    private static double[] CorrectionDb(
        SpatialAverageResult placed,
        IReadOnlyList<IReadOnlyList<double>> raw)
    {
        var rawPlaced = new List<double[]>(raw.Count);
        for (int microphone = 0; microphone < raw.Count; microphone++)
        {
            if (placed.TrimsDb[microphone] is not { } trim)
            {
                continue;
            }

            IReadOnlyList<double> curve = raw[microphone];
            var shifted = new double[curve.Count];
            for (int band = 0; band < shifted.Length; band++)
            {
                shifted[band] = double.IsFinite(curve[band])
                    ? curve[band] + trim
                    : double.NaN;
            }

            rawPlaced.Add(shifted);
        }

        double[] rawAverage = SpatialAverage.RmsAverageDb(rawPlaced);
        var correction = new double[placed.AverageDb.Length];
        for (int band = 0; band < correction.Length; band++)
        {
            double calibratedLevel = placed.AverageDb[band];
            double rawLevel = rawAverage[band];
            // Zero rather than NaN where nothing was measured: a correction of zero
            // is the honest "nothing was subtracted here", and a NaN would spread out
            // of the gap into whatever undoes it.
            correction[band] = double.IsFinite(calibratedLevel) && double.IsFinite(rawLevel)
                ? rawLevel - calibratedLevel
                : 0.0;
        }

        return correction;
    }

    private static double[] Calibrate(
        ArrayMicrophoneCurve microphone,
        IReadOnlyList<double> grid)
    {
        double[] levels = microphone.LevelsDb.ToArray();
        if (microphone.Calibration is not { } settings)
        {
            return levels;
        }

        CalibrationFile calibration = settings.ToCalibrationFile();
        for (int band = 0; band < levels.Length; band++)
        {
            if (double.IsFinite(levels[band]))
            {
                // The pipeline SUBTRACTS a microphone correction from a level.
                levels[band] -= calibration.GetDecibelCorrection(grid[band]);
            }
        }

        return levels;
    }

    // The calibration the document can name: the one every microphone shared, or
    // none. A mixed array is averaged correctly all the same — each microphone was
    // corrected by its own — but no single curve describes what was applied, and
    // claiming one would let a reader "undo" a correction that was never uniform.
    private static VirtualCrossoverCalibrationSettings? SharedCalibration(
        IReadOnlyList<ArrayMicrophoneCurve> microphones)
    {
        VirtualCrossoverCalibrationSettings? first = microphones[0].Calibration;
        if (first == null)
        {
            return null;
        }

        CalibrationFile firstCurve = first.ToCalibrationFile();
        foreach (ArrayMicrophoneCurve microphone in microphones)
        {
            if (microphone.Calibration is not { } settings ||
                !CalibrationFile.SameCurve(settings.ToCalibrationFile(), firstCurve))
            {
                return null;
            }
        }

        return first;
    }
}
