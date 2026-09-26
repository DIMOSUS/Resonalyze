using System.Windows.Forms;
using Resonalyze.Dsp;
using static Resonalyze.App.Tests.AutoSetupWizardFixtures;
using static Resonalyze.App.Tests.VirtualCrossoverAutoSetupDialogWiringTests;

namespace Resonalyze.App.Tests;

/// <summary>
/// One control changed on a shown wizard and on a session beside it: the dialog must show the readers' preview of that
/// session, and Apply must write its proposal. A case takes several seconds of fits, so the controls are split over
/// classes that run in parallel.
/// </summary>
internal static class AutoSetupControlWiring
{
    // Every case starts from this session; a fit costs over a second, so its preview and proposals are read once.
    // The first preview takes the elevation, as the dialog's does; the second is read with it taken.
    private static readonly Lazy<(AutoSetupPreview First, AutoSetupPreview Settled, CrossoverProposal[] Proposals)>
        Untouched = new(() =>
        {
            AutoSetupWizardSession session = Session(LoudSub());
            AutoSetupPreview first = Preview(session)!;
            return (first, Preview(session)!, Proposals(session));
        });

    public static void EachControl_ReachesTheProposalApplyWrites(string change) => StaTest.Run(() =>
    {
        (AutoSetupPreview first, AutoSetupPreview settled, CrossoverProposal[] untouched) = Untouched.Value;
        AutoSetupWizardSession expected = Session(LoudSub());
        expected.TakeElevation(first.ElevationCeiling, first.ElevationValue);
        // Apply is checked against a preview's fit; with the elevation taken, that must be TryFit's.
        Assert.Equal(untouched, AutoSetupWizardFit.InInitOrder(settled.Fits, expected.Rows.Count));
        Action<Wizard> changeShown = Change(change, expected);
        // The readers' fit runs beside the dialog's own; neither touches the other's session.
        Task<AutoSetupPreview?> readers = Task.Run(() => Preview(expected));

        using var wizard = new Wizard(LoudSub());
        changeShown(wizard);
        wizard.Settle();
        AutoSetupPreview preview = readers.GetAwaiter().GetResult()!;

        AssertShows(wizard, expected, preview);
        CrossoverProposal[] applied = wizard.Apply();
        Assert.Equal(AutoSetupWizardFit.InInitOrder(preview.Fits, expected.Rows.Count), applied);
        Assert.NotEqual(untouched, applied);
        Assert.Equal(expected.RequestedChainOrder(), wizard.Dialog.ChainOrder);
    });

    /// <summary>Makes the change on <paramref name="expected"/> and returns the same change made through the wizard.</summary>
    private static Action<Wizard> Change(string change, AutoSetupWizardSession expected)
    {
        AutoSetupWizardJunction middle = expected.Junctions()[1];
        AutoSetupWizardJunction top = expected.Junctions()[2];
        switch (change)
        {
            case "type":
                expected.Rows[2].Type = DriverType.Midbass;
                return wizard => wizard.TypeBox(2).SelectedItem = DriverType.Midbass;
            case "families":
                expected.SetFamily(CrossoverFilterFamily.LinkwitzRiley, false);
                expected.SetFamily(CrossoverFilterFamily.Bessel, false);
                return wizard =>
                {
                    wizard.Find<CheckBox>("checkLinkwitzRiley").Checked = false;
                    wizard.Find<CheckBox>("checkBessel").Checked = false;
                };
            case "floor":
                expected.MinCrossoverHz = 60m;
                return wizard => wizard.Find<ThemedNumericUpDown>("minCrossover").Value = 60m;
            case "ceiling":
                expected.MaxCrossoverHz = 9_000m;
                return wizard => wizard.Find<ThemedNumericUpDown>("maxCrossover").Value = 9_000m;
            case "tied slopes":
                expected.IndependentSlopes = false;
                return wizard => wizard.Find<CheckBox>("independentSlopes").Checked = false;
            case "elevation":
                decimal maximum = expected.ElevationRange.Maximum;
                decimal shown = expected.SubElevationDb;
                expected.SubElevationDb -= 4m;
                return wizard =>
                {
                    var elevation = wizard.Find<ThemedNumericUpDown>("subElevation");
                    Assert.Equal(maximum, elevation.Maximum);
                    Assert.Equal(shown, elevation.Value);
                    elevation.Value = shown - 4m;
                };
            case "junction floor":
                expected.Edit(top, expected.EditsOf(top) with { MinHz = 4_000m });
                return wizard => wizard.MinHz(2).Value = 4_000m;
            case "junction ceiling":
                expected.Edit(middle, expected.EditsOf(middle) with { MaxHz = 200m });
                return wizard => wizard.MaxHz(1).Value = 200m;
            case "steepest slope":
                expected.Edit(middle, expected.EditsOf(middle) with { MaxSlope = 18 });
                return wizard => wizard.MaxSlope(1).SelectedItem = 18;
            case "split":
                // Split moves this fit only with the slopes tied.
                expected.IndependentSlopes = false;
                expected.Edit(middle, expected.EditsOf(middle) with { Split = true });
                return wizard =>
                {
                    wizard.Find<CheckBox>("independentSlopes").Checked = false;
                    wizard.Split(1).Checked = true;
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }
}

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupRangeWiringTests
{
    [Theory]
    [InlineData("floor")]
    [InlineData("ceiling")]
    public void EachControl_ReachesTheProposalApplyWrites(string change) =>
        AutoSetupControlWiring.EachControl_ReachesTheProposalApplyWrites(change);
}

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupFilterWiringTests
{
    [Theory]
    [InlineData("families")]
    [InlineData("tied slopes")]
    public void EachControl_ReachesTheProposalApplyWrites(string change) =>
        AutoSetupControlWiring.EachControl_ReachesTheProposalApplyWrites(change);
}

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupChainWiringTests
{
    [Theory]
    [InlineData("type")]
    [InlineData("elevation")]
    public void EachControl_ReachesTheProposalApplyWrites(string change) =>
        AutoSetupControlWiring.EachControl_ReachesTheProposalApplyWrites(change);
}

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupJunctionWindowWiringTests
{
    [Theory]
    [InlineData("junction floor")]
    [InlineData("junction ceiling")]
    public void EachControl_ReachesTheProposalApplyWrites(string change) =>
        AutoSetupControlWiring.EachControl_ReachesTheProposalApplyWrites(change);
}

[Trait("Category", "Slow")]
public sealed class VirtualCrossoverAutoSetupJunctionSlopeWiringTests
{
    [Theory]
    [InlineData("steepest slope")]
    [InlineData("split")]
    public void EachControl_ReachesTheProposalApplyWrites(string change) =>
        AutoSetupControlWiring.EachControl_ReachesTheProposalApplyWrites(change);
}
