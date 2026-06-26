namespace Resonalyze.Options
{
    public enum LiveSpectrumMode
    {
        TransferFunction,
        InputSpectrum
    }

    public sealed class LiveSpectrumOptions
    {
        public LiveSpectrumMode Mode { get; set; } = LiveSpectrumMode.TransferFunction;
        public bool UseCalibration { get; set; } = true;
        public int SequenceLength { get; set; } = 2048;

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
