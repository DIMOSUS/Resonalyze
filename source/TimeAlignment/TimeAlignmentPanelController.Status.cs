namespace Resonalyze;

internal sealed partial class TimeAlignmentPanelController
{
    private void ShowReport(IReadOnlyList<TimeAlignmentReportSegment> segments, bool fromTop)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            foreach (TimeAlignmentReportSegment segment in segments)
            {
                AppendStatusText(segment.Text, segment.Color, segment.Table ? resultTableFont : null);
            }

            if (fromTop)
            {
                statusTextBox.SelectionStart = 0;
                statusTextBox.SelectionLength = 0;
            }
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void AppendStatusText(string text, Color color, Font? font = null)
    {
        statusTextBox.SelectionStart = statusTextBox.TextLength;
        statusTextBox.SelectionLength = 0;
        statusTextBox.SelectionColor = color;
        statusTextBox.SelectionFont = font ?? statusTextBox.Font;
        statusTextBox.AppendText(text);
        statusTextBox.SelectionFont = statusTextBox.Font;
        statusTextBox.SelectionColor = statusTextBox.ForeColor;
    }

    private void StatusTextBoxMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left ||
            !TryGetCopyableStatusLine(args.Location, out string value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process may hold the clipboard (RDP, clipboard managers); a lost copy must not crash.
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool TryGetCopyableStatusLine(Point location, out string value)
    {
        value = string.Empty;
        int index = statusTextBox.GetCharIndexFromPosition(location);
        int line = statusTextBox.GetLineFromCharIndex(index);
        if (line >= statusTextBox.Lines.Length)
        {
            return false;
        }

        int column = Math.Max(0, index - statusTextBox.GetFirstCharIndexFromLine(line));
        value = DelayTableText.CopyableValue(statusTextBox.Lines[line], column);
        return !string.IsNullOrWhiteSpace(value);
    }
}
