using Resonalyze.Dsp;

// Lives beside the measurement that owns it rather than in Options/, which holds
// the WinForms settings panels: NoiseMeasurement takes this object by reference
// and reads it on the analysis path, so it is measurement state, not panel state.
namespace Resonalyze
{
    /// <summary>
    /// Spectral colour of the excitation signal played during a live measurement.
    /// Ordered by general usefulness for a dual-FFT analyzer:
    /// <list type="bullet">
    /// <item><see cref="PinkPeriodic"/> — pink noise synthesised as one FFT-length
    /// period and looped. Exactly pink and deterministic, so the transfer-function
    /// average converges quickly with no random variance. The default.</item>
    /// <item><see cref="Pink"/> — continuous (random) pink noise, −3 dB/octave.</item>
    /// <item><see cref="Brown"/> — brown/red noise, −6 dB/octave: even more
    /// low-frequency drive, useful for subwoofer and room-mode work.</item>
    /// <item><see cref="White"/> — flat energy per hertz.</item>
    /// </list>
    /// </summary>
    public enum NoiseColor
    {
        PinkPeriodic,
        Pink,
        Brown,
        White,
        Silent
    }

    /// <summary>
    /// Preset averaging speeds for the live analyzer. Fast/Medium/Slow map to
    /// exponential time constants; Infinite is a cumulative (never-forgetting)
    /// average that keeps integrating until reset.
    /// </summary>
    public enum AveragingSpeed
    {
        Fast,
        Medium,
        Slow,
        Infinite
    }

    /// <summary>
    /// What the live analyzer measures.
    /// <list type="bullet">
    /// <item><see cref="TransferFunction"/> — the dual-channel H1 estimate of
    /// microphone over loopback reference, with coherence. Needs a configured
    /// loopback channel; without one the analyzer can only run reference-free,
    /// so the effective mode falls back to <see cref="Rta"/>.</item>
    /// <item><see cref="Rta"/> — the reference-free magnitude spectrum of the
    /// microphone input alone. Shows absolute dB SPL, and the only mode where the
    /// <see cref="NoiseColor.Silent"/> (ambient) signal makes sense.</item>
    /// <item><see cref="Mmm"/> — the same reference-free spectrum, captured under
    /// the one recipe a moving-microphone measurement is valid under. It is not a
    /// preset of <see cref="Rta"/> but a mode of its own, because the settings it
    /// pins are not preferences: a spatial average read through the wrong window,
    /// averaging or slope compensation is wrong by a smooth, plausible-looking
    /// trend that nothing downstream can detect (see MMM-PLAN §5.2 in the owner's
    /// notes — a missing slope compensation reads 13 dB hot at 20 Hz).</item>
    /// </list>
    /// </summary>
    public enum LiveAnalysisMode
    {
        TransferFunction,
        Rta,
        Mmm
    }

    public static class LiveAnalysisModes
    {
        /// <summary>
        /// Whether the mode renders the reference-free microphone spectrum rather
        /// than a dual-channel transfer function — true for both <see
        /// cref="LiveAnalysisMode.Rta"/> and <see cref="LiveAnalysisMode.Mmm"/>.
        /// </summary>
        /// <remarks>
        /// Every "is this mic-only?" decision asks THIS, never <c>== Rta</c>: MMM
        /// shares the whole accumulation and rendering path with the RTA and
        /// differs only in which settings it allows, so a mode comparison left as
        /// an equality test is a mode MMM silently falls out of.
        /// </remarks>
        public static bool IsReferenceFree(this LiveAnalysisMode mode) =>
            mode is LiveAnalysisMode.Rta or LiveAnalysisMode.Mmm;

        /// <summary>
        /// Whether the mode captures a SPATIAL AVERAGE under a pinned recipe: band
        /// power on a fixed excitation, cumulative averaging, slope compensation on,
        /// smoothing off, and an accumulation that is the measurement rather than a
        /// running display.
        /// </summary>
        /// <remarks>
        /// A trait, not an identity test. Everything that treats MMM specially asks
        /// THIS rather than <c>== Mmm</c>, so the planned microphone-array mode joins
        /// by being added here instead of by a sweep through a dozen call sites —
        /// where missing one would not fail loudly but would capture under the wrong
        /// excitation model or averaging and produce a smooth, plausible, wrong curve.
        /// </remarks>
        public static bool IsSpatialAverageCapture(this LiveAnalysisMode mode) =>
            mode is LiveAnalysisMode.Mmm;
    }

