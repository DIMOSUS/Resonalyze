using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The live curves as points, from the analyzer's accumulated spectra: the RTA, the transfer function, the coherence
/// split, the raw RTA an overlay stores and the capture document Save writes.
/// </summary>
/// <remarks>
/// Every curve reads one <see cref="LiveSpectrumDisplay"/>, so a build cannot mix two readings of the options or of
/// the analyzer. See docs/tech/live-spectrum.md.
/// </remarks>
internal sealed class LiveSpectrumCurves
{
    private const double LowHz = 20;
    private const double HighHz = 20000;
    private const int PointCount = 1024;

    // One full render of the analytic noise spectrum per call, too heavy per tick; memoized on its parameters.
    private double[]? tiltBandCompensation;
    private (NoiseSpectralModel Model, int BinCount, int FftLength, int SampleRate,
        double EnbwBins, double MainLobeBins, double SmoothingOctaves, bool Psycho)
        tiltBandKey;

    /// <summary>Raw samples plus smoothing code for the overlay layer; the band-power RTA has no raw form and returns
    /// the drawn-curve fallback.</summary>
    public RawCurveCapture? RawRta(LiveSpectrumDisplay display, IReadOnlyList<double>? inputMagnitude)
    {
        if (inputMagnitude is not { Count: > 1 })
        {
            return null;
        }

        LiveCaptureSetup setup = display.Setup;
        int smoothingCode = display.SmoothingCode;
        if (display.UsesBandPower)
        {
            // The band trace applies calibration additively per band, so a consumer can swap it exactly later.
            return PlotModelFactory.DescribeWithoutRawForm(
                smoothingCode,
                setup.SampleRate,
                setup.MicrophoneCalibration);
        }

        List<SignalPoint> spectrum = LiveRtaRawCapture.BuildRelativeRaw(
            inputMagnitude,
            setup.SequenceLength,
            setup.SampleRate,
            display.TiltModel);
        if (spectrum.Count < 2)
        {
            return PlotModelFactory.DescribeWithoutRawForm(
                smoothingCode,
                setup.SampleRate,
                setup.MicrophoneCalibration);
        }

        return new RawCurveCapture(
            spectrum,
            RawCurveRenderer.CaptureCalibrationCorrection(
                setup.MicrophoneCalibration),
            smoothingCode,
            setup.SampleRate > 0 ? setup.SampleRate : null);
    }

