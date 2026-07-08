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
        public bool ShowCoherence { get; set; } = true;

        // Phase-mode curve visibility. Ignored by the other modes that reuse this
        // options type.
        public bool ShowMeasuredPhase { get; set; } = true;
        public bool ShowMinimumPhase { get; set; } = true;
        public bool ShowExcessPhase { get; set; } = true;

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

        public double PhaseGateOffsetMs { get; set; } = DefaultPhaseGateOffsetMs;
        public double PhaseLeftMs { get; set; } = DefaultPhaseLeftMs;
        public double PhasePlateauMs { get; set; } = DefaultPhasePlateauMs;
        public double PhaseRightMs { get; set; } = DefaultPhaseRightMs;
        public double PhaseDetrendMs { get; set; } = DefaultPhaseDetrendMs;

        // Single source of truth for the group-delay gate defaults (ms). Group delay is
        // usually viewed a bit lower than the phase crossover region, so the gate is
        // slightly wider than the phase default.
        public const double DefaultGroupDelayGateOffsetMs = 0.0;
        public const double DefaultGroupDelayLeftMs = 0.5;
        public const double DefaultGroupDelayPlateauMs = 10.0;
        public const double DefaultGroupDelayRightMs = 3.0;
        public const double DefaultGroupDelaySmoothingInverseOctaves = 12.0;

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

        // Frequency-response curve visibility. Ignored by the other modes that reuse
        // this options type.
        public bool ShowPrimary { get; set; } = true;
        public bool ShowHd2 { get; set; } = true;
        public bool ShowHd3 { get; set; } = true;
        public bool ShowHd4 { get; set; } = true;
        public bool ShowThdPlusNoise { get; set; } = true;

        // Group-delay-mode curve visibility. Ignored by the other modes that reuse
        // this options type.
        public bool ShowGroupDelay { get; set; } = true;
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
