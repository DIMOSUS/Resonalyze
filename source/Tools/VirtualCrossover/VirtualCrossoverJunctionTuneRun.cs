using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What only the panel can do for work run off its UI thread: show that it works, and say whether it still exists.</summary>
internal interface IVirtualCrossoverWorkHost
{
    bool IsGone { get; }

    /// <summary>A wait cursor until disposed; <paramref name="disable"/> also takes the panel's input away meanwhile.</summary>
    IDisposable Busy(bool disable);
}

/// <summary>How a junction tune run off the UI thread ended: a result, the tuner's refusal, a panel gone, or a session that
/// moved under the search (its result is dropped).</summary>
internal sealed record JunctionTuneRunOutcome(
    JunctionTuneResult? Result,
    string? Failure = null,
    bool Gone = false,
    bool SessionMoved = false);

/// <summary>One junction tune for the dialog and the AI import alike. The panel stays live, so the session is fingerprinted
/// around the search instead: disabling it would repaint every plot twice, seconds with spatial averages.</summary>
internal static class VirtualCrossoverJunctionTuneRun
{
    public static async Task<JunctionTuneRunOutcome> RunAsync(
        JunctionTunePlan plan, Func<string> fingerprint, IVirtualCrossoverWorkHost host)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(host);
        string before = fingerprint();
        JunctionTuneResult result;
        using (host.Busy(disable: false))
        {
            try
            {
                result = await Task.Run(() => CrossoverJunctionTuner.Tune(plan.Sides, plan.Options))
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return new JunctionTuneRunOutcome(null, exception.Message.TrimEnd('.'));
            }
        }

        if (host.IsGone)
        {
            return new JunctionTuneRunOutcome(null, Gone: true);
        }

        return string.Equals(before, fingerprint(), StringComparison.Ordinal)
            ? new JunctionTuneRunOutcome(result)
            : new JunctionTuneRunOutcome(null, SessionMoved: true);
    }
}

/// <summary>Runs an action once, when disposed.</summary>
internal sealed class DisposeAction(Action action) : IDisposable
{
    private Action? pending = action;

    public void Dispose()
    {
        Action? run = pending;
        pending = null;
        run?.Invoke();
    }
}
