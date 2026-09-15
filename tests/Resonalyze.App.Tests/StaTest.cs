using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Runs a test body on a fresh STA thread (xUnit threads are MTA). Required for anything that creates a handle:
/// RegisterDragDrop fails outside STA and WinForms' modal exception dialog would hang CI. Exceptions rethrow.
/// </summary>
internal static class StaTest
{
    public static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
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
