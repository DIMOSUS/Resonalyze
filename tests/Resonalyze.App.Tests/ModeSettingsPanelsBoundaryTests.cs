using System.Reflection;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>The mode settings' rules read sessions (docs/tech/mode-settings.md#code-map) and the panels bind the controls
/// to them. A static or a nested type a panel or its base exposes is a rule a test reaches only through the panel.</summary>
public sealed class ModeSettingsPanelsBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static TheoryData<Type> Panels() =>
    [
        typeof(ModeSettingsForm),
        typeof(ImpulsePreviewOptionsForm),
        typeof(GatedAnalysisOptionsForm),
        typeof(WaterfallSettingsForm),
        typeof(FROptions),
        typeof(GDOpt),
        typeof(PROpt),
        typeof(WaterfallOptions),
        typeof(BDOpt),
        typeof(IROpt),
        typeof(ACOpt)
    ];

    [Theory]
    [MemberData(nameof(Panels))]
    public void APanelExposesNoStaticRules(Type panel)
    {
        List<string> exposed = panel
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

    [Theory]
    [MemberData(nameof(Panels))]
    public void APanelExposesNoNestedTypes(Type panel)
    {
        List<string> exposed = panel
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
