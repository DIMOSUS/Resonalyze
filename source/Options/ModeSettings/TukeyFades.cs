namespace Resonalyze.Options;

/// <summary>An analysis window and its two Tukey fades in samples, as the three fields show them: the fades share the
/// window, the left one first.</summary>
internal sealed class TukeyFades
{
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

    /// <summary>A stored pair is contained in its own window, not in the one shown before it.</summary>
    public void Load(int window, int left, int right)
    {
        Window = window;
        (Left, Right) = Contain(left, right, window);
    }

    public void SetWindow(int window)
    {
        Window = window;
        (Left, Right) = Contain(Left, Right, window);
    }

    public void SetLeft(int left) => (Left, Right) = Contain(left, Right, Window);

    public void SetRight(int right) => (Left, Right) = Contain(Left, right, Window);
}
