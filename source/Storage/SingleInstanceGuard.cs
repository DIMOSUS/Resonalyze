using System.Security.Cryptography;
using System.Text;

namespace Resonalyze;

/// <summary>Settings and history are written back whole, so the last instance to close wins; the updater also waits on one pid.
/// Scoped per data directory, so other Windows users and portable copies are not blocked.</summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;

    private bool released;

    private SingleInstanceGuard(Mutex mutex) => this.mutex = mutex;

    public static SingleInstanceGuard? TryAcquire(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Global\ so a second logon session of the same user (RDP, fast switching) is covered.
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
        // Released explicitly before a restart, then again by the using declaration in Main.
        if (released)
        {
            return;
        }

        released = true;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        mutex.Dispose();
    }

    private static string NameFor(string dataDirectory)
    {
        // Hashed: backslashes are kernel namespace separators, and paths can exceed the 260-char name limit.
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(dataDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar).ToUpperInvariant()));
        return "Global\\Resonalyze-" + Convert.ToHexString(hash, 0, 16);
    }
}
