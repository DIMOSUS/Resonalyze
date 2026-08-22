using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp
{
    /// <summary>
    /// Identifies which microphone calibration a view corrects with. The
    /// measurement layer only ever passes the id around; what it resolves to —
    /// the one 0° file, another file, or a curve estimated for an angle — is the
    /// application's bookkeeping. Null or empty means no correction.
    /// </summary>
    public static class MicrophoneCalibrationIds
    {
        /// <summary>
        /// The microphone's own 0° calibration: the one slot every other entry
        /// is derived from or compared against.
        /// </summary>
        public const string ZeroDegrees = "0deg";

        public static bool IsOff(string? calibrationId) =>
            string.IsNullOrEmpty(calibrationId);

        /// <summary>Empty selections collapse to null so persisted state has one spelling of "off".</summary>
        public static string? Normalize(string? calibrationId) =>
            string.IsNullOrWhiteSpace(calibrationId) ? null : calibrationId.Trim();
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

        // Windowing mode for the primary magnitude curve. Fixed applies the one
        // Tukey window above; FrequencyDependent (REW-style FDW) keeps that
        // window as the outer gate but shortens the analysis window past the
        // peak to MagnitudeFdwCycles periods of each frequency, so late cabin
        // reflections drop out of the treble while the bass keeps the full
        // window. Defaults to Fixed — the steady-state curve is the canonical
        // magnitude reading (and what in-car SPL targets are stated against);
        // FDW is the opt-in quasi-anechoic view. Phase below deliberately
        // defaults the other way: an ungated in-car phase trace is unreadable.
        public PhaseWindowMode MagnitudeWindowMode { get; set; } = PhaseWindowMode.Fixed;
        public int MagnitudeFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public bool Unwrap { get; set; } = true;
        /// <summary>
        /// Which microphone calibration corrects this view, by id (see
        /// <see cref="MicrophoneCalibrationIds"/>); null means uncalibrated.
        /// </summary>
        public string? CalibrationId { get; set; } = MicrophoneCalibrationIds.ZeroDegrees;

        public bool UseCalibration
        {
            get => !MicrophoneCalibrationIds.IsOff(CalibrationId);
            set => CalibrationId = value ? MicrophoneCalibrationIds.ZeroDegrees : null;
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

        // The steady-state magnitude window (milliseconds): ONE definition for every
        // magnitude curve the Virtual DSP tool and the EQ Wizard draw, long enough
        // that what is shown is the response the ear hears — tonal balance with the
        // cabin, and an EQ band's full depth even at high Q in the bass (a Q 10 bell
        // at 60 Hz rings for ~100 ms; a short gate reads a fraction of its gain).
        // Deliberately NOT taken from the user's gate: that gate exists to time
        // junctions and shapes the phase and impulse views, where cutting before the
        // first reflection is the point. Magnitude and phase answer different
        // questions and read different windows.
        //
        // In milliseconds, not samples, so the analysed TIME does not shrink with the
        // sample rate — but the carve is clamped to GatedFftLength samples
        // (ResolveGatePlacement trims the fades coherently), so the effective length
        // is min(682 ms, 32768 samples): the full 682 ms up to 48 kHz, 341 ms at
        // 96 kHz, 171 ms at 192 kHz — still resolving ~6 Hz, and dozens of times the
        // junction gate it replaces.
        public const double SteadyStateLeftMs = 2.0;
        public const double SteadyStatePlateauMs = 500.0;
        public const double SteadyStateRightMs = 180.0;

        /// <summary>
        /// The steady-state window as sample counts for the plain (non-gated)
        /// spectrum path, trimmed exactly the way the gated carve trims itself
        /// (total clamped to <see cref="DataHelper.GatedFftLength"/>, then the fades
        /// cut to fit) — so the two paths realize one window definition.
        /// </summary>
        public static (int Window, int LeftTukey, int RightTukey)
            SteadyStateWindowSamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            int left = (int)Math.Round(SteadyStateLeftMs / 1_000.0 * sampleRate);
            int plateau = (int)Math.Round(SteadyStatePlateauMs / 1_000.0 * sampleRate);
            int right = (int)Math.Round(SteadyStateRightMs / 1_000.0 * sampleRate);
            int window = Math.Clamp(
                left + plateau + right, 1, DataHelper.GatedFftLength);
            left = Math.Min(left, window);
            right = Math.Min(right, window - left);
            return (window, left, right);
        }

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

        /// <summary>
        /// A copy of these options reading a different display smoothing — for a
        /// caller that needs one curve at two widths (the Compare view's summation
        /// loss, which must divide UNSMOOTHED curves and smooth the result; see
        /// <see cref="VirtualCrossoverAnalysis.SumLossCurve"/>) without mutating the
        /// shared, UI-owned instance.
        /// </summary>
        public FrequencyResponseOptions WithSmoothing(double smoothingInverseOctaves)
        {
            var copy = (FrequencyResponseOptions)MemberwiseClone();
            copy.SmoothingInverseOctaves = smoothingInverseOctaves;
            return copy;
        }

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

    /// <summary>
    /// The unit the impulse view's time axis is drawn in. A pure display choice:
    /// the samples are the record, the axis is only how it is read.
    /// </summary>
    public enum ImpulseTimeUnit
    {
        Samples,
        Milliseconds
    }

    /// <summary>
    /// Where the impulse view puts time zero. VIEW-ONLY: unlike REW's t=0 buttons
    /// this never rewrites the measurement — Time Alignment, the Virtual DSP gate
    /// pin and every saved offset are statements about the record's own absolute
    /// timeline, and a tool that silently moved that origin would invalidate all
    /// of them. Only the axis moves.
    /// </summary>
    public enum ImpulseTimeOrigin
    {
        RecordStart,
        FirstArrival,
        Peak
    }

    /// <summary>
    /// The vertical scale of the impulse view.
    /// </summary>
    public enum ImpulseAmplitudeScale
    {
        /// <summary>Raw sample values, absolute and comparable between records.</summary>
        Linear,

        /// <summary>Percent of the reference peak (peak = 100 %).</summary>
        PercentOfPeak,

        /// <summary>Decibels relative to the reference peak (peak = 0 dB).</summary>
        Decibels
    }

    public sealed class ImpulseResponseOptions
    {
        /// <summary>
        /// How much of the tail past the peak the impulse view OPENS on, in samples.
        /// The traces themselves are always built over the whole record — this frames
        /// the default view, and every gesture and the graph-limits dialog can leave
        /// it.
        /// </summary>
        public int Length { get; set; } = 4096;

        // Curve visibility. Impulse Response and Autocorrelation modes share this
        // options type but read their own flag.
        public bool ShowImpulse { get; set; } = true;
        public bool ShowEnvelope { get; set; }
        public bool ShowStep { get; set; }
        public bool ShowAutocorrelation { get; set; } = true;

        public ImpulseTimeUnit TimeUnit { get; set; } = ImpulseTimeUnit.Milliseconds;
        public ImpulseTimeOrigin TimeOrigin { get; set; } = ImpulseTimeOrigin.RecordStart;
        public ImpulseAmplitudeScale AmplitudeScale { get; set; } =
            ImpulseAmplitudeScale.Linear;

        /// <summary>
        /// Duration of the centred moving average applied to the envelope (ETC);
        /// zero leaves it unsmoothed.
        /// </summary>
        public double EnvelopeSmoothingMs { get; set; }

        /// <summary>
        /// Flips the displayed polarity of the impulse and step traces. View-only —
        /// the record is not modified, and the envelope (a magnitude) is unaffected.
        /// </summary>
        public bool Invert { get; set; }

        /// <summary>
        /// Normalizes the step response against the impulse peak rather than against
        /// the step's own peak, so a step keeps its size relative to the impulse
        /// instead of always filling the axis.
        /// </summary>
        public bool NormalizeStepToImpulsePeak { get; set; } = true;

        /// <summary>
        /// Width of the zero-phase band the traces are read through, in octaves
        /// (1 = full octave, 1/3 = third octave); zero draws the broadband record.
        /// The band answers "when does this band arrive" — a question a full-range
        /// impulse cannot, because every band's arrival is buried in one waveform.
        /// </summary>
        public double BandFilterOctaves { get; set; }

        /// <summary>
        /// Centre of that band, in hertz. Ignored while
        /// <see cref="BandFilterOctaves"/> is zero.
        /// </summary>
        public double BandCenterHz { get; set; } = 1000.0;

        /// <summary>
        /// Whether a band filter is selected and can actually be REALIZED at this rate.
        /// The centre being under Nyquist is not enough: the band is symmetric around it
        /// in octaves, so a one-octave band at 16 kHz asks for a passband reaching
        /// 22.6 kHz, which a 44.1 kHz record cannot carry. The mask would simply stop at
        /// the end of the spectrum and the view would draw a lopsided band under the name
        /// of a symmetric one. The fade skirt beyond the passband is allowed to clip —
        /// that costs roll-off steepness, not the band's identity.
        /// </summary>
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

    /// <summary>
    /// The view-only framing the impulse traces are rendered into: where the axis
    /// zero sits (in samples from the record start, fractional so a sub-sample
    /// arrival estimate lands where it actually is) and the peak every level is
    /// normalized against. A null <c>ReferencePeak</c> makes the set use its own
    /// peak; passing the main set's peak is what lets a Compare curve be read
    /// against the same reference.
    /// </summary>
    public readonly record struct ImpulseRenderFrame(
        double OriginSamples = 0.0,
        double? ReferencePeak = null);

    /// <summary>
    /// The impulse view's traces, each null when its curve was not requested, plus
    /// the framing figures a second (Compare) set needs to be drawn against the
    /// same reference: the peak amplitude the levels were normalized against and
    /// the sample it sits at, in the record's own absolute coordinates.
    /// <c>SnrDb</c> is how far that peak stands above the record's noise floor,
    /// and is present only when the envelope was computed — the figure is read
    /// off that envelope, and computing one just to report it would cost a
    /// transform per redraw for a line of text.
    /// </summary>
    public sealed record ImpulseCurveSet(
        AnalysisCurve? Impulse,
        AnalysisCurve? Envelope,
        AnalysisCurve? Step,
        double PeakReference,
        int PeakSample,
        double? SnrDb);

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
