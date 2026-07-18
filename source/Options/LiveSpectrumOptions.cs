using Resonalyze.Dsp;

namespace Resonalyze.Options
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

    public sealed class LiveSpectrumOptions
    {
        /// <summary>Spectral colour of the excitation noise played during measurement.</summary>
        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;

        public MicrophoneCalibrationMode CalibrationMode { get; set; } =
            MicrophoneCalibrationMode.Degrees0;

        public bool UseCalibration
        {
            get => CalibrationMode != MicrophoneCalibrationMode.Off;
            set => CalibrationMode = value
                ? MicrophoneCalibrationMode.Degrees0
                : MicrophoneCalibrationMode.Off;
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
        /// with no scalar SPL under noise excitation, so it is not shown on the SPL axis.
        /// </summary>
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;
    }
}
