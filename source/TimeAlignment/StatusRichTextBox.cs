namespace Resonalyze;

internal sealed class StatusRichTextBox : RichTextBox
{
    private const int WmSetCursor = 0x20;
    private const int WmSetRedraw = 0x0B;
    private const int EmGetFirstVisibleLine = 0xCE;
    private const int EmLineScroll = 0xB6;
    private int updateDepth;
    private string? shownLines;
    private IReadOnlyList<ToneLine>? lastLines;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? UseHandCursorAt { get; set; }

    public void BeginUpdate()
    {
        if (updateDepth++ == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void EndUpdate()
    {
        if (updateDepth == 0)
        {
            return;
        }

        updateDepth--;
        if (updateDepth == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            Invalidate();
        }
    }

    /// <summary>Replaces the text with <paramref name="lines"/>, coloured by tone. An unchanged text is left alone, and
    /// <paramref name="keepScroll"/> keeps the first visible line where it was.</summary>
    public void ShowLines(IReadOnlyList<ToneLine> lines, bool keepScroll = false)
    {
        lastLines = lines;
        if (!IsHandleCreated)
        {
            return;
        }

        // Spans with their tones, so a recolour with the same text still repaints.
        string shown = string.Join(
            "\u0001",
            lines.SelectMany(line => line.Spans.Select(span => $"{(int)span.Tone}{span.Text}").Append("\n")));
        if (shownLines == shown && TextLength > 0)
        {
            return;
        }

        shownLines = shown;
        int firstLine = keepScroll
            ? (int)SendMessage(Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero)
            : 0;
        BeginUpdate();
        try
        {
            Clear();
            for (int index = 0; index < lines.Count; index++)
            {
                foreach (ToneSpan span in lines[index].Spans)
                {
                    SelectionStart = TextLength;
                    SelectionLength = 0;
                    SelectionColor = span.Tone switch
                    {
                        TextTone.Good => UiPalette.Success,
                        TextTone.Bad => UiPalette.Error,
                        _ => ForeColor
                    };
                    AppendText(span.Text);
                }

                if (index < lines.Count - 1)
                {
                    AppendText(Environment.NewLine);
                }
            }

            SelectionStart = 0;
            SelectionLength = 0;
            SelectionColor = ForeColor;
            if (firstLine > 0)
            {
                SendMessage(Handle, EmLineScroll, IntPtr.Zero, new IntPtr(firstLine));
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    // Lines shown before the handle exists wait for it: text set on a handle-less box comes back without its colours.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (lastLines != null)
        {
            shownLines = null;
            ShowLines(lastLines);
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSetCursor)
        {
            Point point = PointToClient(Cursor.Position);
            Cursor.Current = UseHandCursorAt?.Invoke(point) == true
                ? Cursors.Hand
                : Cursors.Default;
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);
}
