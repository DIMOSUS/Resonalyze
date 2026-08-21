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
    /// microphone input alone. The only mode that can show absolute dB SPL, and
    /// the only mode where the <see cref="NoiseColor.Silent"/> (ambient) signal
    /// makes sense.</item>
    /// </list>
    /// </summary>
    public enum LiveAnalysisMode
    {
        TransferFunction,
        Rta
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
