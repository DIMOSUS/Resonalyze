using System;
using System.Collections.Generic;

namespace Resonalyze.Dsp;

/// <summary>Pass/fail rule for an acoustic calibrator tone. The frequency tolerance is a sanity gate against unrelated peaks (hum, another tone),
/// not precision; bins below the analysis floor are excluded from peak search and background.</summary>
public readonly record struct SplToneCriteria(
    double TargetFrequencyHz,
    double FrequencyToleranceHz,
    double MinimumProminenceDb,
    double MinimumAnalysisFrequencyHz)
{
    public static SplToneCriteria Default => new(
        TargetFrequencyHz: 1_000.0,
        FrequencyToleranceHz: 40.0,
        MinimumProminenceDb: 20.0,
        MinimumAnalysisFrequencyHz: 100.0);
}

/// <summary><see cref="HasClearPeak"/> alone does not certify calibration: the caller also checks clipping and level stability.</summary>
public readonly record struct SplToneReading(
    double PeakFrequencyHz,
    double LevelDbFs,
    double ProminenceDb,
    bool WithinFrequencyTolerance,
    bool HasClearPeak);

/// <summary>Finds the calibrator tone in an averaged, tone-calibrated flat-top power spectrum (flat-top keeps the peak bin within hundredths of a dB).</summary>
public static class SplToneAnalysis
{
    private const double PowerFloor = 1e-40;

    /// <summary><paramref name="binWidthHz"/> = sample rate / pre-FFT frame length.</summary>
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

    // Median outside a guard band around the peak: the tone's lobe and incidental spikes must not lift the background.
    private static double EstimateBackgroundDb(
        IReadOnlyList<double> spectrum,
        int firstBin,
        int peakBin,
        double binWidthHz)
    {
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

    private static double LevelToDecibels(double power) =>
        10.0 * Math.Log10(Math.Max(power, PowerFloor));
}
