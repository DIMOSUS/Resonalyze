using System.Windows.Forms;

namespace Resonalyze.App.Tests;

public sealed class ToolTipTextWrapperTests
{
    [Fact]
    public void Wrap_LeavesShortTextAlone()
    {
        const string text = "Overlay vertical offset (dB)";

        Assert.Equal(text, ToolTipTextWrapper.Wrap(text));
    }

    [Fact]
    public void Wrap_BreaksALongSentenceIntoLinesWithinTheBudget()
    {
        string wrapped = ToolTipTextWrapper.Wrap(
            "Overlaps successive analysis frames by sliding the FFT window a " +
            "fraction of its size. Higher overlap gives faster, smoother " +
            "averaging at the cost of more CPU.");

        string[] lines = Lines(wrapped);
        Assert.True(lines.Length > 1);
        Assert.All(lines, line => Assert.True(
            line.Length <= ToolTipTextWrapper.DefaultLineLength,
            $"line too long ({line.Length}): {line}"));
    }

    [Fact]
    public void Wrap_KeepsEveryWordAndTheirOrder()
    {
        const string text =
            "Applies the selected microphone calibration file so the displayed " +
            "curve reads in absolute terms rather than relative decibels.";

        string wrapped = ToolTipTextWrapper.Wrap(text);

        Assert.Equal(
            text.Split(' '),
            wrapped.Split(["\r\n", " "], StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Wrap_PreservesTheAuthorsOwnBreaks()
    {
        string wrapped = ToolTipTextWrapper.Wrap(
            "Channel gain (dB).\r\nCompensate any difference here.");

        Assert.Equal(
            ["Channel gain (dB).", "Compensate any difference here."],
            Lines(wrapped));
    }

    [Fact]
    public void Wrap_NormalizesLoneLineFeedsToCrLf()
    {
        string wrapped = ToolTipTextWrapper.Wrap("First line.\nSecond line.");

        Assert.Equal("First line.\r\nSecond line.", wrapped);
    }

    [Fact]
    public void Wrap_IndentsTheContinuationOfABulletUnderItsText()
    {
        string wrapped = ToolTipTextWrapper.Wrap(
            "Excitation noise played during the measurement.\r\n" +
            "• Pink noise (periodic): one FFT-length period of exactly pink " +
            "noise, looped. Deterministic and leakage-free.\r\n" +
            "• White noise: equal energy per hertz.");

        string[] lines = Lines(wrapped);
        Assert.Equal("Excitation noise played during the measurement.", lines[0]);
        Assert.StartsWith("• Pink noise", lines[1], StringComparison.Ordinal);
        // The bullet's own wrapped remainder lines up with its text, and the next
        // bullet still starts at the left edge.
        Assert.StartsWith("  ", lines[2]);
        Assert.DoesNotContain(
            "•",
            lines[2][..2]);
        Assert.Equal("• White noise: equal energy per hertz.", lines[^1]);
    }

    [Fact]
    public void Wrap_IsIdempotent()
    {
        const string text =
            "Frequencies whose coherence falls below this limit are drawn dimmed " +
            "and dashed to flag where the transfer function is unreliable. Off " +
            "disables the marking.";

        string once = ToolTipTextWrapper.Wrap(text);

        Assert.Equal(once, ToolTipTextWrapper.Wrap(once));
    }

    [Fact]
    public void Wrap_HardSplitsATokenThatCannotFitOnALine()
    {
        string path = @"D:\hobby\" + new string('x', 120) + @"\measurement.wav";

        string[] lines = Lines(ToolTipTextWrapper.Wrap("Source: " + path));

        Assert.True(lines.Length > 2);
        Assert.All(lines, line => Assert.True(
            line.Length <= ToolTipTextWrapper.DefaultLineLength,
            $"line too long ({line.Length}): {line}"));
        // A break replaces the space it lands on, so compare with whitespace removed:
        // nothing inside the path may be dropped.
        Assert.Equal(
            ("Source: " + path).Replace(" ", string.Empty),
            string.Concat(lines).Replace(" ", string.Empty));
    }

    [Fact]
    public void Wrap_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, ToolTipTextWrapper.Wrap(null));
        Assert.Equal(string.Empty, ToolTipTextWrapper.Wrap(string.Empty));
    }

    [Fact]
    public void Wrap_RejectsALineBudgetTooSmallForWords()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ToolTipTextWrapper.Wrap("text", maxLineLength: 4));
    }

    // The app assigns tooltips through WrappingToolTip, including from designer code;
    // this is the interception every one of those assignments relies on.
    [Fact]
    public void WrappingToolTip_StoresTheWrappedTextForTheControl()
    {
        using var toolTip = new WrappingToolTip();
        using var control = new Label();

        toolTip.SetToolTip(
            control,
            "Sets the FFT block size. Longer sequences give finer frequency " +
            "resolution but slower visual updates.");

        string stored = Assert.IsType<string>(toolTip.GetToolTip(control));
        Assert.Contains("\r\n", stored);
        Assert.All(Lines(stored), line => Assert.True(
            line.Length <= ToolTipTextWrapper.DefaultLineLength,
            $"line too long ({line.Length}): {line}"));
    }

    private static string[] Lines(string text) =>
        text.Split("\r\n", StringSplitOptions.None);
}
