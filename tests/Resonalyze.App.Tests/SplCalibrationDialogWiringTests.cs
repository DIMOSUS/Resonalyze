using System.Windows.Forms;
using Resonalyze.Options;
using Resonalyze.Ui;
using static Resonalyze.App.Tests.CalibrationDialogFixtures;

namespace Resonalyze.App.Tests;

/// <summary>A shown SPL dialog listening to a fake calibrator through its own buttons.</summary>
public sealed class SplCalibrationDialogWiringTests
{
    private static readonly double[] Clean = Enumerable.Repeat(0.1, 8).ToArray();

    [Fact]
    [Trait("Category", "Slow")]
    public void AListenThatHearsTheToneOffersSave() => Run(() =>
    {
        var streams = new Queue<ToneStream>([new ToneStream(1_000, Clean)]);
        using SplCalibrationDialog dialog = Shown(new SplCalibrationDialog(Factory(streams), SplRequest()));
        ThemedComboBox reference = In<ThemedComboBox>(dialog, "comboBoxReference");
        Assert.Equal(["94 dB SPL", "104 dB SPL", "114 dB SPL"], reference.Items.Cast<string>());
        Assert.Equal(0, reference.SelectedIndex);
        Assert.Equal("Idle.", In<Label>(dialog, "labelStatus").Text);
        Assert.False(In<Button>(dialog, "buttonSave").Enabled);
        reference.SelectedIndex = 2;

        Click(dialog, "buttonStart");
        AssertListening(dialog);
        Wait(() => In<Button>(dialog, "buttonStart").Text == "Start calibration");

        Assert.Equal(114.0, dialog.Result!.ReferenceLevelDbSpl);
        Assert.StartsWith("Calibration successful.", In<Label>(dialog, "labelStatus").Text);
        Assert.Equal(UiPalette.Success, In<Label>(dialog, "labelStatus").ForeColor);
        Assert.True(In<Button>(dialog, "buttonSave").Enabled);
        Assert.True(In<Button>(dialog, "buttonCancel").Enabled);
        Assert.True(reference.Enabled);
        Assert.False(In<ProgressBar>(dialog, "progressBar").Visible);
    });

    [Fact]
    [Trait("Category", "Slow")]
    public void StopCancelsTheListenAndKeepsNoResult() => Run(() =>
    {
        var stream = new ToneStream(1_000, Clean);
        using SplCalibrationDialog dialog = Shown(new SplCalibrationDialog(Factory(new([stream])), SplRequest(), Existing(104)));
        Assert.Equal(1, In<ThemedComboBox>(dialog, "comboBoxReference").SelectedIndex);

        Click(dialog, "buttonStart");
        StaTest.Settle(stream.Emitted.Task);
        Wait(() => In<Label>(dialog, "labelStatus").Text.StartsWith("Listening…   input peak", StringComparison.Ordinal));
        AssertListening(dialog);
        Click(dialog, "buttonStart");
        Wait(() => In<Button>(dialog, "buttonStart").Text == "Start calibration");

        Assert.Null(dialog.Result);
        Assert.Equal("Calibration cancelled.", In<Label>(dialog, "labelStatus").Text);
        Assert.Equal(UiPalette.TextDefault, In<Label>(dialog, "labelStatus").ForeColor);
        Assert.False(In<Button>(dialog, "buttonSave").Enabled);
    });

    [Fact]
    public void AFailedListenIsShownAsAnError() => Run(() =>
    {
        var factory = new FakeAudioSessionFactory(streamingFactory: _ => throw new InvalidOperationException("Gone."));
        using SplCalibrationDialog dialog = Shown(new SplCalibrationDialog(factory, SplRequest()));

        Click(dialog, "buttonStart");
        Wait(() => In<Button>(dialog, "buttonStart").Text == "Start calibration");

        Assert.Equal("Could not open the input for calibration:\r\nGone.", In<Label>(dialog, "labelStatus").Text);
        Assert.Equal(UiPalette.Error, In<Label>(dialog, "labelStatus").ForeColor);
        Assert.False(In<Button>(dialog, "buttonSave").Enabled);
    });

    [Fact]
    public void ClosingWhileListeningWaitsForTheListenToStop() => Run(() =>
    {
        var stream = new ToneStream(1_000, Clean);
        SplCalibrationDialog dialog = Shown(new SplCalibrationDialog(Factory(new([stream])), SplRequest()));
        Click(dialog, "buttonStart");
        StaTest.Settle(stream.Emitted.Task);

        dialog.Close();

        Assert.False(dialog.IsDisposed);
        Assert.True(dialog.Visible);
        Wait(() => dialog.IsDisposed);
        Assert.Null(dialog.Result);
    });

    private static void AssertListening(SplCalibrationDialog dialog)
    {
        Assert.Equal("Stop", In<Button>(dialog, "buttonStart").Text);
        Assert.StartsWith("Listening", In<Label>(dialog, "labelStatus").Text);
        Assert.True(In<ProgressBar>(dialog, "progressBar").Visible);
        Assert.False(In<ThemedComboBox>(dialog, "comboBoxReference").Enabled);
        Assert.False(In<Button>(dialog, "buttonCancel").Enabled);
        Assert.False(In<Button>(dialog, "buttonSave").Enabled);
    }

    private static FakeAudioSessionFactory Factory(Queue<ToneStream> streams) =>
        new(streamingFactory: _ => streams.Dequeue());

    private static SplCalibration Existing(double level) => new() { ReferenceLevelDbSpl = level, MeasuredLevelDbFs = -30 };
}
