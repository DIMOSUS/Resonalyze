using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class EqWizardAutoTuneRequest
{
    public EqWizardAutoTuneRequest(
        IEnumerable<SignalPoint> source,
        IEnumerable<SignalPoint> target,
        EqAutoTuner.Options options,
        IEnumerable<SignalPoint>? coherence = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        Source = Array.AsReadOnly(source.ToArray());
        Target = Array.AsReadOnly(target.ToArray());
        Options = options;
        Coherence = coherence == null ? null : Array.AsReadOnly(coherence.ToArray());
    }

    public IReadOnlyList<SignalPoint> Source { get; }
    public IReadOnlyList<SignalPoint> Target { get; }
    public EqAutoTuner.Options Options { get; }

    public IReadOnlyList<SignalPoint>? Coherence { get; }
}

/// <summary>Newest-wins fits off the UI thread; invalidation orphans an in-flight fit without shared mutable state.</summary>
internal sealed class EqWizardAutoTuneOrchestrator
{
    private readonly Func<EqWizardAutoTuneRequest, EqualizationCurve> tune;
    private long revision;

    public EqWizardAutoTuneOrchestrator()
        : this(request => EqAutoTuner.Tune(
            request.Source,
            request.Target,
            request.Options,
            request.Coherence))
    {
    }

    internal EqWizardAutoTuneOrchestrator(
        Func<EqWizardAutoTuneRequest, EqualizationCurve> tune)
    {
        this.tune = tune ?? throw new ArgumentNullException(nameof(tune));
    }

    public void Invalidate() => Interlocked.Increment(ref revision);

    public async Task<EqualizationCurve?> TuneLatestAsync(EqWizardAutoTuneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long requestRevision = Interlocked.Increment(ref revision);
        EqualizationCurve result = await Task.Run(() => tune(request)).ConfigureAwait(false);
        return requestRevision == Interlocked.Read(ref revision) ? result : null;
    }
}
