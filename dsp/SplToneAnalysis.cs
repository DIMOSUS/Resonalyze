using System;
using System.Collections.Generic;

namespace Resonalyze.Dsp;

/// <summary>
/// The pass/fail rule for detecting an acoustic calibrator's reference tone in a
/// captured spectrum. A professional microphone calibrator emits one pure,
/// dominant tone (nominally 1 kHz at 94/104/114 dB SPL); these thresholds decide
/// whether what the microphone heard really is that tone and not room noise,
/// mains hum, or a mis-seated coupler.
/// </summary>
/// <param name="TargetFrequencyHz">The calibrator's nominal tone frequency.</param>
/// <param name="FrequencyToleranceHz">
/// How far the observed peak may sit from <paramref name="TargetFrequencyHz"/>
/// and still count as the reference tone. Generous by design: the calibrator's
/// own tolerance is a percent or two and the soundcard clock adds well under
/// that, so this is a sanity gate against locking onto an unrelated peak (mains
/// harmonics, a different tone), not a precision measurement.
/// </param>
/// <param name="MinimumProminenceDb">
/// How far the peak must stand above the spectrum's background for the tone to be
/// called "clear". A calibrator at the capsule typically towers 40 dB or more
/// over ambient; this floor rejects noise and hum while tolerating a merely
/// quiet room.
/// </param>
/// <param name="MinimumAnalysisFrequencyHz">
/// Bins below this are excluded from both the dominant-peak search and the
/// background estimate, so low-frequency rumble and HVAC (which the coupler does
/// not exclude and which carry no calibration information) cannot masquerade as
/// the loudest tone or inflate the background.
/// </param>
public readonly record struct SplToneCriteria(
    double TargetFrequencyHz,
    double FrequencyToleranceHz,
    double MinimumProminenceDb,
    double MinimumAnalysisFrequencyHz)
{
    /// <summary>The standard IEC 60942 calibrator: a 1 kHz tone.</summary>
    public static SplToneCriteria Default => new(
        TargetFrequencyHz: 1_000.0,
        FrequencyToleranceHz: 40.0,
        MinimumProminenceDb: 20.0,
        MinimumAnalysisFrequencyHz: 100.0);
}

/// <summary>
/// What <see cref="SplToneAnalysis.Analyze"/> found: the dominant peak's
/// frequency and level, how far it stood above the background, and whether it
/// passes the criteria as the calibrator's reference tone.
/// </summary>
/// <param name="PeakFrequencyHz">Frequency of the loudest in-range bin.</param>
/// <param name="LevelDbFs">
/// The peak's level in dBFS. Because the spectrum is tone-calibrated (a
/// full-scale sine reads amplitude 1.0), this is the microphone's digital level
/// at the calibrator frequency — the raw number the SPL offset is anchored to.
/// </param>
/// <param name="ProminenceDb">The peak's level minus the background level.</param>
/// <param name="WithinFrequencyTolerance">
/// Whether the peak sits within the frequency tolerance of the target.
/// </param>
/// <param name="HasClearPeak">
/// The overall verdict: a peak that is both on-frequency and prominent enough.
/// This alone does not certify the calibration — the caller must also confirm the
/// capture did not clip and the level was steady (see the listener).
/// </param>
public readonly record struct SplToneReading(
    double PeakFrequencyHz,
    double LevelDbFs,
    double ProminenceDb,
    bool WithinFrequencyTolerance,
    bool HasClearPeak);

/// <summary>
/// Locates an acoustic calibrator's reference tone in an averaged, tone-calibrated
/// power spectrum. Pure and backend-neutral: it consumes the spectrum produced by
/// <see cref="SpectrumAnalysis.ComputePowerSpectrum"/> (evaluated with a
/// <see cref="WindowType.FlatTop"/> window, whose amplitude flatness keeps the
/// peak bin within hundredths of a dB of the true tone level wherever the tone
/// falls between bins) and reports the peak, its prominence and the verdict.
/// </summary>
public static class SplToneAnalysis
{
    // The floor a bin's power is clamped to before the log so an empty or silent
    // spectrum yields a very negative level instead of -infinity or NaN. -400 dBFS
    // is far below any real capture yet stays finite.
    private const double PowerFloor = 1e-40;

