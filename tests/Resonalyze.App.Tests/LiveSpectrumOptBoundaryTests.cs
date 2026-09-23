using System.Reflection;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>The Live Spectrum settings' rules read a <c>LiveSpectrumSettingsSession</c>, and the panel binds the controls
/// to them. A static or a nested type the panel exposes is a rule a test reaches only through the panel.</summary>
public sealed class LiveSpectrumOptBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void ThePanelExposesNoStaticRules()
    {
        List<string> exposed = typeof(LiveSpectrumOpt)
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
    public void ThePanelExposesNoNestedTypes()
    {
        List<string> exposed = typeof(LiveSpectrumOpt)
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
