using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    public enum MicrophoneCalibrationMode
    {
        Off,
        Degrees0,
        Degrees90
    }

    /// <summary>
    /// The vertical scale of a frequency-response plot: the native
    /// loopback-referenced dB (the default), or absolute dB SPL derived from the
    /// microphone SPL calibration.
    /// </summary>
    public enum MagnitudeScale
    {
        Relative,
        SoundPressureLevel
    }

    public sealed class FrequencyResponseOptions
    {
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 256;
        public int RightTukeyWindow { get; set; } = 256;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        public MicrophoneCalibrationMode CalibrationMode { get; set; } =
            MicrophoneCalibrationMode.Degrees0;

        public bool UseCalibration
        {
            get => CalibrationMode != MicrophoneCalibrationMode.Off;
            set => CalibrationMode = value
                ? MicrophoneCalibrationMode.Degrees0
                : MicrophoneCalibrationMode.Off;
        }

        // Whether the magnitude plot reads in native loopback-referenced dB or in
        // absolute dB SPL. Presentation only: the curves are computed the same way,
        // then shifted to SPL at draw time when a valid calibration is available.
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;

        // Phase-mode windowing (milliseconds): the Tukey gate is left + plateau + right
        // with the peak at the fade-in/plateau boundary. PhaseDetrendMs is the τ used
        // to detrend the excess phase (absolute reference). Phase mode uses these
        // instead of Window/LeftTukeyWindow/RightTukeyWindow/Offset.
        // Single source of truth for the phase-mode defaults. Tune these to taste;
        // they drive the first-run values, the settings-file fallback and the "R"
        // reset buttons.
        public const double DefaultPhaseGateOffsetMs = 0.0;
        public const double DefaultPhaseLeftMs = 0.5;
        public const double DefaultPhasePlateauMs = 4.0;
        public const double DefaultPhaseRightMs = 1.5;
        public const double DefaultPhaseDetrendMs = 0.0;
        public const double DefaultPhaseSmoothingInverseOctaves = 12.0;

        // Auto keeps the gate offset snapped to the estimated IR start
        // (TransferIrDiagnostics.EstimateIrStart) whenever the measurement
        // changes; off leaves the offset to the user. Default on: a first-run
        // user should see a correctly gated phase without touching anything.
        public bool PhaseGateAutoFit { get; set; } = true;

        public double PhaseGateOffsetMs { get; set; } = DefaultPhaseGateOffsetMs;
        public double PhaseLeftMs { get; set; } = DefaultPhaseLeftMs;
        public double PhasePlateauMs { get; set; } = DefaultPhasePlateauMs;
        public double PhaseRightMs { get; set; } = DefaultPhaseRightMs;
        public double PhaseDetrendMs { get; set; } = DefaultPhaseDetrendMs;
        public PhaseWindowMode PhaseWindowMode { get; set; } =
            PhaseWindowMode.FrequencyDependent;
        public int PhaseFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public PhaseDetrendMode PhaseDetrendMode { get; set; } = PhaseDetrendMode.Auto;

        public PhaseAnalysisSettings CreatePhaseAnalysisSettings() => new(
            PhaseWindowMode,
            PhaseFdwCycles,
            PhaseDetrendMode,
            PhaseDetrendMs,
            PhaseGateOffsetMs,
            PhaseLeftMs,
            PhasePlateauMs,
            PhaseRightMs,
            Unwrap,
            SmoothingInverseOctaves);

        // Single source of truth for the group-delay gate defaults (ms). Group delay is
        // usually viewed a bit lower than the phase crossover region, so the gate is
        // slightly wider than the phase default.
        public const double DefaultGroupDelayGateOffsetMs = 0.0;
        public const double DefaultGroupDelayLeftMs = 0.5;
        public const double DefaultGroupDelayPlateauMs = 10.0;
        public const double DefaultGroupDelayRightMs = 3.0;
        public const double DefaultGroupDelaySmoothingInverseOctaves = 12.0;

        // The Group Delay twin of PhaseGateAutoFit.
        public bool GroupDelayGateAutoFit { get; set; } = true;

        public double GroupDelayGateOffsetMs { get; set; } = DefaultGroupDelayGateOffsetMs;
        public double GroupDelayLeftMs { get; set; } = DefaultGroupDelayLeftMs;
        public double GroupDelayPlateauMs { get; set; } = DefaultGroupDelayPlateauMs;
        public double GroupDelayRightMs { get; set; } = DefaultGroupDelayRightMs;

        // The lowest frequency the gated window can resolve (~one period inside the
        // gate). Driven purely by the gate duration, not the sample rate or FFT size.
        public static double GateMinReliableFrequencyHz(
            double leftMs,
            double plateauMs,
            double rightMs)
        {
            double gateMs = leftMs + plateauMs + rightMs;
            return gateMs > 0.0 ? 1000.0 / gateMs : 0.0;
        }

    }

    public sealed class ImpulseResponseOptions
    {
        public int Length { get; set; } = 4096;
        public bool Logarithmic { get; set; }

        // Curve visibility. Impulse Response and Autocorrelation modes share this
        // options type but read their own flag.
        public bool ShowImpulse { get; set; } = true;
        public bool ShowAutocorrelation { get; set; } = true;
    }

    /// <summary>
    /// Converts measured impulse responses into frequency-domain and time-domain plot data.
    /// </summary>
    public static partial class DataHelper
    {
        private const double MinimumAmplitude = 1e-8;

        public static double AmplitudeToDecibels(double amplitude)
        {
            return 20.0 * Math.Log10(Math.Max(amplitude, MinimumAmplitude));
        }

        public static double DecibelsToAmplitude(double decibels)
        {
            return Math.Pow(10.0, decibels / 20.0);
        }

        /// <summary>
        /// Converts the magnitude bins of a real FFT to ascending (Hz, dB) points, skipping
        /// the DC bin (no place on a logarithmic axis) and stopping below Nyquist.
        /// <paramref name="offsetDb"/> shifts every level (e.g. a reference offset). The
        /// result is the UNSMOOTHED spectrum: callers resample it for display or store it as
        /// a raw reference. A non-positive <paramref name="fftLength"/> or
        /// <paramref name="sampleRate"/> yields an empty list.
        /// </summary>
        public static List<SignalPoint> MagnitudeBinsToDecibels(
            IReadOnlyList<double> magnitude,
            int fftLength,
            int sampleRate,
            double offsetDb = 0.0)
        {
            ArgumentNullException.ThrowIfNull(magnitude);

            int binCount = Math.Min(fftLength / 2, magnitude.Count);
            var points = new List<SignalPoint>(Math.Max(0, binCount - 1));
            if (fftLength <= 0 || sampleRate <= 0)
            {
                return points;
            }

            double binWidth = (double)sampleRate / fftLength;
            for (int i = 1; i < binCount; i++)
            {
                points.Add(new SignalPoint(
                    i * binWidth, AmplitudeToDecibels(magnitude[i]) + offsetDb));
            }

            return points;
        }

        public static Complex[] ExtractWindow(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null,
            bool wrap = false)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Complex[] source = measurement.ImpulseResponse
                ?? throw new InvalidOperationException("Impulse response is not available.");
            Complex[] result = new Complex[length];

            for (int i = 0; i < length; i++)
            {
                int sourceIndex = start + i;
                if (wrap)
                {
                    sourceIndex %= source.Length;
                    if (sourceIndex < 0)
                    {
                        sourceIndex += source.Length;
                    }
                }

                if ((uint)sourceIndex < (uint)source.Length)
                {
                    result[i] = source[sourceIndex] *
                        (window is { Length: > 0 } && i < window.Length ? window[i] : 1.0);
                }
            }

            return result;
        }
    }
}
