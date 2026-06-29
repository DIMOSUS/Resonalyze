using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public enum LiveSpectrumMode
    {
        TransferFunction,
        InputSpectrum
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
        public LiveSpectrumMode Mode { get; set; } = LiveSpectrumMode.TransferFunction;
        public bool UseCalibration { get; set; } = true;
        public int SequenceLength { get; set; } = 2048;

        /// <summary>Analysis window applied before the FFT.</summary>
        public WindowType WindowType { get; set; } = WindowType.Hann;

        /// <summary>Exponential/cumulative averaging speed preset.</summary>
        public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Medium;

        /// <summary>Shows the main live trace (the spectrum / transfer-function curve).</summary>
        public bool ShowMainCurve { get; set; } = true;

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
    }
}
