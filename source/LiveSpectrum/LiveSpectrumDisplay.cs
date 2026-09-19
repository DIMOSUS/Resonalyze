using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// How Live Spectrum draws what the analyzer holds under the current options: the mode in effect, the axis, the SPL
/// anchor and the transforms the curves go through. One read serves a whole build, so the plot, the peak hold, an
/// overlay capture and a saved capture agree.
/// </summary>
/// <remarks>See docs/tech/live-spectrum.md#scale-and-spl-view-only.</remarks>
internal sealed record LiveSpectrumDisplay(
    LiveSpectrumOptions Options,
    LiveCaptureSetup Setup,
    LiveAnalysisMode Mode,
    MagnitudeScale Scale,
    double? SplOffsetDb,
    NoiseSpectralModel? TiltModel,
    int SmoothingCode)
{
    /// <param name="splAnchor">The configured SPL calibration; it counts only when taken on the live input.</param>
    public static LiveSpectrumDisplay Of(
        LiveSpectrumOptions options,
        LiveCaptureSetup setup,
        SplCalibration? splAnchor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(setup);

        // The selection, forced to RTA without a loopback reference.
        LiveAnalysisMode mode =
            setup.IsMicOnly && options.AnalysisMode == LiveAnalysisMode.TransferFunction
                ? LiveAnalysisMode.Rta
                : options.AnalysisMode;

        // Raw mic dBFS to dB SPL, with no loopback term; validated against the live input, not the sweep's.
        double? splOffsetDb = splAnchor is { } anchor && anchor.MatchesInput(setup.Input)
            ? anchor.OffsetDb
            : null;

        // A spatial average is absolute only with an anchor: relative band levels on the SPL axis fall below its
        // -20 floor. Tested by trait, not identity, so band power and axis scale stay consistent. A transfer
        // function is a dimensionless ratio, always relative.
        MagnitudeScale scale = mode.IsSpatialAverageCapture()
            ? splOffsetDb.HasValue ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative
            : mode.IsReferenceFree()
                ? options.MagnitudeScale
                : MagnitudeScale.Relative;

        // Null when off (MMM forces it on), not reference-free, or Silent. Flat is a real value.
        NoiseSpectralModel? tiltModel =
            mode.IsReferenceFree() &&
            (options.CompensateNoiseTilt || mode.IsSpatialAverageCapture())
                ? NoiseColorTilt.SpectralModel(options.EffectiveNoiseColor)
                : null;

        // MMM pins smoothing Off: the SPL path already integrates a fixed 1/12-octave band, and a capture recipe must
        // not vary across a set.
        int smoothingCode = mode.IsSpatialAverageCapture()
            ? 0
            : options.SmoothingInverseOctaves;

        return new LiveSpectrumDisplay(options, setup, mode, scale, splOffsetDb, tiltModel, smoothingCode);
    }

    /// <summary>The microphone spectrum alone: no transfer function, no coherence, and the RTA forced on.</summary>
    public bool RtaOnly => Mode.IsReferenceFree();

    public bool RendersSpl => Scale == MagnitudeScale.SoundPressureLevel;

    /// <summary>dB SPL selected without a matching anchor: the axis and SPL overlays show, live curves do not.</summary>
    /// <remarks>MMM never gets here: without an anchor it reports a relative scale.</remarks>
    public bool SplViewOnly => RendersSpl && SplOffsetDb == null;

    /// <summary>Band power rather than per-bin dB; separate from the axis, since a spatial average needs band levels but
    /// no absolute reference.</summary>
    public bool UsesBandPower => Mode.IsSpatialAverageCapture() || RendersSpl;

    public double SplRenderOffsetDb => RendersSpl ? SplOffsetDb ?? 0.0 : 0.0;

    /// <summary>The RTA is read, not only drawn, whenever it is shown or is the only curve.</summary>
    public bool NeedsInputMagnitude => Options.ShowInputMagnitude || RtaOnly;

    /// <summary>The transform behind a peak-hold envelope; any change must drop the envelope.</summary>
    /// <remarks>See docs/tech/live-spectrum.md#peak-hold.</remarks>
    public LivePeakHoldKey PeakHoldKey => new(
        RendersSpl ? MagnitudeScale.SoundPressureLevel : MagnitudeScale.Relative,
        RtaOnly,
        SmoothingCode,
        RendersSpl ? SplOffsetDb : null,
        TiltModel);
}
