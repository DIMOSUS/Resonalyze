using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    /// <summary>Calibration ids passed around by the measurement layer; the app resolves them. Null or empty = no correction.</summary>
    public static class MicrophoneCalibrationIds
    {
        public const string ZeroDegrees = "0deg";

        /// <summary>The curve frozen into the measurement when its run began.</summary>
        public const string Own = "own";

        public static bool IsOwn(string? calibrationId) =>
            string.Equals(calibrationId, Own, StringComparison.OrdinalIgnoreCase);

        public static bool IsOff(string? calibrationId) =>
            string.IsNullOrEmpty(calibrationId);

        public static string? Normalize(string? calibrationId) =>
            string.IsNullOrWhiteSpace(calibrationId) ? null : calibrationId.Trim();
    }

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

        // Fixed by default: steady state is the canonical magnitude (in-car SPL targets); phase defaults the other way.
        public PhaseWindowMode MagnitudeWindowMode { get; set; } = PhaseWindowMode.Fixed;
        public int MagnitudeFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        public string? CalibrationId { get; set; } = MicrophoneCalibrationIds.ZeroDegrees;

        public bool UseCalibration
        {
            get => !MicrophoneCalibrationIds.IsOff(CalibrationId);
            set => CalibrationId = value ? MicrophoneCalibrationIds.ZeroDegrees : null;
        }

        // Presentation only: curves are shifted to SPL at draw time.
        public MagnitudeScale MagnitudeScale { get; set; } = MagnitudeScale.Relative;

        // Phase gate defaults (ms); drive first run, settings fallback and the "R" reset buttons.
        public const double DefaultPhaseGateOffsetMs = 0.0;
        public const double DefaultPhaseLeftMs = 0.5;
        public const double DefaultPhasePlateauMs = 4.0;
        public const double DefaultPhaseRightMs = 1.5;
        public const double DefaultPhaseDetrendMs = 0.0;
        public const double DefaultPhaseSmoothingInverseOctaves = 12.0;

        // One steady-state magnitude window for VDSP and EQ Wizard, deliberately not the user's gate. See docs/tech/phase-and-group-delay.md#steady-state-magnitude-window.
        public const double SteadyStateLeftMs = 2.0;
        public const double SteadyStatePlateauMs = 500.0;
        public const double SteadyStateRightMs = 180.0;

        public static (int Window, int LeftTukey, int RightTukey)
            SteadyStateWindowSamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            return TrimGateToFft(
                (int)Math.Round(SteadyStateLeftMs / 1_000.0 * sampleRate),
                (int)Math.Round(SteadyStatePlateauMs / 1_000.0 * sampleRate),
                (int)Math.Round(SteadyStateRightMs / 1_000.0 * sampleRate));
        }

        /// <summary>Caps a Tukey geometry at <see cref="DataHelper.GatedFftLength"/>, sharing the loss between plateau and fade-out. See docs/tech/phase-and-group-delay.md#steady-state-magnitude-window.</summary>
        public static (int Window, int LeftTukey, int RightTukey) TrimGateToFft(
            int left, int plateau, int right)
        {
            left = Math.Max(0, left);
            plateau = Math.Max(0, plateau);
            right = Math.Max(0, right);
            int window = Math.Clamp(
                left + plateau + right, 1, DataHelper.GatedFftLength);

            // Left fade keeps its absolute length (anti-edge on the arrival); clamped so it cannot eat the window.
            left = Math.Min(left, window - 1);
            int remaining = window - left;
            int wanted = plateau + right;
            if (wanted > remaining && wanted > 0)
            {
                right = (int)Math.Round((double)right / wanted * remaining);
            }

            return (window, left, Math.Clamp(right, 0, remaining));
        }

        // Auto snaps the gate offset to TransferIrDiagnostics.EstimateIrStart on every measurement change.
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

        public FrequencyResponseOptions WithSmoothing(double smoothingInverseOctaves)
        {
            FrequencyResponseOptions copy = Copy();
            copy.SmoothingInverseOctaves = smoothingInverseOctaves;
            return copy;
        }

        public FrequencyResponseOptions Copy() => (FrequencyResponseOptions)MemberwiseClone();

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

        public const double DefaultGroupDelayGateOffsetMs = 0.0;
        public const double DefaultGroupDelayLeftMs = 0.5;
        public const double DefaultGroupDelayPlateauMs = 10.0;
        public const double DefaultGroupDelayRightMs = 3.0;
        public const double DefaultGroupDelaySmoothingInverseOctaves = 12.0;

        public bool GroupDelayGateAutoFit { get; set; } = true;

        public double GroupDelayGateOffsetMs { get; set; } = DefaultGroupDelayGateOffsetMs;
        public double GroupDelayLeftMs { get; set; } = DefaultGroupDelayLeftMs;
        public double GroupDelayPlateauMs { get; set; } = DefaultGroupDelayPlateauMs;
        public double GroupDelayRightMs { get; set; } = DefaultGroupDelayRightMs;

        // Settings files predating this field open on Fixed (see MeasurementSettingsFile).
        public PhaseWindowMode GroupDelayWindowMode { get; set; } =
            PhaseWindowMode.FrequencyDependent;
        public int GroupDelayFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;

        public PhaseAnalysisSettings CreateGroupDelayAnalysisSettings() => new(
            GroupDelayWindowMode,
            GroupDelayFdwCycles,
            PhaseDetrendMode.Off,
            ManualDetrendMilliseconds: 0.0,
            GroupDelayGateOffsetMs,
            GroupDelayLeftMs,
            GroupDelayPlateauMs,
            GroupDelayRightMs,
            Unwrap: false,
            SmoothingInverseOctaves: 0.0);

        // ~One period inside the gate; depends only on gate duration.
        public static double GateMinReliableFrequencyHz(
            double leftMs,
            double plateauMs,
            double rightMs)
        {
            double gateMs = leftMs + plateauMs + rightMs;
            return gateMs > 0.0 ? 1000.0 / gateMs : 0.0;
        }

    }

    public enum ImpulseTimeUnit
    {
        Samples,
        Milliseconds
    }

    /// <summary>View-only: never rewrites the record, since alignment, gate pins and saved offsets use its absolute timeline.</summary>
    public enum ImpulseTimeOrigin
    {
        RecordStart,
        FirstArrival,
        Peak
    }

    public enum ImpulseAmplitudeScale
    {
        Linear,

        PercentOfPeak,

        Decibels
    }

    public sealed class ImpulseResponseOptions
    {
        /// <summary>Initial view framing past the peak, in samples; traces always span the whole record.</summary>
        public int Length { get; set; } = 4096;

        public bool ShowImpulse { get; set; } = true;
        public bool ShowEnvelope { get; set; }
        public bool ShowStep { get; set; }
        public bool ShowAutocorrelation { get; set; } = true;

        public ImpulseTimeUnit TimeUnit { get; set; } = ImpulseTimeUnit.Milliseconds;
        public ImpulseTimeOrigin TimeOrigin { get; set; } = ImpulseTimeOrigin.RecordStart;
        public ImpulseAmplitudeScale AmplitudeScale { get; set; } =
            ImpulseAmplitudeScale.Linear;

        public double EnvelopeSmoothingMs { get; set; }

        public bool Invert { get; set; }

        public bool NormalizeStepToImpulsePeak { get; set; } = true;

        /// <summary>Zero-phase band width in octaves; zero draws the broadband record.</summary>
        public double BandFilterOctaves { get; set; }

        public double BandCenterHz { get; set; } = 1000.0;

        public ImpulseResponseOptions Copy() => (ImpulseResponseOptions)MemberwiseClone();

        /// <summary>The whole octave-symmetric passband must fit under Nyquist (1 oct at 16 kHz needs 22.6 kHz); only the fade skirt may clip.</summary>
        public bool HasBandFilter(int sampleRate)
        {
            if (BandFilterOctaves <= 0.0 || BandCenterHz <= 0.0 || sampleRate <= 0)
            {
                return false;
            }

            (_, _, double passbandHighHz, _) = BandpassWindow.BandAround(
                BandCenterHz, BandFilterOctaves, 0.0);
            return passbandHighHz <= sampleRate / 2.0;
        }
    }

    /// <summary>View-only framing: fractional axis-zero sample and the normalization peak (null = the set's own; pass the main set's for Compare).</summary>
    public readonly record struct ImpulseRenderFrame(
        double OriginSamples = 0.0,
        double? ReferencePeak = null);

    /// <summary><c>SnrDb</c> is present only when the envelope was computed: it is read off the envelope.</summary>
    public sealed record ImpulseCurveSet(
        AnalysisCurve? Impulse,
        AnalysisCurve? Envelope,
        AnalysisCurve? Step,
        double PeakReference,
        int PeakSample,
        double? SnrDb);

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

        /// <summary>Unsmoothed (Hz, dB) points without DC and Nyquist; non-positive length or rate yields empty.</summary>
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

        /// <param name="wrap">Every index read circularly.</param>
        /// <param name="wrapPreRoll">Only indices before the record read from its end (a circular IR's pre-roll); past
        /// the end stays zero, so a window longer than the record never reads the direct sound twice.</param>
        public static Complex[] ExtractWindow(
            IImpulseMeasurement measurement,
            int start,
            int length,
            double[]? window = null,
            bool wrap = false,
            bool wrapPreRoll = false)
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
                else if (wrapPreRoll && sourceIndex < 0 && sourceIndex >= -source.Length)
                {
                    sourceIndex += source.Length;
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