    /// <summary>
    /// The analysis frame lengths the live analyzer offers, and the rule for
    /// snapping a stored value onto them.
    /// </summary>
    /// <remarks>
    /// One list, read by both the options panel and the settings schema. They used
    /// to carry a copy each, and a length added to only one of them is not a
    /// cosmetic bug: the panel would offer it and the schema would floor it back on
    /// the next save, so the setting would appear to take and then quietly revert.
    /// <para>
    /// 32768 and 65536 exist for MMM. What a band-power display resolves is set by
    /// the frame DURATION — a rectangular window resolves 2/T hertz, a Hann one 4/T
    /// — so the same resolution costs twice the samples at twice the rate.
    /// </para>
    /// </remarks>
    public static class LiveSequenceLengths
    {
        public static readonly IReadOnlyList<int> Supported =
            [256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];

        /// <summary>
        /// The largest supported length that does not exceed <paramref
        /// name="sequenceLength"/>, or the smallest one when it sits below them all.
        /// </summary>
        public static int Normalize(int sequenceLength)
        {
            int normalized = Supported[0];
            foreach (int candidate in Supported)
            {
                if (sequenceLength >= candidate)
                {
                    normalized = candidate;
                }
            }

            return normalized;
        }
    }

    public sealed class LiveSpectrumOptions
    {
        /// <summary>
        /// The selected analysis mode. The invariant that <see cref="NoiseColor.Silent"/>
        /// exists only in <see cref="LiveAnalysisMode.Rta"/> is enforced at settings load
        /// and by <c>LiveSpectrumController.NormalizeSignalType</c>, never assumed here.
        /// </summary>
        public LiveAnalysisMode AnalysisMode { get; set; } = LiveAnalysisMode.TransferFunction;

        /// <summary>Spectral colour of the excitation noise played during measurement.</summary>
        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;

        /// <summary>
        /// Compensates the tilt the excitation noise itself prints onto the
        /// reference-free RTA display (pink reads −3 dB/octave on the per-bin dB
        /// axis even through a flat system), so a flat system reads flat. RTA mode
        /// only — the transfer function divides the excitation out — and inert for
        /// <see cref="NoiseColor.Silent"/>, whose excitation spectrum is unknown.
        /// </summary>
        public bool CompensateNoiseTilt { get; set; }

        /// <summary>
        /// Which microphone calibration corrects the live curves, by id (see
        /// <see cref="MicrophoneCalibrationIds"/>); null means uncalibrated.
        /// </summary>
        public string? CalibrationId { get; set; } = MicrophoneCalibrationIds.ZeroDegrees;

        public bool UseCalibration
        {
            get => !MicrophoneCalibrationIds.IsOff(CalibrationId);
            set => CalibrationId = value ? MicrophoneCalibrationIds.ZeroDegrees : null;
        }
        public int SequenceLength { get; set; } = 2048;

        /// <summary>Analysis window applied before the FFT.</summary>
        public WindowType WindowType { get; set; } = WindowType.Hann;

        /// <summary>Exponential/cumulative averaging speed preset.</summary>
        public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Medium;

        /// <summary>Shows the main live trace (the spectrum / transfer-function curve).</summary>
        public bool ShowMainCurve { get; set; } = true;

        /// <summary>
        /// Overlays a reference-free RTA curve: the plain magnitude spectrum of the
        /// measured (microphone) input, with no division by the loopback reference.
        /// Off by default.
        /// </summary>
        public bool ShowInputMagnitude { get; set; }

        /// <summary>Shows a peak-hold envelope curve of the displayed trace.</summary>
        public bool PeakHold { get; set; }

        /// <summary>
        /// Shows the coherence (γ²) curve in Transfer Function mode.
        /// </summary>
        public bool ShowCoherence { get; set; } = true;

