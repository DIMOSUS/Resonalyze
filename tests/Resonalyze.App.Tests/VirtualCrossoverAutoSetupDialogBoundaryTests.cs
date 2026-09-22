using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>The crossover wizard's rules read an <c>AutoSetupWizardSession</c>, and the dialog binds the controls to them. A
/// static or a nested type the dialog exposes is a rule a test reaches only through the dialog.</summary>
public sealed class VirtualCrossoverAutoSetupDialogBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheDialogExposesNoStaticRules()
    {
        List<string> exposed = typeof(VirtualCrossoverAutoSetupDialog)
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
    public void TheDialogExposesNoNestedTypes()
    {
        List<string> exposed = typeof(VirtualCrossoverAutoSetupDialog)
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
