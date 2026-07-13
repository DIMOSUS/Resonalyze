namespace Resonalyze.Audio;

/// <summary>
/// Exception-safe rollback for a partially-opened session. Every resource is
/// released even if an earlier release throws, and cleanup failures are
/// swallowed so they never mask the primary open/validation exception the
/// caller is about to rethrow.
/// </summary>
internal static class AudioBackendCleanup
{
    public static async ValueTask DisposeQuietlyAsync(params IAsyncDisposable?[] resources)
    {
        foreach (IAsyncDisposable? resource in resources)
        {
            if (resource == null)
            {
                continue;
            }
            try
            {
                await resource.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort teardown of a failed open: releasing the remaining
                // resources and preserving the primary exception matters more
                // than a secondary cleanup failure.
            }
        }
    }
}
