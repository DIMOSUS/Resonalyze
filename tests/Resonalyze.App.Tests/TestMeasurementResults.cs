using System.Numerics;

namespace Resonalyze.App.Tests;

/// <summary>Stored results for tests, read the way a file restores one: bands left out come from the sweep they describe.</summary>
internal static class TestMeasurementResults
{
    public static MeasurementResult Restored(
        double lowFrequencyHz,
        double highFrequencyHz,
        int sampleRate,
        int bits,
        double sweepDurationSeconds,
        PlaybackChannel playChannel,
        Complex[] sweepDeconvolutionImpulseResponse,
        int sweepDeconvolutionPeakIndex,
        SweepMeasurementMode measurementMode = SweepMeasurementMode.SweepDeconvolution,
        Complex[]? transferImpulseResponse = null,
        int? transferPeakIndex = null,
        double[]? transferCoherence = null,
        int averageRunCount = 1,
        int acceptedAverageRunCount = 1,
        double achievedLowFrequencyHz = 0.0,
        double achievedHighFrequencyHz = 0.0,
        TimingReference timingReference = TimingReference.SynchronizedLoopback,
        double measuredLowFrequencyHz = 0.0,
        double measuredHighFrequencyHz = 0.0,
        DateTimeOffset? measuredAtUtc = null)
    {
        if (!(achievedLowFrequencyHz > 0 && achievedHighFrequencyHz > achievedLowFrequencyHz))
        {
            using var sweep = new ExponentialSineSweep();
            sweep.FillData(lowFrequencyHz, highFrequencyHz, sweepDurationSeconds, bits, sampleRate);
            achievedLowFrequencyHz = sweep.LowFrequencyHz;
            achievedHighFrequencyHz = sweep.HighFrequencyHz;
            measuredLowFrequencyHz = sweep.Spec.FullAmplitudeLowFrequencyHz;
            measuredHighFrequencyHz = sweep.Spec.FullAmplitudeHighFrequencyHz;
        }

        (double measuredLowHz, double measuredHighHz) = MeasurementResult.ResolveMeasuredBand(
            measuredLowFrequencyHz,
            measuredHighFrequencyHz,
            achievedLowFrequencyHz,
            achievedHighFrequencyHz);
        int runs = Math.Clamp(averageRunCount, 1, 64);
        return new MeasurementResult
        {
            SampleRate = sampleRate,
            Bits = bits,
            PlaybackChannel = playChannel,
            LowFrequencyHz = lowFrequencyHz,
            HighFrequencyHz = highFrequencyHz,
            AchievedLowFrequencyHz = achievedLowFrequencyHz,
            AchievedHighFrequencyHz = achievedHighFrequencyHz,
            MeasuredLowFrequencyHz = measuredLowHz,
            MeasuredHighFrequencyHz = measuredHighHz,
            SweepDurationSeconds = sweepDurationSeconds,
            MeasuredAtUtc = measuredAtUtc ?? DateTimeOffset.UtcNow,
            MeasurementMode = measurementMode,
            TimingReference = timingReference,
            SweepDeconvolution = new MeasurementImpulseResponse(
                sweepDeconvolutionImpulseResponse.ToArray(),
                sweepDeconvolutionPeakIndex),
            Transfer = transferImpulseResponse == null
                ? null
                : new MeasurementImpulseResponse(transferImpulseResponse.ToArray(), transferPeakIndex ?? -1),
            TransferCoherence = transferCoherence?.ToArray(),
            AverageRunCount = runs,
            AcceptedAverageRunCount = Math.Clamp(acceptedAverageRunCount, 1, runs)
        }.Validated();
    }

    /// <summary>A document holding <paramref name="result"/>, as the analyzer holds an open measurement.</summary>
    public static AnalyzerDocument Open(MeasurementResult result, string? sourceName = null)
    {
        var document = new AnalyzerDocument();
        document.Install(result, sourceName);
        return document;
    }
}
