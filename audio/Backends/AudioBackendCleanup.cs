namespace Resonalyze.Audio;

/// <summary>Rollback for a partially opened session: releases everything and swallows cleanup errors so the primary exception survives.</summary>
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
            }
        }
    }
}
