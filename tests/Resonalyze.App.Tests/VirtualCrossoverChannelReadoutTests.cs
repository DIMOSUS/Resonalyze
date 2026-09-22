using System.Drawing;
using System.Globalization;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverChannelReadoutTests
{
    [Theory]
    [InlineData(CrossoverKind.Off, false, false)]
    [InlineData(CrossoverKind.HighPass, true, false)]
    [InlineData(CrossoverKind.LowPass, false, true)]
    [InlineData(CrossoverKind.BandPass, true, true)]
    public void OnlyTheEdgesTheRoleRuns_TakeInput_AndRippleOnlyOnAChebyshevOne(CrossoverKind kind, bool high, bool low)
    {
        VirtualCrossoverChannelAvailability chebyshev = VirtualCrossoverChannelAvailability.Of(
            kind, CrossoverFilterFamily.Chebyshev, CrossoverFilterFamily.Chebyshev);
        VirtualCrossoverChannelAvailability other = VirtualCrossoverChannelAvailability.Of(
            kind, CrossoverFilterFamily.Butterworth, null);

        Assert.Equal(new VirtualCrossoverChannelAvailability(high, low, high, low), chebyshev);
        Assert.Equal(new VirtualCrossoverChannelAvailability(high, low, false, false), other);
    }

    [Fact]
    public void TheTotal_FoldsThePreampIntoTheGain_AndIsBlankWithoutOne()
    {
        Assert.Equal(string.Empty, VirtualCrossoverChannelTotalGain.Text(-6.0, 0));
        Assert.Equal("All " + (-8.0).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture),
            VirtualCrossoverChannelTotalGain.Text(-3.5, -4.5));
        Assert.Equal("All " + 0.0.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture),
            VirtualCrossoverChannelTotalGain.Text(4.5, -4.5));
    }

    [Fact]
    public void TheDelay_ReadsAsAirAtTwentyDegrees()
    {
        double millimeters = 2.0 * Acoustics.SpeedOfSoundAt20CMetersPerSecond;
        Assert.Equal(
            $"= {millimeters:0.#} mm\r\n({millimeters / 25.4:0.#} in)\r\nin air",
            VirtualCrossoverChannelDelayReadout.Tooltip(2.0));
    }

    [Fact]
    public void WithoutAKernel_TheFirRowOffersToAddOne()
    {
        VirtualCrossoverChannelFirReadout readout =
            VirtualCrossoverChannelFirReadout.Read(null, "ignored.txt", null, 48_000, CrossoverKind.BandPass);

        Assert.Equal(("Add…", "off", "No FIR filter on this channel."), (readout.ButtonText, readout.Info, readout.InfoTip));
        Assert.Equal(UiPalette.TextDisabled, readout.InfoColor);
        Assert.Equal(UiPalette.TextPrimary, readout.ButtonColor);
        Assert.Null(VirtualCrossoverChannelFirReadout.ConflictOf(null, null, 48_000, CrossoverKind.BandPass));
    }

    [Fact]
    public void AFileKernelAtAnotherRate_IsAmber_AndASilentOne_SaysItMutes()
    {
        var kernel = new FirFilter(Taps(255, silent: false), 44_100);
        VirtualCrossoverChannelFirReadout readout =
            VirtualCrossoverChannelFirReadout.Read(kernel, "taps.txt", null, 48_000, CrossoverKind.BandPass);

        Assert.Equal("Edit…", readout.ButtonText);
        Assert.StartsWith("taps.txt: 255 taps · file 44.1 kHz ≠ 48 kHz", readout.Info.Replace(',', '.'));
        Assert.Equal(UiPalette.Warning, readout.InfoColor);
        Assert.StartsWith("taps.txt: 255 taps", readout.InfoTip);
        Assert.Contains("not the filter its designer drew", readout.InfoTip);
        Assert.Null(VirtualCrossoverChannelFirReadout.ConflictOf(kernel, null, 48_000, CrossoverKind.BandPass));

        VirtualCrossoverChannelFirReadout silent = VirtualCrossoverChannelFirReadout.Read(
            new FirFilter(Taps(64, silent: true)), null, null, 48_000, CrossoverKind.Off);
        Assert.StartsWith("FIR: 64 taps", silent.Info);
        Assert.Contains("MUTES the channel", silent.InfoTip);
        Assert.Equal(UiPalette.TextSecondary, silent.InfoColor);
    }

    [Fact]
    public void ADesignedCrossover_ConflictsWithAStaleRate_OrAnIirCrossover()
    {
        FirCrossoverDesign design = Design(96_000);
        var kernel = new FirFilter(Taps(511, silent: false));

        VirtualCrossoverChannelFirReadout clean =
            VirtualCrossoverChannelFirReadout.Read(kernel, "ignored", design, 96_000, CrossoverKind.Off);
        Assert.Null(VirtualCrossoverChannelFirReadout.ConflictOf(kernel, design, 96_000, CrossoverKind.Off));
        Assert.StartsWith(FirCrossoverDescription.Short(design) + ": 511 taps", clean.Info);
        Assert.Equal(UiPalette.TextPrimary, clean.ButtonColor);

        VirtualCrossoverChannelFirReadout stale =
            VirtualCrossoverChannelFirReadout.Read(kernel, null, design, 48_000, CrossoverKind.Off);
        string? staleConflict = VirtualCrossoverChannelFirReadout.ConflictOf(kernel, design, 48_000, CrossoverKind.Off);
        Assert.Contains("rebuild it at the processor's rate", staleConflict);
        Assert.Equal(UiPalette.Danger, stale.ButtonColor);
        Assert.Equal(UiPalette.Error, stale.InfoColor);
        Assert.Equal(staleConflict, stale.InfoTip);
        Assert.StartsWith(staleConflict + Environment.NewLine + Environment.NewLine, stale.ButtonTip);

        VirtualCrossoverChannelFirReadout twice =
            VirtualCrossoverChannelFirReadout.Read(kernel, null, design, 96_000, CrossoverKind.HighPass);
        Assert.Equal(UiPalette.Danger, twice.ButtonColor);
        Assert.Contains("IIR crossover", VirtualCrossoverChannelFirReadout.ConflictOf(kernel, design, 96_000, CrossoverKind.HighPass));
        Assert.Null(VirtualCrossoverChannelFirReadout.ConflictOf(null, design, 48_000, CrossoverKind.HighPass));
    }

    [Fact]
    public void ThePhaseAngle_IsStatedAtTheSubsLowPass_AndEveryOtherBlocksHighPass()
    {
        Assert.Equal(80, VirtualCrossoverChannelPhaseReadout.ReferenceHz(VirtualCrossoverZone.Sub, 20, 80));
        Assert.Equal(20, VirtualCrossoverChannelPhaseReadout.ReferenceHz(VirtualCrossoverZone.Front, 20, 80));
        Assert.Equal(20, VirtualCrossoverChannelPhaseReadout.ReferenceHz(VirtualCrossoverZone.Center, 20, 80));
    }

    [Fact]
    public void ThePhaseReadout_NamesTheCorner_OrTheAngleTheDeviceDeliversInstead()
    {
        Assert.Equal(
            new VirtualCrossoverChannelPhaseReadout("ref 500 Hz", UiPalette.TextDisabled),
            VirtualCrossoverChannelPhaseReadout.Read(0, 500, 96_000));

        VirtualCrossoverChannelPhaseReadout placed = VirtualCrossoverChannelPhaseReadout.Read(180, 500, 96_000);
        Assert.Equal("ref 500 Hz → AP2 500 Hz", placed.Text);
        Assert.Equal(UiPalette.TextSecondary, placed.Color);

        VirtualCrossoverChannelPhaseReadout capped =
            VirtualCrossoverChannelPhaseReadout.Read(PhaseRotationControl.StepDegrees, 5_000, 96_000);
        Assert.EndsWith("° min", capped.Text);
        Assert.Equal(UiPalette.Warning, capped.Color);
        Assert.Contains("Every smaller setting delivers the same filter.",
            VirtualCrossoverChannelPhaseReadout.Tooltip(PhaseRotationControl.StepDegrees, 5_000, 96_000));
        Assert.EndsWith("No rotation. The reference would be 12.0 kHz.",
            VirtualCrossoverChannelPhaseReadout.Tooltip(0, 12_000, 96_000).Replace(',', '.'));
    }

    [Fact]
    public void AGoal_ShowsForTheEdgesTheChannelRuns_AndIsKeptForTheOthers()
    {
        var lr24 = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);
        var bw18 = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 18);

        VirtualCrossoverChannelGoalReadout none = VirtualCrossoverChannelGoalReadout.Read(null, null, true, true);
        Assert.Equal(("—", UiPalette.TextDisabled), (none.Text, none.Color));
        Assert.Contains("Nothing stated", none.Tooltip);

        Assert.Equal("LR24/BW18", VirtualCrossoverChannelGoalReadout.Read(lr24, bw18, true, true).Text);
        Assert.Equal("LR24", VirtualCrossoverChannelGoalReadout.Read(lr24, lr24, true, true).Text);

        VirtualCrossoverChannelGoalReadout kept = VirtualCrossoverChannelGoalReadout.Read(lr24, bw18, false, true);
        Assert.Equal(("BW18", UiPalette.TextPrimary), (kept.Text, kept.Color));
        Assert.Contains("Stated, LP BW18:", kept.Tooltip);
        Assert.Contains("Kept for an edge this channel does not run: HP LR24", kept.Tooltip);
    }

    [Fact]
    public void TheAverageButton_NamesTheMethod_AndTheCapturesState()
    {
        VirtualCrossoverChannelAverageReadout attached = VirtualCrossoverChannelAverageReadout.Read(
            "front seats", 60, true, VirtualCrossoverSpatialAverageMode.MovingMic, null);
        Assert.Equal(("MMM ✓", UiPalette.Success), (attached.Text, attached.Color));
        Assert.StartsWith("Spatial average: front seats" + Environment.NewLine + "60 s integrated", attached.Tooltip);

        VirtualCrossoverChannelAverageReadout missing = VirtualCrossoverChannelAverageReadout.Read(
            "front seats", null, false, VirtualCrossoverSpatialAverageMode.MovingMic, null);
        Assert.Equal(("MMM ⚠", UiPalette.Warning), (missing.Text, missing.Color));
        Assert.StartsWith("Missing spatial average: front seats", missing.Tooltip);

        VirtualCrossoverChannelAverageReadout point = VirtualCrossoverChannelAverageReadout.Read(
            null, null, false, VirtualCrossoverSpatialAverageMode.MicArray, null);
        Assert.Equal(("Array", UiPalette.TextPrimary), (point.Text, point.Color));
        Assert.Contains("POINT measurement", point.Tooltip);

        Assert.Equal("Avg off", VirtualCrossoverChannelAverageReadout.Read(
            "front seats", null, true, VirtualCrossoverSpatialAverageMode.Off, null).Text);
    }

    [Fact]
    public void TheRawToggle_WearsTheAccentFadedIntoTheSurface()
    {
        Color faded = VirtualCrossoverColors.ChannelAccentFaded(Color.FromArgb(200, 100, 0), Color.FromArgb(0, 0, 100));

        Assert.Equal(Color.FromArgb(110, 55, 44).ToArgb(), faded.ToArgb());
    }

    private static double[] Taps(int count, bool silent)
    {
        var taps = new double[count];
        if (!silent)
        {
            taps[count / 2] = 1;
        }

        return taps;
    }

    private static FirCrossoverDesign Design(int rate) =>
        new(
            CrossoverKind.HighPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24),
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 80, 24),
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            511,
            rate);
}
