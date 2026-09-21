using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>One Time Alignment read: record hygiene, the band, both analyses and the arrival probes.</summary>
internal static class TimeAlignmentRead
{
    // Works from the request and the thread-safe record cache alone: safe on a worker thread.
    public static TimeAlignmentOutcome Run(TimeAlignmentRequest request, TimeAlignmentRecordCache records)
    {
        try
        {
            TimeAlignmentAnalysisSource mainSource = request.MainSource;
            // Crosstalk detection on the RAW record, analysis on the CLEANED one (engine order): a click in band could otherwise verify an arrival that times the click. Bypass mode keeps raw and flags.
            TimeAlignmentHygiene mainHygiene = records.MainHygiene(mainSource);
            TimeAlignmentAnalysisSource mainAnalysisSource = CleanForAnalysis(
                mainSource, mainHygiene, request.BandMode);

            // Compare resolved before the band: the Auto band is shared, so the delta must not depend on which record is Main.
            TimeAlignmentAnalysisSource? compareSource = TryGetCompareSource(
                request,
                mainSource,
                records,
                out string? compareWarning,
                out CrosstalkHeadGate? compareCrosstalk);

            TimeAlignmentAnalysisOptions analysisOptions = TimeAlignmentBand.CreateAnalysisOptions(
                request,
                mainAnalysisSource,
                compareSource,
                out DominantBand? autoBand,
                out bool autoBandShared);

            TimeAlignmentAnalysisResult mainResult = TimeAlignmentAnalysis.Analyze(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainAnalysisSource.TransferCoherence);
            if (!mainResult.IsValid)
            {
                return TimeAlignmentOutcome.Failed(
                    "No signal in the analysis band.\r\n" +
                    "The transfer IR carries no energy inside the current " +
                    "band-pass window — widen or move the band, or check " +
                    "that the measurement actually captured the driver.",
                    autoBand,
                    autoBandShared);
            }

            TimeAlignmentArrivalProbe? mainProbe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainResult,
                mainAnalysisSource.TransferCoherence);
            TimeAlignmentCompareAnalysis? compareAnalysis = AnalyzeCompare(
                compareSource, analysisOptions, ref compareWarning);
            TimeAlignmentArrivalProbe? compareProbe = compareAnalysis == null
                ? null
                : TimeAlignmentAnalysis.ProbeArrivalHonesty(
                    compareAnalysis.Value.Source.TransferImpulseResponse,
                    compareAnalysis.Value.Source.SampleRate,
                    analysisOptions,
                    compareAnalysis.Value.Result,
                    compareAnalysis.Value.Source.TransferCoherence);
            return new TimeAlignmentOutcome(
                mainSource,
                autoBand,
                autoBandShared,
                mainResult,
                mainProbe,
                mainHygiene.Crosstalk,
                compareAnalysis,
                compareProbe,
                compareCrosstalk,
                compareWarning,
                Message: null);
        }
        catch (Exception exception)
        {
            return TimeAlignmentOutcome.Failed(exception.Message);
        }
    }

    private static TimeAlignmentAnalysisSource CleanForAnalysis(
        TimeAlignmentAnalysisSource source,
        TimeAlignmentHygiene hygiene,
        TimeAlignmentBandMode bandMode) =>
        bandMode != TimeAlignmentBandMode.FullBand && hygiene.Crosstalk != null
            ? source with { TransferImpulseResponse = hygiene.Cleaned }
            : source;

    // Same hygiene as Main; no analysis yet, because the band is agreed between both records first.
    private static TimeAlignmentAnalysisSource? TryGetCompareSource(
        TimeAlignmentRequest request,
        TimeAlignmentAnalysisSource mainSource,
        TimeAlignmentRecordCache records,
        out string? warning,
        out CrosstalkHeadGate? crosstalk)
    {
        warning = null;
        crosstalk = null;
        TimeAlignmentCompareMeasurement? compare = request.Compare;
        if (compare == null)
        {
            return null;
        }

        TimeAlignmentCompareMeasurement compareValue = compare.Value;
        MeasurementResult result = compareValue.Result;
        if (result.SampleRate != mainSource.SampleRate)
        {
            warning =
                $"Sample rate mismatch: Main is {mainSource.SampleRate} Hz, " +
                $"Compare is {result.SampleRate} Hz.";
            return null;
        }

        if (!result.HasTransfer)
        {
            warning = "Compare impulse response has no transfer IR.";
            return null;
        }

        try
        {
            TimeAlignmentAnalysisSource compareSource =
                TimeAlignmentSources.CreateCompare(compareValue, records);
            TimeAlignmentHygiene hygiene = records.CompareHygiene(compareSource);
            crosstalk = hygiene.Crosstalk;
            return CleanForAnalysis(compareSource, hygiene, request.BandMode);
        }
        catch (Exception exception)
        {
            warning = exception.Message;
            return null;
        }
    }

    private static TimeAlignmentCompareAnalysis? AnalyzeCompare(
        TimeAlignmentAnalysisSource? compareSource,
        TimeAlignmentAnalysisOptions analysisOptions,
        ref string? warning)
    {
        if (compareSource is not { } source)
        {
            return null;
        }

        try
        {
            TimeAlignmentAnalysisResult compareResult = TimeAlignmentAnalysis.Analyze(
                source.TransferImpulseResponse,
                source.SampleRate,
                analysisOptions,
                source.TransferCoherence);
            if (!compareResult.IsValid)
            {
                warning = "Compare: no signal in the analysis band.";
                return null;
            }

            return new TimeAlignmentCompareAnalysis(source, compareResult);
        }
        catch (Exception exception)
        {
            warning = exception.Message;
            return null;
        }
    }
}
