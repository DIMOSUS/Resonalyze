using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Per-record derivations, reused across band edits. Swapped whole so a reader never pairs a verdict with another record's samples.</summary>
internal sealed class TimeAlignmentRecordCache
{
    // A superseded read may still run on its thread when the next starts.
    private readonly object gate = new();
    private ProjectionEntry? mainProjection;
    private ProjectionEntry? compareProjection;
    private TimeAlignmentHygiene? mainHygiene;
    private TimeAlignmentHygiene? compareHygiene;

    public double[] MainSamples(Complex[] transfer) => RealSamples(ref mainProjection, transfer);

    public double[] CompareSamples(Complex[] transfer) => RealSamples(ref compareProjection, transfer);

    public TimeAlignmentHygiene MainHygiene(TimeAlignmentAnalysisSource source) => Hygiene(ref mainHygiene, source);

    public TimeAlignmentHygiene CompareHygiene(TimeAlignmentAnalysisSource source) => Hygiene(ref compareHygiene, source);

    private double[] RealSamples(ref ProjectionEntry? slot, Complex[] transfer)
    {
        lock (gate)
        {
            if (slot is { } entry && ReferenceEquals(entry.Source, transfer))
            {
                return entry.Samples;
            }

            var projected = new ProjectionEntry(
                transfer,
                Array.ConvertAll(transfer, sample => sample.Real));
            slot = projected;
            return projected.Samples;
        }
    }

    private TimeAlignmentHygiene Hygiene(ref TimeAlignmentHygiene? slot, TimeAlignmentAnalysisSource source)
    {
        double[] samples = source.TransferImpulseResponse;
        lock (gate)
        {
            if (slot is { } cached && ReferenceEquals(cached.Samples, samples))
            {
                return cached;
            }
        }

        CrosstalkHeadGate? crosstalk = TransferIrDiagnostics.DetectCrosstalkHead(
            samples, source.SampleRate);
        var entry = new TimeAlignmentHygiene(
            samples,
            crosstalk,
            crosstalk is { } head
                ? TransferIrDiagnostics.CleanCrosstalkHead(samples, source.SampleRate, head)
                : samples);
        lock (gate)
        {
            slot = entry;
        }

        return entry;
    }

    // Cached per record: a fresh array every refresh would also make every request compare unequal.
    private sealed record ProjectionEntry(Complex[] Source, double[] Samples);
}

// Band-independent, so a band edit does not pay for detection and a sample copy again.
internal sealed record TimeAlignmentHygiene(
    double[] Samples,
    CrosstalkHeadGate? Crosstalk,
    double[] Cleaned);
