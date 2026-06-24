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
    }
}
