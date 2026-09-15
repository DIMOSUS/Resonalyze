namespace Resonalyze;

/// <summary>Window the Sum loss is measured through; Full and Direct figures are not comparable.
/// See docs/tech/virtual-dsp-analysis.md#sum-loss-window-full-vs-direct.</summary>
public enum SumLossWindow
{
    Full,

    Direct,

    /// <summary>No loss curve; the read-out column keeps the <see cref="Full"/> read.</summary>
    Off
}

public static class SumLossWindows
{
    public static readonly IReadOnlyList<SumLossWindow> All =
        [SumLossWindow.Full, SumLossWindow.Direct, SumLossWindow.Off];

    public static string DisplayName(SumLossWindow window) => window switch
    {
        SumLossWindow.Full => "Full",
        SumLossWindow.Direct => "FDW-8",
        SumLossWindow.Off => "Disable",
        _ => window.ToString()
    };
}
