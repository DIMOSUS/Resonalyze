namespace Resonalyze.Options;

/// <summary>An analysis window and its two Tukey fades in samples, as the three fields show them: the fades share the
/// window, the left one first. The fades the user chose are kept, so a window that shrinks clamps them on screen and
/// one that grows back returns them.</summary>
internal sealed class TukeyFades
{
    private int chosenLeft;
    private int chosenRight;

    public int Window { get; private set; }

    public int Left { get; private set; }

    public int Right { get; private set; }

    /// <summary>What the left field takes: the window less the right fade.</summary>
    public int LeftMaximum => Math.Max(0, Window - Right);

    public int RightMaximum => Math.Max(0, Window - Left);

    /// <summary>The one rule the fields and the settings file keep: each fade within the window, the right one within
    /// what the left leaves.</summary>
    public static (int Left, int Right) Contain(int left, int right, int window)
    {
        int containedLeft = Math.Clamp(left, 0, Math.Max(0, window));
        return (containedLeft, Math.Clamp(right, 0, Math.Max(0, window - containedLeft)));
    }

    public void Load(int window, int left, int right)
    {
        (chosenLeft, chosenRight) = (left, right);
        SetWindow(window);
    }

    public void SetWindow(int window)
    {
        Window = window;
        (Left, Right) = Contain(chosenLeft, chosenRight, window);
    }

    public void SetLeft(int left)
    {
        chosenLeft = left;
        (Left, Right) = Contain(chosenLeft, chosenRight, Window);
    }

    public void SetRight(int right)
    {
        chosenRight = right;
        (Left, Right) = Contain(chosenLeft, chosenRight, Window);
    }
}
