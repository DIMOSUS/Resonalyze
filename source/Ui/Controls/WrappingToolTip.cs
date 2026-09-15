using System.ComponentModel;

namespace Resonalyze;

/// <summary>ToolTip that wraps text via <see cref="ToolTipTextWrapper"/>. SetToolTip is not virtual, so it is hidden with <c>new</c>;
/// consumers declare this type, since a base-typed reference would bypass the wrap.</summary>
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
