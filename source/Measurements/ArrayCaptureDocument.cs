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
/// The stored curves are raw, so the calibration is applied by
/// <see cref="ArrayPlacement"/>, each microphone through its own: unlike the
/// frequency-response view, which has a calibration switch of its own, a consumer
/// reading this document wants the driver's response and not the microphones'
/// colouring. The document says which calibration was applied only when every
/// microphone shared one; a mixed array can still be averaged correctly, but there
/// is no single curve a reader could undo — which is what
/// <see cref="LiveCaptureDocument.CalibrationIsAggregate"/> exists to say.
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
        IReadOnlyList<double> grid = SpatialAverage.BuildGrid();
        if (ArrayPlacement.Resolve(microphones, grid) is not { } placed)
        {
            return (null, null);
        }

        // Two placed positions at the least, or this is not a spatial average and
        // must not present itself as one. One is what is left when every further
        // microphone failed to record or sat too far off the anchor's band to be
        // placed — and one microphone is the point measurement the consumers already
        // have, wearing a title that says a listening volume was covered. Its spread
        // is NaN everywhere, so nothing downstream could tell the difference either.
        // Refusing here sends the consumers to the impulse response, which they fall
        // back to out loud.
        if (placed.PlacedCount < 2)
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
            Calibration = ArrayPlacement.SharedCalibration(microphones),
            CurveDb = placed.CalibratedAverageDb,
            CalibrationCorrectionDb = placed.CorrectionDb(),
            CalibrationIsAggregate = ArrayPlacement.IsMixed(microphones),
            GridStartHz = grid[0],
            GridStopHz = grid[^1]
        },
        placed.CalibratedSpreadDb);
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
}
