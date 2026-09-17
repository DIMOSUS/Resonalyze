using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Runs a test body on a fresh STA thread (xUnit threads are MTA). Required for anything that creates a handle:
/// RegisterDragDrop fails outside STA and WinForms' modal exception dialog would hang CI. Exceptions rethrow.
/// </summary>
internal static class StaTest
{
    /// <summary>Pumps the message loop until <paramref name="task"/> finishes. A plain wait would deadlock: the
    /// continuation needs the very thread that is waiting.</summary>
    public static void Settle(Task? task, int timeoutMilliseconds = 30_000)
    {
        if (task == null)
        {
            return;
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The background work did not finish in time.");
    }

    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                // Installed here rather than left to WinForms: AutoInstall is process-wide state that a test
                // running beside this one can turn off, and a continuation that then resumes on the thread pool
                // reaches a panel's fields and controls while this thread is reading them.
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                body();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