    /// <summary>Snapshot of the reference-free capture as a document; null unless on the band-power path.</summary>
    /// <remarks>Built here so the recipe describes what this pipeline drew, not what the options asked for.</remarks>
    /// <param name="frameCount">Taken from the same snapshot as the bins; the analyzer may have advanced since.</param>
    public LiveCaptureDocument? CaptureDocument(
        LiveSpectrumDisplay display,
        double[]? inputMagnitude,
        int frameCount,
        string title,
        int clippedFrameCount = 0)
    {
        LiveCaptureSetup setup = display.Setup;
        // From the accumulation, the same field the render divides out.
        ProtectiveHighPassConfiguration protectiveHighPass = setup.ProtectiveHighPass;
        int sampleRate = setup.SampleRate;
        int sequenceLength = setup.SequenceLength;
        if (inputMagnitude is not { Length: > 1 } ||
            sampleRate < 1 ||
            sequenceLength < 2 ||
            !display.UsesBandPower)
        {
            return null;
        }

        var applied = new LiveRtaApplied();
        List<SignalPoint> curve = Rta(display, inputMagnitude, applied);
        if (curve.Count != LiveCaptureDocument.CurvePointCount)
        {
            return null;
        }

        CalibrationFile? calibration = setup.MicrophoneCalibration;

        int hop = Math.Max(1, setup.HopSize);
        int frames = frameCount;
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Title = title ?? string.Empty,
            Method = SpatialAverageMethod.MovingMic,
            CaptureSessionId = setup.CaptureSessionId,
            SpectrumDb = LiveCaptureDocument.StoreSpectrumBins(
                inputMagnitude, sequenceLength, sampleRate),
            CurveDb = curve.Select(point => point.Y).ToArray(),
            GridStartHz = curve[0].X,
            GridStopHz = curve[^1].X,
            TiltCompensationDb = applied.TiltDb,
            CalibrationCorrectionDb = applied.CalibrationDb,
            ProtectiveHighPassCorrectionDb = applied.ProtectiveHighPassDb,
            // Name frozen beside the curve: ids past the 0 deg slot are GUIDs. The name is only a hint.
            Calibration = calibration != null
                ? VirtualCrossoverCalibrationSettings.From(
                    calibration,
                    setup.MicrophoneCalibrationName,
                    fileName: null)
                : null,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = display.Mode,
                SampleRateHz = sampleRate,
                SequenceLength = sequenceLength,
                FrameMilliseconds = 1000.0 * sequenceLength / sampleRate,
                WindowType = setup.WindowType,
                WindowEnbwBins = setup.WindowEnbwBins,
                WindowMainLobeBins = setup.WindowMainLobeBins,
                OverlapPercent = 100 - 100 * hop / sequenceLength,
                AveragingSpeed = display.Options.EffectiveAveragingSpeed,
                AveragedFrameCount = frames,
                ClippedFrameCount = clippedFrameCount,
                IntegratedSeconds = (double)frames * hop / sampleRate,
                NoiseColor = display.Options.EffectiveNoiseColor,
                // What the curve received: the render skips a misaligned compensation.
                SlopeCompensation = applied.TiltDb.Length > 0,
                // Null offset: relative levels, consistent across the set.
                MagnitudeScale = display.Scale,
                SplAnchorOffsetDb = display.SplOffsetDb,
                SmoothingCode = display.SmoothingCode,
                ProtectiveHighPassKind = protectiveHighPass.Kind,
                ProtectiveHighPassFrequencyHz = protectiveHighPass.FrequencyHz,
                ProtectiveHighPassSlopeDbPerOctave = protectiveHighPass.SlopeDbPerOctave
            }
        };
    }

    /// <summary>The live transfer function on the display grid, relative.</summary>
    public List<SignalPoint> Transfer(LiveSpectrumDisplay display, double[] magnitude) =>
        Magnitude(display, magnitude);

    // Peak-hold accumulates over display points: per-bin maxima summed per band would overstate peak band power.
    public List<SignalPoint> MainDisplayPoints(LiveSpectrumDisplay display, double[] magnitude, bool rtaOnly) =>
        rtaOnly
            ? Rta(display, magnitude)
            : Magnitude(display, magnitude, 0.0);

    /// <summary>Trusted and low-coherence (dimmed, dashed) segments sharing boundary points; NaN elsewhere.</summary>
    public (List<SignalPoint> Trusted, List<SignalPoint> Untrusted) CoherenceSplit(
        LiveSpectrumDisplay display,
        double[] magnitude,
        double[] coherence,
        int thresholdPercent)
    {
        List<SignalPoint> magnitudePoints = Magnitude(display, magnitude);
        List<SignalPoint> coherencePoints = Coherence(display, coherence);
        int count = magnitudePoints.Count;
        double threshold = thresholdPercent / 100.0;

        // Different grids, so match coherence by frequency, not index. Missing coverage counts as trusted.
        var trustedFlags = new bool[count];
        int cursor = 0;
        for (int i = 0; i < count; i++)
        {
            trustedFlags[i] =
                NearestCoherence(coherencePoints, magnitudePoints[i].X, ref cursor) >=
                threshold;
        }

        bool IsTrusted(int index) => trustedFlags[index];

        var trusted = new List<SignalPoint>(count);
        var untrusted = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            bool trustedHere = IsTrusted(i);
            bool boundary =
                (i > 0 && IsTrusted(i - 1) != trustedHere) ||
                (i < count - 1 && IsTrusted(i + 1) != trustedHere);
            double frequency = magnitudePoints[i].X;
            double decibels = magnitudePoints[i].Y;

            trusted.Add(new SignalPoint(
                frequency,
                trustedHere || boundary ? decibels : double.NaN));
            untrusted.Add(new SignalPoint(
                frequency,
                !trustedHere || boundary ? decibels : double.NaN));
        }

        return (trusted, untrusted);
    }

    public List<SignalPoint> Coherence(LiveSpectrumDisplay display, double[] coherence) =>
        PlotModelFactory.ResampleCoherence(
            coherence,
            display.Setup.SampleRate,
            display.Setup.SequenceLength,
            display.Options.SmoothingInverseOctaves);

    // SPL: band-integrated power with per-band calibration and offset; native: amplitude-averaged dB.
    // Tilt compensation applies per bin (native) or per band (SPL), whose band laws differ (see NoiseTiltCompensation).
    /// <summary>The RTA on the display grid.</summary>
    /// <param name="applied">Filled with what the band render baked in, for a capture document to record.</param>
    public List<SignalPoint> Rta(
        LiveSpectrumDisplay display,
        double[] amplitudeSpectrum,
        LiveRtaApplied? applied = null)
    {
        LiveCaptureSetup setup = display.Setup;
        NoiseSpectralModel? tiltModel = display.TiltModel;
        if (!display.UsesBandPower)
        {
            return Magnitude(display, amplitudeSpectrum, 0.0, tiltModel);
        }

        int smoothingCode = display.SmoothingCode;
        double smoothingOctaves = SpectrumSmoothing.SmoothingOctaves(smoothingCode);
        bool psychoacoustic = SpectrumSmoothing.IsPsychoacoustic(smoothingCode);
        List<SignalPoint> bands = DataHelper.LogarithmicPowerBandResample(
            amplitudeSpectrum,
            setup.SequenceLength,
            setup.SampleRate,
            setup.WindowEnbwBins,
            setup.WindowMainLobeBins,
            LowHz,
            HighHz,
            PointCount,
            smoothingOctaves,
            psychoacoustic);

        double offsetDb = display.SplRenderOffsetDb;
        CalibrationFile? calibration = setup.MicrophoneCalibration;
        double[]? recordedCorrection =
            applied != null && calibration != null ? new double[bands.Count] : null;
        for (int i = 0; i < bands.Count; i++)
        {
            double correction = calibration?.GetDecibelCorrection(bands[i].X) ?? 0.0;
            if (recordedCorrection != null)
            {
                recordedCorrection[i] = correction;
            }

            bands[i] = new SignalPoint(bands[i].X, bands[i].Y - correction + offsetDb);
        }

        if (applied != null && recordedCorrection != null)
        {
            applied.CalibrationDb = recordedCorrection;
        }

        // The protective HP sits ahead of the speaker, so an MMM capture carries it; divide it out to match swept IRs (plain RTA keeps it).
        // Read from the accumulation: the filter in force during the walk, same field as the saved recipe.
        ProtectiveHighPassConfiguration captureFilter = setup.ProtectiveHighPass;
        if (display.Mode.IsSpatialAverageCapture() && captureFilter.Enabled)
        {
            double[] filter = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
                captureFilter.ToEdge(),
                setup.SampleRate,
                ProtectiveHighPassConfiguration.MaximumCompensationBoostDb,
                bands.Select(band => band.X).ToArray());
            for (int i = 0; i < bands.Count; i++)
            {
                bands[i] = new SignalPoint(bands[i].X, bands[i].Y + filter[i]);
            }

            if (applied != null)
            {
                applied.ProtectiveHighPassDb = filter;
            }
        }

        if (tiltModel is { } bandModel)
        {
            // Same resampler and parameters, so grids align by index; a length mismatch means divergence, so skip.
            double[] compensation = TiltBandCompensation(
                setup, bandModel, amplitudeSpectrum.Length, smoothingOctaves, psychoacoustic);
            if (compensation.Length == bands.Count)
            {
                for (int i = 0; i < bands.Count; i++)
                {
                    bands[i] = new SignalPoint(bands[i].X, bands[i].Y + compensation[i]);
                }

                if (applied != null)
                {
                    applied.TiltDb = compensation;
                }
            }
        }

        return bands;
    }

    private double[] TiltBandCompensation(
        LiveCaptureSetup setup,
        NoiseSpectralModel model,
        int binCount,
        double smoothingOctaves,
        bool psychoacoustic)
    {
        var key = (model, binCount, setup.SequenceLength,
            setup.SampleRate, setup.WindowEnbwBins,
            setup.WindowMainLobeBins, smoothingOctaves, psychoacoustic);
        if (tiltBandCompensation == null || !key.Equals(tiltBandKey))
        {
            tiltBandCompensation = NoiseTiltCompensation.BandCompensationDb(
                model,
                binCount,
                setup.SequenceLength,
                setup.SampleRate,
                setup.WindowEnbwBins,
                setup.WindowMainLobeBins,
                LowHz,
                HighHz,
                PointCount,
                smoothingOctaves,
                psychoacoustic);
            tiltBandKey = key;
        }

        return tiltBandCompensation;
    }

    private static List<SignalPoint> Magnitude(
        LiveSpectrumDisplay display,
        double[] magnitude,
        double offsetDb = 0.0,
        NoiseSpectralModel? tiltCompensationModel = null)
    {
        LiveCaptureSetup setup = display.Setup;
        List<SignalPoint> bins = DataHelper.MagnitudeBinsToDecibels(
            magnitude, setup.SequenceLength, setup.SampleRate, offsetDb);

        // Per bin before resample, where LiveRtaRawCapture bakes it, so re-smoothing a raw capture reproduces this trace.
        if (tiltCompensationModel is { } model)
        {
            for (int i = 0; i < bins.Count; i++)
            {
                bins[i] = new SignalPoint(
                    bins[i].X,
                    bins[i].Y + NoiseTiltCompensation.BinCompensationDb(
                        model, bins[i].X, setup.SampleRate));
            }
        }

        return DataHelper.LogarithmicResample(
            bins,
            LowHz,
            HighHz,
            PointCount,
            setup.MicrophoneCalibration,
            SpectrumSmoothing.SmoothingOctaves(display.SmoothingCode),
            psychoacoustic: SpectrumSmoothing.IsPsychoacoustic(
                display.SmoothingCode));
    }

    // Both lists sorted by X; a forward cursor keeps pairing linear.
    private static double NearestCoherence(
        List<SignalPoint> coherencePoints,
        double frequency,
        ref int cursor)
    {
        if (coherencePoints.Count == 0)
        {
            return 1.0;
        }

        while (cursor + 1 < coherencePoints.Count &&
            coherencePoints[cursor + 1].X <= frequency)
        {
            cursor++;
        }

        double value = coherencePoints[cursor].Y;
        if (cursor + 1 < coherencePoints.Count &&
            coherencePoints[cursor + 1].X - frequency < frequency - coherencePoints[cursor].X)
        {
            value = coherencePoints[cursor + 1].Y;
        }

        return value;
    }
}

/// <summary>What the band render baked in, reported by the render itself (compensation may be skipped on length
/// mismatch).</summary>
internal sealed class LiveRtaApplied
{
    public double[] TiltDb { get; set; } = [];

    /// <summary>Protective high-pass divided out, dB per point; NaN where unrecoverable.</summary>
    public double[] ProtectiveHighPassDb { get; set; } = [];

    /// <summary>Mic correction per point, sign convention of <see cref="CalibrationFile.GetDecibelCorrection"/>; the
    /// render subtracts it.</summary>
    public double[] CalibrationDb { get; set; } = [];
}
