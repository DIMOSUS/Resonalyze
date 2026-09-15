using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class TuningSheetQConventionDialogTests
{
    [Theory]
    [InlineData(PeqQConvention.Rbj)]
    [InlineData(PeqQConvention.Symmetric)]
    [InlineData(PeqQConvention.Classic)]
    public void PreSelectsTheConventionItWasOpenedWith(PeqQConvention convention)
    {
        using var dialog = new TuningSheetQConventionDialog(convention);

        Assert.Equal(convention, dialog.SelectedConvention);
    }

    // Radios are set individually, not as a group, so an unmatched convention would leave none checked.
    [Fact]
    public void ChecksExactlyOneOption()
    {
        using var dialog = new TuningSheetQConventionDialog(PeqQConvention.Symmetric);

        Assert.Single(dialog.Controls.OfType<RadioButton>(), radio => radio.Checked);
    }

    [Fact]
    public void ActionButtonsRemainInsideTheClientArea()
    {
        using var dialog = new TuningSheetQConventionDialog(PeqQConvention.Rbj);
        Button export = dialog.Controls.OfType<Button>()
            .Single(button => button.Text == "Export");
        Button cancel = dialog.Controls.OfType<Button>()
            .Single(button => button.Text == "Cancel");

        Assert.True(export.Bottom <= dialog.ClientSize.Height);
        Assert.True(cancel.Bottom <= dialog.ClientSize.Height);
        Assert.True(cancel.Right <= dialog.ClientSize.Width);
    }

    [Fact]
    public void CribFollowsTheSelection()
    {
        using var dialog = new TuningSheetQConventionDialog(PeqQConvention.Rbj);
        TextBox crib = dialog.Controls.OfType<TextBox>().Single();

        Assert.Contains(PeqQConventions.DescribeDevices(PeqQConvention.Rbj), crib.Text);
        Assert.Contains(PeqQConventions.DescribeBandwidth(PeqQConvention.Rbj), crib.Text);

        dialog.Controls.OfType<RadioButton>()
            .Single(radio => radio.Text == PeqQConventions.Describe(PeqQConvention.Classic))
            .Checked = true;

        Assert.Equal(PeqQConvention.Classic, dialog.SelectedConvention);
        Assert.Contains(PeqQConventions.DescribeDevices(PeqQConvention.Classic), crib.Text);
        Assert.Contains(PeqQConventions.DescribeBandwidth(PeqQConvention.Classic), crib.Text);
    }

    [Fact]
    public void LabelsTheOptionsAsTheSheetDoes()
    {
        using var dialog = new TuningSheetQConventionDialog(PeqQConvention.Rbj);
        string[] texts = dialog.Controls.OfType<RadioButton>()
            .Select(radio => radio.Text)
            .ToArray();

        Assert.Contains(PeqQConventions.Describe(PeqQConvention.Rbj), texts);
        Assert.Contains(PeqQConventions.Describe(PeqQConvention.Symmetric), texts);
        Assert.Contains(PeqQConventions.Describe(PeqQConvention.Classic), texts);
    }
}
