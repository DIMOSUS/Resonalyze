using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Which records a Time Alignment read takes, what the panel says about them, and why a record cannot be read.</summary>
internal static class TimeAlignmentSources
{
    public static bool TryGetMain(
        TimeAlignmentSession session,
        out TimeAlignmentAnalysisSource source,
        out string message)
    {
        MeasurementResult? measurement = session.Main;
        // Without absolute time every delay this mode reports would be meaningless.
        if (measurement?.TimingReference == TimingReference.RecordedSweep)
        {
            source = default;
            message =
                "This measurement was imported from a recorded sweep.\r\n" +
                "Its arrival time is set by when the recorder was started, not by " +
                "the tract, so delays cannot be compared across measurements.\r\n" +
                "Time Alignment needs a sweep measured against its own loopback.";
            return false;
        }

        if (measurement != null && !measurement.TimingReference.HasAbsoluteTime())
        {
            source = default;
            message =
                "In this measurement the microphone heard the sweep before the loopback did.\r\n" +
                "Its arrival is not the tract's delay and cannot be compared across " +
                "measurements: usually the loopback was on another device or stream, or its " +
                "path adds latency the loudspeaker's does not.\r\n" +
                "Time Alignment needs the microphone and a loopback taken straight from the " +
                "output on one audio device.";
            return false;
        }

        if (measurement?.Transfer is { ImpulseResponse.Length: > 0 } transfer)
        {
            source = new TimeAlignmentAnalysisSource(
                "Main",
                session.MainFileName ?? "Transfer IR",
                measurement.SampleRate,
                measurement.Bits,
                measurement.SweepDurationSeconds,
                measurement.PlaybackChannel,
                measurement.MeasurementMode,
                session.Records.MainSamples(transfer.ImpulseResponse),
                measurement.TransferCoherence,
                measurement.Levels);
            message = string.Empty;
            return true;
        }

        if (measurement != null)
        {
            source = default;
            message =
                "This record was captured without loopback.\r\n" +
                "Time Alignment requires a transfer IR.\r\n" +
                "Run a new measurement with loopback enabled or load a file that contains transfer IR.";
            return false;
        }

        source = default;
        message =
            "No impulse response is loaded.\r\n" +
            "Run a loopback measurement or load an impulse response file with transfer IR.";
        return false;
    }

    public static string MainSummary(TimeAlignmentSession session)
    {
        MeasurementResult? measurement = session.Main;
        if (measurement?.HasTransfer == true)
        {
            string source = session.MainFileName ?? "Transfer IR";
            return $"Source: {source}, {measurement.SampleRate} Hz, {measurement.Bits} bit.";
        }

        if (measurement != null)
        {
            return
                $"Source: Sweep deconvolution IR only, {measurement.SampleRate} Hz, {measurement.Bits} bit.\r\n" +
                "Loopback was not recorded for this entry.";
        }

        return "Source: waiting for a loopback measurement or file with transfer IR.";
    }

    public static string CompareSummary(TimeAlignmentSession session)
    {
        TimeAlignmentCompareMeasurement? compare = session.Compare;
        if (compare == null)
        {
            return "Compare: -";
        }

        MeasurementResult result = compare.Value.Result;
        return $"Compare: {compare.Value.DisplayName}, {result.SampleRate} Hz, {result.Bits} bit.";
    }

    public static TimeAlignmentAnalysisSource CreateCompare(
        TimeAlignmentCompareMeasurement compare,
        TimeAlignmentRecordCache records)
    {
        MeasurementResult result = compare.Result;
        return new(
            "Compare",
            compare.DisplayName,
            result.SampleRate,
            result.Bits,
            result.SweepDurationSeconds,
            result.PlaybackChannel,
            result.MeasurementMode,
            records.CompareSamples(result.Transfer!.ImpulseResponse),
            result.TransferCoherence,
            result.Levels);
    }
}

internal readonly record struct TimeAlignmentCompareMeasurement(
    string DisplayName,
    MeasurementResult Result);

internal readonly record struct TimeAlignmentCompareAnalysis(
    TimeAlignmentAnalysisSource Source,
    TimeAlignmentAnalysisResult Result);

internal readonly record struct TimeAlignmentAnalysisSource(
    string Kind,
    string DisplayName,
    int SampleRate,
    int Bits,
    double SweepDurationSeconds,
    PlaybackChannel PlayChannel,
    SweepMeasurementMode MeasurementMode,
    double[] TransferImpulseResponse,
    // γ² half spectrum behind TransferImpulseResponse (null for <2 averages or a snapshot without it); weights the GCC-PHAT refinement.
    double[]? TransferCoherence,
    InputLevelMeterSnapshot Levels);
