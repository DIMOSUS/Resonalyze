using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>Virtual DSP's rules read a <c>VirtualCrossoverSession</c> and the panel binds controls to it. A rule the panel
/// exposes as a static is one a test reaches only through the panel, which is how its logic piled up there.</summary>
public sealed class VirtualCrossoverPanelBoundaryTests
{
    [Fact]
    public void ThePanelExposesNoStaticRules()
    {
        List<string> exposed = typeof(VirtualCrossoverPanel)
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
