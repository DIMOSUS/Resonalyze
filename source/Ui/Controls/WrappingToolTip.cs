using System.ComponentModel;

namespace Resonalyze;

/// <summary>
/// The tooltip every window in the app uses: identical to <see cref="ToolTip"/> except
/// that assigned text is word-wrapped by <see cref="ToolTipTextWrapper"/> instead of
/// being drawn as one endless line.
/// </summary>
/// <remarks>
/// <see cref="ToolTip.SetToolTip"/> is neither virtual nor routed through an overridable
/// hook, so wrapping has to intercept the call itself — hence the hiding method below.
/// A reference typed as the base <see cref="ToolTip"/> would slip past it, so everything
/// that takes a tooltip to fill in (for example <see cref="DarkNumericUpDown.ApplyToolTip"/>)
/// declares this type rather than the base one, which lets the compiler keep that promise.
/// </remarks>
public sealed class WrappingToolTip : ToolTip
{
    public WrappingToolTip()
    {
    }

    public WrappingToolTip(IContainer container)
        : base(container)
    {
    }

    public new void SetToolTip(Control control, string? caption) =>
        base.SetToolTip(control, ToolTipTextWrapper.Wrap(caption));
}
