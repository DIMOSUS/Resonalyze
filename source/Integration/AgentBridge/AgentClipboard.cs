using System.Runtime.InteropServices;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// The bridge's one transport, behind two delegates so the flow can be driven
/// in tests without a Windows clipboard. The clipboard is a shared resource —
/// a remote desktop or a clipboard manager may hold it for a moment — so each
/// call is retried a few times before it is reported, never thrown.
/// </summary>
internal static class AgentClipboard
{
    private const int Attempts = 5;
    private const int RetryDelayMs = 40;

    /// <summary>Reads the clipboard's text, or null when it holds none. Swappable for tests.</summary>
    public static Func<string?> ReadText { get; set; } =
        () => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    /// <summary>Replaces the clipboard's content with the text. Swappable for tests.</summary>
    public static Action<string> WriteText { get; set; } =
        text => Clipboard.SetText(text);

    public static bool TryRead(out string? text, out string? error)
    {
        text = null;
        error = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                text = ReadText();
                return true;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch (ExternalException exception)
            {
                error = "The clipboard is held by another program; try again in a moment. " +
                    $"({exception.Message})";
                return false;
            }
        }
    }

    public static bool TryWrite(string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);

        error = null;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                WriteText(text);
                return true;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch (ExternalException exception)
            {
                error = "The clipboard is held by another program; nothing was copied. " +
                    $"({exception.Message})";
                return false;
            }
        }
    }
}
