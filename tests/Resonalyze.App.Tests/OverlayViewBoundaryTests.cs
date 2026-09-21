using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>The overlay rules read an <c>OverlaySession</c> and the views bind controls to it; a static rule on a view is
/// one a test reaches only through the controls.</summary>
public sealed class OverlayViewBoundaryTests
{
    [Theory]
    [InlineData(typeof(OverlayPanel))]
    [InlineData(typeof(OverlaySlotView))]
    public void TheViewsExposeNoStaticRules(Type view)
    {
        List<string> exposed = view
            .GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            // Local functions compile to non-private statics named <Method>g__Name.
            .Where(member => !member.Name.Contains('<', StringComparison.Ordinal))
            .Where(member => member switch
            {
                MethodBase method => !method.IsPrivate,
                FieldInfo field => !field.IsPrivate,
                PropertyInfo property => property.GetAccessors(nonPublic: true).Any(accessor => !accessor.IsPrivate),
                _ => false
            })
            .Select(member => member.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
