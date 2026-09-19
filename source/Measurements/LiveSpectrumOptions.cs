using Resonalyze.Dsp;

namespace Resonalyze
{
    /// <summary>Excitation colour; <see cref="PinkPeriodic"/> (one looped FFT period, deterministic) is the default.</summary>
    public enum NoiseColor
    {
        PinkPeriodic,
        Pink,
        Brown,
        White,
        Silent
    }

    public enum AveragingSpeed
    {
        Fast,
        Medium,
        Slow,
        Infinite
    }

    /// <summary>Live analysis mode. See docs/tech/sweep-measurement.md#live-analysis-modes.</summary>
    public enum LiveAnalysisMode
    {
        TransferFunction,
        Rta,
        Mmm
    }

    public static class LiveAnalysisModes
    {
        /// <summary>RTA or MMM. Ask this, never <c>== Rta</c>: MMM shares the whole reference-free path.</summary>
        public static bool IsReferenceFree(this LiveAnalysisMode mode) =>
            mode is LiveAnalysisMode.Rta or LiveAnalysisMode.Mmm;

        /// <summary>Pinned-recipe spatial-average capture; ask this, never <c>== Mmm</c>, so future array modes join in one place.</summary>
        public static bool IsSpatialAverageCapture(this LiveAnalysisMode mode) =>
            mode is LiveAnalysisMode.Mmm;
    }

    /// <summary>Single list for the options panel and settings schema; 32768/65536 exist for MMM resolution.</summary>
    public static class LiveSequenceLengths
    {
        public static readonly IReadOnlyList<int> Supported =
            [256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];

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
        public LiveAnalysisMode AnalysisMode { get; set; } = LiveAnalysisMode.TransferFunction;

        public NoiseColor NoiseColor { get; set; } = NoiseColor.PinkPeriodic;

        /// <summary>Undoes the excitation tilt on the RTA display; inert for Transfer and Silent.</summary>
        public bool CompensateNoiseTilt { get; set; }

        public string? CalibrationId { get; set; } = MicrophoneCalibrationIds.ZeroDegrees;

        public bool UseCalibration
        {
            get => !MicrophoneCalibrationIds.IsOff(CalibrationId);
            set => CalibrationId = value ? MicrophoneCalibrationIds.ZeroDegrees : null;
        }
        public int SequenceLength { get; set; } = 2048;

        public WindowType WindowType { get; set; } = WindowType.Hann;

        public AveragingSpeed AveragingSpeed { get; set; } = AveragingSpeed.Medium;

        public bool ShowMainCurve { get; set; } = true;

        public bool ShowInputMagnitude { get; set; }

        public bool PeakHold { get; set; }

        public bool ShowCoherence { get; set; } = true;

        public int CoherenceThresholdPercent { get; set; } = 25;

        public int OverlapPercent { get; set; } = 50;

        /// <summary>Inverse octave fraction (6 = 1/6 octave); 0 disables.</summary>
        public int SmoothingInverseOctaves { get; set; } = 6;

        /// <summary>SPL applies only to reference-free modes; a transfer plot always renders relative.</summary>
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;

        /// <summary>MMM pins periodic pink (exact 1/sqrt(f), rate-independent). The pin lives here so analyzer and plot cannot drift.</summary>
        public NoiseColor EffectiveNoiseColor =>
            AnalysisMode.IsSpatialAverageCapture()
                ? NoiseColor.PinkPeriodic
                : NoiseColor;

        /// <summary>MMM pins Infinite: an exponential window would weight the end of the microphone path.</summary>
        public AveragingSpeed EffectiveAveragingSpeed =>
            AnalysisMode.IsSpatialAverageCapture()
                ? AveragingSpeed.Infinite
                : AveragingSpeed;

        /// <summary>Silent is RTA-only (Transfer needs an excitation), so Transfer falls back to periodic pink. Other signals are valid in both modes.</summary>
        /// <returns>Whether the signal changed.</returns>
        public bool NormalizeSignalType()
        {
            if (AnalysisMode == LiveAnalysisMode.TransferFunction &&
                NoiseColor == NoiseColor.Silent)
            {
                NoiseColor = NoiseColor.PinkPeriodic;
                return true;
            }

            return false;
        }
    }

    /// <summary>Synthesised spectral shape the tilt compensation undoes. See docs/tech/sweep-measurement.md#live-analysis-modes.</summary>
    internal static class NoiseColorTilt
    {
        public static NoiseSpectralModel? SpectralModel(NoiseColor color) => color switch
        {
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
