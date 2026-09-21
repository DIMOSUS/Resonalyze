using System.Reflection;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Record Settings' rules read a <c>RecordSettingsSession</c>, and the form binds the controls to them. A static or
/// a nested type the form exposes is a rule a test reaches only through the form.</summary>
public sealed class MeasurementOptionsBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheFormExposesNoStaticRules()
    {
        List<string> exposed = typeof(MeasurementOptions)
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
    public void TheFormExposesNoNestedTypes()
    {
        List<string> exposed = typeof(MeasurementOptions)
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
