namespace Resonalyze;

/// <summary>A span's colour by role; <see cref="StatusRichTextBox.ShowLines"/> picks the palette colour.</summary>
internal enum TextTone
{
    Plain,
    Good,
    Bad
}

internal sealed record ToneSpan(string Text, TextTone Tone = TextTone.Plain);

internal sealed record ToneLine(IReadOnlyList<ToneSpan> Spans)
{
    public static ToneLine Of(string text) => new([new ToneSpan(text)]);

    /// <summary>One plain line per line of <paramref name="text"/>; empty text gives no lines.</summary>
    public static List<ToneLine> Plain(string text) =>
        text.Length == 0 ? [] : [.. text.Split("\r\n").Select(Of)];

    public static string TextOf(IEnumerable<ToneLine> lines) =>
        string.Join("\r\n", lines.Select(line => line.Text));

    public string Text => string.Concat(Spans.Select(span => span.Text));
}
