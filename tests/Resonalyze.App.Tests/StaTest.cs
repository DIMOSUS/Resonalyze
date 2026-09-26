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
            Pump();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "The background work did not finish in time.");
    }

    /// <summary>
    /// <see cref="Application.DoEvents"/>, keeping the thread's WinForms context: DoEvents leaves a plain
    /// SynchronizationContext behind, so an await that follows would resume on the thread pool and race the test (or
    /// create a control's handle there, which hangs disposal). The app's message loop never loses it.
    /// </summary>
    public static void Pump()
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        Application.DoEvents();
        if (context is WindowsFormsSynchronizationContext && SynchronizationContext.Current != context)
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    /// <summary>The first open form of <typeparamref name="TForm"/>, created on the calling thread, that
    /// <paramref name="match"/> takes. The open-forms list is process-wide: tests on other STA threads open and close their
    /// own forms, of the same types too, while this one reads it. So it is copied first, again if a change interrupted the
    /// copy, and a form of another thread's test is never this test's.</summary>
    public static TForm? OpenForm<TForm>(Func<TForm, bool> match)
        where TForm : Form
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return Application.OpenForms.OfType<TForm>().ToArray()
                    .FirstOrDefault(form => !form.InvokeRequired && match(form));
            }
            catch (InvalidOperationException) when (attempt < 10)
            {
            }
        }
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