    /// <summary>
    /// Analyses <paramref name="averagedPowerSpectrum"/> — the single-sided,
    /// coherent-gain-normalised power spectrum (amplit² per bin) from
    /// <see cref="SpectrumAnalysis.ComputePowerSpectrum"/>, averaged over the
    /// capture's frames — for the calibrator tone described by
    /// <paramref name="criteria"/>. <paramref name="binWidthHz"/> is the spectrum's
    /// frequency resolution (sample rate divided by the pre-FFT frame length).
    /// </summary>
    public static SplToneReading Analyze(
        IReadOnlyList<double> averagedPowerSpectrum,
        double binWidthHz,
        SplToneCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(averagedPowerSpectrum);
        if (!double.IsFinite(binWidthHz) || binWidthHz <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(binWidthHz));
        }
        if (!double.IsFinite(criteria.TargetFrequencyHz) || criteria.TargetFrequencyHz <= 0.0 ||
            !double.IsFinite(criteria.FrequencyToleranceHz) || criteria.FrequencyToleranceHz < 0.0 ||
            !double.IsFinite(criteria.MinimumProminenceDb) ||
            !double.IsFinite(criteria.MinimumAnalysisFrequencyHz) ||
            criteria.MinimumAnalysisFrequencyHz < 0.0)
        {
            throw new ArgumentException("The tone criteria contain an invalid value.", nameof(criteria));
        }

        int count = averagedPowerSpectrum.Count;
        // Skip DC and everything below the analysis floor: rumble carries no
        // calibration information and must not win the dominant-peak search.
        int firstBin = Math.Max(1, (int)Math.Ceiling(criteria.MinimumAnalysisFrequencyHz / binWidthHz));
        if (count < 2 || firstBin >= count)
        {
            return new SplToneReading(0.0, LevelToDecibels(0.0), 0.0, false, false);
        }

        int peakBin = firstBin;
        double peakPower = averagedPowerSpectrum[firstBin];
        for (int bin = firstBin + 1; bin < count; bin++)
        {
            double power = averagedPowerSpectrum[bin];
            if (power > peakPower)
            {
                peakPower = power;
                peakBin = bin;
            }
        }

        double peakFrequencyHz = peakBin * binWidthHz;
        double levelDbFs = LevelToDecibels(peakPower);
        bool withinTolerance =
            Math.Abs(peakFrequencyHz - criteria.TargetFrequencyHz) <= criteria.FrequencyToleranceHz;

        double backgroundDb = EstimateBackgroundDb(
            averagedPowerSpectrum, firstBin, peakBin, binWidthHz);
        double prominenceDb = double.IsFinite(backgroundDb)
            ? levelDbFs - backgroundDb
            : 0.0;

        bool clearPeak =
            withinTolerance &&
            peakPower > 0.0 &&
            double.IsFinite(backgroundDb) &&
            prominenceDb >= criteria.MinimumProminenceDb;

        return new SplToneReading(
            peakFrequencyHz,
            levelDbFs,
            prominenceDb,
            withinTolerance,
            clearPeak);
    }

    // The background level is the MEDIAN of the analysed bins, excluding a guard
    // band around the peak so the tone's own main lobe (the flat-top lobe is
    // several bins wide) does not lift the background it is measured against. The
    // median — not the mean — keeps a second incidental tone or a spike from
    // dragging the background up and masking a genuinely dominant calibrator tone.
    private static double EstimateBackgroundDb(
        IReadOnlyList<double> spectrum,
        int firstBin,
        int peakBin,
        double binWidthHz)
    {
        // Wide enough to clear the flat-top main lobe and the calibrator's own
        // frequency slop; 50 Hz is a handful of bins at measurement FFT lengths.
        int guardBins = Math.Max(8, (int)Math.Ceiling(50.0 / binWidthHz));
        int guardLow = peakBin - guardBins;
        int guardHigh = peakBin + guardBins;

        var background = new List<double>(spectrum.Count);
        for (int bin = firstBin; bin < spectrum.Count; bin++)
        {
            if (bin >= guardLow && bin <= guardHigh)
            {
                continue;
            }

            background.Add(spectrum[bin]);
        }

        if (background.Count == 0)
        {
            return double.NaN;
        }

        background.Sort();
        int middle = background.Count / 2;
        double medianPower = background.Count % 2 == 1
            ? background[middle]
            : 0.5 * (background[middle - 1] + background[middle]);
        return LevelToDecibels(medianPower);
    }

    // Power (amplitude²) to dBFS. The spectrum is amplitude-calibrated, so
    // 10·log10(power) equals 20·log10(amplitude) — the microphone's dBFS level.
    private static double LevelToDecibels(double power) =>
        10.0 * Math.Log10(Math.Max(power, PowerFloor));
}
