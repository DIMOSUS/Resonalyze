using System.Reflection;

namespace Resonalyze.App.Tests;

/// <summary>Tune junction's question, the processor choice, a channel's goal, the phase gate and Auto delay read their rules
/// from their own types, and the dialogs bind the controls to them. A static or a nested type a dialog exposes is a rule a
/// test reaches only through the dialog.</summary>
public sealed class VirtualCrossoverDialogBoundaryTests
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static TheoryData<Type> Dialogs =>
    [
        typeof(VirtualCrossoverJunctionTuneDialog),
        typeof(DspProcessorDialog),
        typeof(VirtualCrossoverAcousticGoalDialog),
        typeof(VirtualCrossoverGateDialog),
        typeof(VirtualCrossoverAutoDelayDialog)
    ];

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void TheDialogExposesNoStaticRules(Type dialog)
    {
        List<string> exposed = dialog
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
    [MemberData(nameof(Dialogs))]
    public void TheDialogExposesNoNestedTypes(Type dialog)
    {
        List<string> exposed = dialog
            .GetNestedTypes(Declared)
            .Where(type => !type.IsNestedPrivate && !type.Name.Contains('<', StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(exposed);
    }
}
