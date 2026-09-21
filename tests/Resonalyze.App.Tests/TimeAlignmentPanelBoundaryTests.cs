using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>Time Alignment's rules read a <c>TimeAlignmentSession</c> or a read's outcome, and the controller binds the
/// panel to them. A static or a nested type the controller exposes is a rule a test reaches only through the controller.</summary>
public sealed class TimeAlignmentPanelBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheControllerExposesNoStaticRules()
    {
        List<string> exposed = typeof(TimeAlignmentPanelController)
            .GetMembers(Declared)
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

    [Fact]
    public void TheControllerExposesNoNestedTypes()
    {
        List<string> exposed = typeof(TimeAlignmentPanelController)
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
