using System.Security.Cryptography;
using System.Text;

namespace Resonalyze;

/// <summary>
/// Refuses a second instance, because two of them share one set of files and
/// silently destroy each other's work: settings and history are read at startup
/// and written back whole, so whichever copy closes LAST wins and the other's
/// session is gone. It also breaks the silent updater, which waits on a single
/// process id.
///
/// Scoped to the application data directory rather than to the machine. A second
/// Windows user has their own <c>%LocalAppData%</c> and must not be blocked by
/// the first, while a portable copy (its own directory beside the executable) is
/// free to run alongside an installed one — different files, no conflict.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;

    private SingleInstanceGuard(Mutex mutex) => this.mutex = mutex;

    /// <summary>
    /// Returns the guard when this process is the only instance for
    /// <paramref name="dataDirectory"/>, or null when another one holds it. Keep
    /// the returned guard alive for the lifetime of the process.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Global\ so the guard also covers a second logon session of the same
        // user (fast user switching, RDP), which shares the same data directory.
        var mutex = new Mutex(initiallyOwned: true, NameFor(dataDirectory), out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owner (should not happen); nothing to release.
        }

        mutex.Dispose();
    }

    private static string NameFor(string dataDirectory)
    {
        // Hashed because the directory contains backslashes, which are the
        // namespace separator in a kernel object name, and because the path can
        // exceed the 260-character name limit.
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(dataDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar).ToUpperInvariant()));
        return "Global\\Resonalyze-" + Convert.ToHexString(hash, 0, 16);
    }
}
