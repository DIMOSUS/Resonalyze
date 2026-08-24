using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// Runs a test body on a fresh STA thread, which is what a WinForms control is
/// entitled to expect and what xUnit does not give it: its threads are MTA.
/// </summary>
/// <remarks>
/// Constructing controls on an MTA thread mostly works, which is why the suite
/// got away with it — the trouble starts when a WINDOW HANDLE is created. A
/// control that has asked for <c>AllowDrop</c> registers itself with OLE at that
/// moment, and <c>RegisterDragDrop</c> fails outside an STA: the EQ wizard's PEQ
/// strips are drop targets, so showing one raised "DragDrop registration failed".
/// WinForms turns that into its modal unhandled-exception dialog, which a person
/// can dismiss and CI cannot — a headless run would sit on it until the job
/// times out, reporting nothing.
/// So: any test that realises a handle — Show, CreateControl, DrawToBitmap —
/// belongs in here. Tests that only construct and measure controls do not need
/// it. The mode is set to rethrow so a WinForms exception fails the test with its
/// own stack instead of raising that dialog at all.
/// </remarks>
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
