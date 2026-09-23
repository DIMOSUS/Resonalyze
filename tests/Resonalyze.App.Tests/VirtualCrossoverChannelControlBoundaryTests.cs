using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>A channel block's read-outs come from the <c>VirtualCrossoverChannel*Readout</c> readers and the block binds them. A
/// static or a nested type the block exposes is a rule a test reaches only through the block.</summary>
public sealed class VirtualCrossoverChannelControlBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheBlockExposesNoStaticRules()
    {
        List<string> exposed = typeof(VirtualCrossoverChannelControl)
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
    public void TheBlockExposesNoNestedTypes()
    {
        List<string> exposed = typeof(VirtualCrossoverChannelControl)
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
