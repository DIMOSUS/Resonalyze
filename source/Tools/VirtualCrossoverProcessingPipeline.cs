using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Pure background-processing boundary for Virtual DSP redraws. The panel
/// snapshots inputs and chains on the UI thread, then this component computes
/// complete processed responses without reading controls or mutable runtimes.
/// </summary>
internal static class VirtualCrossoverProcessingPipeline
{
    public static Task<IReadOnlyList<VirtualCrossoverProcessingResult>> ProcessAsync(
        IReadOnlyList<VirtualCrossoverProcessingInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<VirtualCrossoverProcessingResult>>(
                Array.Empty<VirtualCrossoverProcessingResult>());
        }

        VirtualCrossoverProcessingInput[] snapshot = inputs.ToArray();
        return Task.Run<IReadOnlyList<VirtualCrossoverProcessingResult>>(() =>
        {
            var results = new VirtualCrossoverProcessingResult[snapshot.Length];
            Parallel.For(
                0,
                snapshot.Length,
                new ParallelOptions { CancellationToken = cancellationToken },
                i =>
                {
                    VirtualCrossoverProcessingInput input = snapshot[i];
                    Complex[] impulseResponse = VirtualCrossoverAnalysis.ApplyChain(
                        input.ImpulseResponse,
                        input.Chain,
                        input.SampleRate);
                    results[i] = new VirtualCrossoverProcessingResult(
                        input.Id,
                        impulseResponse,
                        VirtualCrossoverAnalysis.FindPeakIndex(impulseResponse));
                });
            return results;
        }, cancellationToken);
    }
}

internal sealed record VirtualCrossoverProcessingInput(
    int Id,
    Complex[] ImpulseResponse,
    int SampleRate,
    DspChannelChain Chain);

internal sealed record VirtualCrossoverProcessingResult(
    int Id,
    Complex[] ImpulseResponse,
    int PeakIndex);
