using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Immutable input captured from the EQ Wizard controls before a fit leaves the UI thread.
/// </summary>
internal sealed class EqWizardAutoTuneRequest
{
    public EqWizardAutoTuneRequest(
        IEnumerable<SignalPoint> source,
        IEnumerable<SignalPoint> target,
        EqAutoTuner.Options options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        Source = Array.AsReadOnly(source.ToArray());
        Target = Array.AsReadOnly(target.ToArray());
        Options = options;
    }

    public IReadOnlyList<SignalPoint> Source { get; }
    public IReadOnlyList<SignalPoint> Target { get; }
    public EqAutoTuner.Options Options { get; }
}

/// <summary>
/// Runs EQ fits away from the UI thread and accepts only the newest result.
/// Invalidation lets a control change orphan an in-flight fit without sharing
/// mutable panel state with the worker.
/// </summary>
internal sealed class EqWizardAutoTuneOrchestrator
{
    private readonly Func<EqWizardAutoTuneRequest, EqualizationCurve> tune;
    private long revision;

    public EqWizardAutoTuneOrchestrator()
        : this(request => EqAutoTuner.Tune(
            request.Source,
            request.Target,
            request.Options))
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