        /// <summary>
        /// Coherence threshold (percent) below which the transfer-function curve
        /// is drawn dimmed and dashed to flag untrustworthy frequencies. Zero
        /// disables the marking.
        /// </summary>
        public int CoherenceThresholdPercent { get; set; } = 25;

        /// <summary>
        /// Fractional overlap between successive analysis frames, in percent.
        /// Supported values are 0 (no overlap), 50, and 75. Higher overlap
        /// reclaims samples discarded by the analysis window, giving faster and
        /// smoother averaging at the cost of more FFTs per second.
        /// </summary>
        public int OverlapPercent { get; set; } = 50;

        /// <summary>
        /// Fractional-octave smoothing applied to the displayed curve, expressed
        /// as the inverse octave fraction (for example 6 means 1/6 octave).
        /// Zero disables smoothing.
        /// </summary>
        public int SmoothingInverseOctaves { get; set; } = 6;

        /// <summary>
        /// Vertical scale of the live plot. In <see cref="MagnitudeScale.SoundPressureLevel"/>
        /// the reference-free RTA (microphone) spectrum is shown in absolute dB SPL
        /// (mic + calibration offset). The transfer function is a dimensionless ratio
        /// with no scalar SPL under noise excitation, so the scale takes effect only in
        /// <see cref="LiveAnalysisMode.Rta"/>; a transfer plot always renders relative.
        /// </summary>
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;

        /// <summary>
        /// The excitation colour the selected mode actually uses. MMM pins periodic
        /// pink: its spectrum is exactly <c>1/√f</c> and, unlike the Kellett bank
        /// behind <see cref="NoiseColor.Pink"/> whose poles sit in normalized
        /// frequency, the model the slope compensation undoes does not move with the
        /// sample rate.
        /// </summary>
        /// <remarks>
        /// The pin lives on the OPTIONS, not on the analyzer or the plot factory,
        /// because both of those read it and they do not always hold the same options
        /// instance — a rule implemented twice is a rule that drifts, and one of the
        /// two copies would have been the one deciding what the tilt compensation
        /// undoes.
        /// </remarks>
        public NoiseColor EffectiveNoiseColor =>
            AnalysisMode.IsSpatialAverageCapture()
                ? NoiseColor.PinkPeriodic
                : NoiseColor;

        /// <summary>
        /// The averaging preset the selected mode actually uses. MMM pins Infinite: a
        /// spatial average is a cumulative mean of frame power over the whole
        /// microphone path, and an exponential window would weight the end of that
        /// path over its beginning.
        /// </summary>
        public AveragingSpeed EffectiveAveragingSpeed =>
            AnalysisMode.IsSpatialAverageCapture()
                ? AveragingSpeed.Infinite
                : AveragingSpeed;
    }

    /// <summary>
    /// The spectral shape each excitation colour is actually SYNTHESISED with, as
    /// the model the tilt compensation must undo (see
    /// <see cref="Dsp.NoiseTiltCompensation"/>). Ideal power laws only where the
    /// synthesis is one: periodic pink is exactly <c>1/√k</c> per bin and white is
    /// flat, but random pink is the Kellett filter bank and brown a leaky
    /// integrator — both flatten below their filter corners, and compensating a
    /// nominal slope there would print an artificial bass roll-off onto a correct
    /// measurement.
    /// </summary>
    internal static class NoiseColorTilt
    {
        /// <summary>
        /// The spectral model of the colour, or null when the excitation spectrum is
        /// unknown (<see cref="NoiseColor.Silent"/> — an external source) and no
        /// compensation can be honest.
        /// </summary>
        public static NoiseSpectralModel? SpectralModel(NoiseColor color) => color switch
        {
            // Exactly 1/√k per synthesised bin: PSD ∝ 1/f → −10·log10(2) dB/octave.
            NoiseColor.PinkPeriodic =>
                NoiseSpectralModel.PowerLaw(-10.0 * Math.Log10(2.0)),
            NoiseColor.Pink => NoiseSpectralModel.KellettPink,
            NoiseColor.Brown =>
                NoiseSpectralModel.LeakyIntegrator(NoiseSignal.BrownCornerHz),
            NoiseColor.White => NoiseSpectralModel.PowerLaw(0.0),
            _ => null
        };
    }
}
