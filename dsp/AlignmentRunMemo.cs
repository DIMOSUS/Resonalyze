using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Resonalyze.Dsp;

/// <summary>
/// Reads an alignment run repeats on identical input, kept for that run only. A run's responses are rendered once and
/// never written after, so an arrival read keys on the array itself, and lives only as long as it: a render the run
/// has let go takes its reads with it. See docs/tech/auto-alignment.md#run-memo.
/// </summary>
internal sealed class AlignmentRunMemo
{
    private static readonly AsyncLocal<AlignmentRunMemo?> Current = new();

    private readonly ConditionalWeakTable<Complex[], ConcurrentDictionary<ArrivalBand, TimeAlignmentAnalysisResult>>
        arrivals = new();
    private readonly ConcurrentDictionary<KernelKey, double[]> kernelEnvelopes = new();

    /// <summary>Opens a run's memo, which the run's parallel work sees too; inside an open one it does nothing.</summary>
    public static Scope Begin()
    {
        if (Current.Value != null)
        {
            return default;
        }

        Current.Value = new AlignmentRunMemo();
        return new Scope(owner: true);
    }

    public static TimeAlignmentAnalysisResult Arrival(
        Complex[] impulseResponse,
        int sampleRate,
        double lowFrequencyHz,
        double highFrequencyHz,
        ValidSampleRange validRange,
        Func<TimeAlignmentAnalysisResult> read) =>
        Current.Value is { } memo && impulseResponse != null
            ? memo.arrivals
                .GetValue(impulseResponse, _ => new ConcurrentDictionary<ArrivalBand, TimeAlignmentAnalysisResult>())
                .GetOrAdd(new ArrivalBand(sampleRate, lowFrequencyHz, highFrequencyHz, validRange), _ => read())
            : read();

    /// <summary>The band-pass kernel's envelope, which depends only on the window's length, rate and band.</summary>
    public static double[] KernelEnvelope(
        int length,
        int sampleRate,
        TimeAlignmentAnalysisOptions options,
        Func<double[]> build) =>
        Current.Value is { } memo
            ? memo.kernelEnvelopes.GetOrAdd(
                new KernelKey(
                    length,
                    sampleRate,
                    options.BandpassCenterHz,
                    options.BandpassPassOctaves,
                    options.BandpassFadeOctaves),
                _ => build())
            : build();

    public readonly struct Scope(bool owner) : IDisposable
    {
        public void Dispose()
        {
            if (owner)
            {
                Current.Value = null;
            }
        }
    }

    private readonly record struct ArrivalBand(int SampleRate, double LowHz, double HighHz, ValidSampleRange Range);

    private readonly record struct KernelKey(
        int Length, int SampleRate, double CenterHz, double PassOctaves, double FadeOctaves);
}
