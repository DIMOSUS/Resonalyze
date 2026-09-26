using System.Text.Json.Nodes;
using System.Windows.Forms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class FirConstructorTests
{
    private static readonly CrossoverEdge Lr24At80 = new(CrossoverFilterFamily.LinkwitzRiley, 80, 24);
    private static readonly CrossoverEdge Lr24At2500 = new(CrossoverFilterFamily.LinkwitzRiley, 2_500, 24);

    private static FirCrossoverDesign HighPassDesign(int rate = 48_000, int taps = 1_023) =>
        new(
            CrossoverKind.HighPass,
            Lr24At2500,
            Lr24At80,
            FirCrossoverMethod.IirMagnitude,
            FirWindow.Kaiser,
            8,
            taps,
            rate);

    private static VirtualCrossoverChannelSettings WithDesignedKernel(
        VirtualCrossoverChannelSettings settings, FirCrossoverDesign design)
    {
        settings.Fir = design.Build();
        settings.FirDesign = design;
        return settings;
    }

    [Fact]
    public void TheDesign_TravelsBesideItsKernel_AndIsAbsentWithoutOne()
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            // The session checks design slopes against the constructor's list, not the IIR row's.
            FirCrossoverDesign design = HighPassDesign() with
            {
                Kind = CrossoverKind.BandPass,
                LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_500, 96),
                Window = FirWindow.Blackman
            };
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            WithDesignedKernel(original.Pairs[0].Left, design);
            original.Pairs[1].Left.Fir = new FirFilter([0.0, 1.0]);

            original.SaveTo(path);
            JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadFrom(path);

            Assert.Equal(design, loaded.Pairs[0].Left.FirDesign);
            Assert.True(loaded.Pairs[0].Left.HasFirCrossover);
            Assert.Equal(original.Pairs[0].Left.Fir!.Taps.ToArray(), loaded.Pairs[0].Left.Fir!.Taps.ToArray());
            Assert.Equal("BandPass", (string?)json["pairs"]![0]!["left"]!["firDesign"]!["kind"]);
            Assert.Null(loaded.Pairs[1].Left.FirDesign);
            Assert.False(loaded.Pairs[1].Left.HasFirCrossover);
            Assert.Null(json["pairs"]![1]!["left"]!["firDesign"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("no kernel")]
    [InlineData("another length")]
    [InlineData("even taps")]
    public void ADesignThatDoesNotDescribeItsKernel_IsRefused(string defect)
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            VirtualCrossoverChannelSettings side = WithDesignedKernel(original.Pairs[0].Left, HighPassDesign());
            original.SaveTo(path);
            JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
            JsonNode left = json["pairs"]![0]!["left"]!;
            switch (defect)
            {
                case "no kernel":
                    left.AsObject().Remove("fir");
                    break;
                case "another length":
                    left["firDesign"]!["tapCount"] = 1_021;
                    break;
                default:
                    left["firDesign"]!["tapCount"] = 1_024;
                    break;
            }

            File.WriteAllText(path, json.ToJsonString());

            Assert.Throws<InvalidDataException>(() => VirtualCrossoverProjectFile.LoadFrom(path));
            Assert.NotNull(side.FirDesign);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AWindowedSincDesign_LoadsWhateverFamilyAndSlopeItsUnusedBoxesHeld()
    {
        // Problem() does not read a sinc's family or slope, so the session keeps whatever the greyed-out boxes held.
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "session.json");
        try
        {
            FirCrossoverDesign design = HighPassDesign() with
            {
                Method = FirCrossoverMethod.WindowedSinc,
                HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.Chebyshev, 80, 18)
            };
            Assert.Null(design.Problem());
            var original = new VirtualCrossoverProjectFile { DspProcessorFirFilters = true };
            WithDesignedKernel(original.Pairs[0].Left, design);
            original.SaveTo(path);

            Assert.Equal(design, VirtualCrossoverProjectFile.LoadFrom(path).Pairs[0].Left.FirDesign);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ADeviceWithoutTheStage_TakesTheDesignWithTheKernel()
    {
        var project = new VirtualCrossoverProjectFile { DspProcessorFirFilters = false };
        WithDesignedKernel(project.Pairs[0].Left, HighPassDesign());

        Assert.Equal(1, project.ClearUnavailableFirFilters());
        Assert.Null(project.Pairs[0].Left.Fir);
        Assert.Null(project.Pairs[0].Left.FirDesign);
    }

    [Fact]
    public void TheIirCrossover_SpeaksFirst_AndAFirCrossoverWhereItIsOff()
    {
        var settings = new VirtualCrossoverChannelSettings
        {
            CrossoverKind = CrossoverKind.LowPass,
            LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.Butterworth, 5_000, 12)
        };
        WithDesignedKernel(settings, HighPassDesign());

        Assert.Equal(CrossoverKind.LowPass, settings.EffectiveCrossover.Kind);
        Assert.Equal(5_000, settings.EffectiveLowPassHz);
        Assert.Null(settings.EffectiveHighPassHz);
        // Both stages filter here, so both narrow the band the fit works in: the FIR's own corner is the lower edge
        // even though the effective crossover speaks for the IIR.
        Assert.Equal(80, settings.FirDesignCrossover!.HighPassHz);
        Assert.Equal((80.0, 5_000.0), VirtualDspEqHandoff.PassbandFor(settings));

        settings.CrossoverKind = CrossoverKind.Off;
        Assert.Equal(CrossoverKind.HighPass, settings.EffectiveCrossover.Kind);
        Assert.Equal(80, settings.EffectiveHighPassHz);
        Assert.Equal((80.0, 20_000.0), VirtualDspEqHandoff.PassbandFor(settings));
        var lower = new VirtualCrossoverChannelSettings();
        Assert.Equal(80, VirtualCrossoverJunctions.GetPairCrossoverHz(lower, settings));
        Assert.Equal((80.0, 20_000.0), VirtualCrossoverJunctions.GetChannelBand(settings));

        DspChannelChain chain = settings.ToChain(VirtualCrossoverZone.Front);
        Assert.Equal(CrossoverKind.Off, chain.Crossover?.Kind ?? CrossoverKind.Off);
        Assert.Same(settings.Fir, chain.Fir);

        settings.FirDesign = null;
        Assert.Equal(CrossoverKind.Off, settings.EffectiveCrossover.Kind);
        Assert.Null(VirtualDspEqHandoff.PassbandFor(settings));
    }

    [Fact]
    public void AFirCrossoverRunAtAnotherRate_CutsWhereTheRateMovesIt()
    {
        // Designed at 48 kHz, run at 96 kHz: the same taps cut an octave higher until rebuilt.
        var settings = WithDesignedKernel(new VirtualCrossoverChannelSettings(), HighPassDesign());
        Assert.Equal(80, settings.EffectiveHighPassHz);

        settings.FirRunSampleRateHz = 96_000;
        Assert.Equal(160, settings.EffectiveHighPassHz);
        Assert.Equal((160.0, 20_000.0), VirtualDspEqHandoff.PassbandFor(settings));

        settings.FirRunSampleRateHz = 48_000;
        Assert.Equal(80, settings.EffectiveHighPassHz);
    }

    [Fact]
    public void TheHandoff_CarriesTheSide_ItsDesign_AndTheCornersToStartFrom()
    {
        var channel = new VirtualCrossoverChannel("B");
        channel.Pair.Right.DisplayName = "Midbass";
        channel.Pair.Right.CrossoverKind = CrossoverKind.BandPass;
        channel.Pair.Right.HighPassEdge = Lr24At80;
        channel.Pair.Right.LowPassEdge = Lr24At2500;
        FirCrossoverDesign design = HighPassDesign(rate: 96_000);
        WithDesignedKernel(channel.Pair.Right, design);

        FirConstructorHandoffRequest request = FirConstructorHandoff.Build(channel, rightSide: true, 7, 48_000);

        Assert.Equal("Channel B — Midbass, right side", request.ChannelLabel);
        Assert.Same(channel.Pair.Right.Fir, request.Kernel);
        Assert.Same(channel.Pair.Right.Fir, request.Token.Kernel);
        Assert.Equal(design, request.Design);
        Assert.Equal(CrossoverKind.BandPass, request.SeedCrossover!.Kind);
        Assert.Equal(48_000, request.ProcessorSampleRateHz);
        Assert.Equal(7, request.Token.ProjectGeneration);
    }

    [Fact]
    public void AReturn_LandsTheKernelAndItsDesign_OnTheSideItWasTakenFrom()
    {
        var channel = new VirtualCrossoverChannel("B");
        channel.Pair.Left.Fir = new FirFilter([1.0]);
        channel.Pair.Left.FirSourceName = "old.wav";
        FirConstructorReturnToken token = FirConstructorHandoff.Build(channel, false, 3, 48_000).Token;
        FirCrossoverDesign design = HighPassDesign();
        FirFilter kernel = design.Build();

        Assert.True(FirConstructorHandoff.TryApplyReturn([channel], token, kernel, design, 3, 48_000, true));

        Assert.Same(kernel, channel.Pair.Left.Fir);
        Assert.Equal(design, channel.Pair.Left.FirDesign);
        Assert.Null(channel.Pair.Left.FirSourceName);
        Assert.Null(channel.Pair.Right.Fir);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("removed")]
    [InlineData("mono")]
    [InlineData("processor rate")]
    [InlineData("design rate")]
    [InlineData("kernel replaced")]
    [InlineData("stage gone")]
    public void AReturn_IsRefused_WhenTheSideIsNoLongerTheOneItWasTakenFrom(string change)
    {
        var channel = new VirtualCrossoverChannel("B");
        FirConstructorReturnToken token = FirConstructorHandoff.Build(channel, true, 3, 48_000).Token;
        FirCrossoverDesign design = HighPassDesign();
        long generation = 3;
        int rate = 48_000;
        bool stage = true;
        List<VirtualCrossoverChannel> channels = [channel];
        switch (change)
        {
            case "generation":
                generation = 4;
                break;
            case "removed":
                channels = [new VirtualCrossoverChannel("B")];
                break;
            case "mono":
                channel.Pair.Mono = true;
                break;
            case "processor rate":
                rate = 96_000;
                break;
            case "design rate":
                design = HighPassDesign(rate: 96_000);
                break;
            case "kernel replaced":
                channel.Pair.Right.Fir = new FirFilter([1.0]);
                break;
            default:
                stage = false;
                break;
        }

        FirFilter? before = channel.Pair.Right.Fir;
        Assert.False(FirConstructorHandoff.TryApplyReturn(
            channels, token, design.Build(), design, generation, rate, stage));
        Assert.Same(before, channel.Pair.Right.Fir);
        Assert.Null(channel.Pair.Right.FirDesign);
    }

    [Fact]
    public void TheLock_CarriesTheFirStage_AsOneUnit()
    {
        var pair = new VirtualCrossoverChannelPairSettings();
        pair.Right.InvertPolarity = true;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        FirCrossoverDesign design = HighPassDesign();
        WithDesignedKernel(pair.Left, design);

        Assert.True(sideLock.Follow([pair], shownRight: false));
        Assert.Same(pair.Left.Fir, pair.Right.Fir);
        Assert.Equal(design, pair.Right.FirDesign);
        Assert.True(pair.Right.InvertPolarity);

        pair.Left.Fir = null;
        pair.Left.FirDesign = null;

        Assert.True(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);
        Assert.Null(pair.Right.FirDesign);
    }

    [Fact]
    public void TheLock_NeverCarriesAnImportedKernel_NorWritesOverOne()
    {
        // The lock keeps crossovers in step and leaves per-side corrections where they were imported.
        var pair = new VirtualCrossoverChannelPairSettings();
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        var leftCorrection = new FirFilter([0.2, 1.0, 0.2]);
        pair.Left.Fir = leftCorrection;
        pair.Left.FirSourceName = "left-correction.wav";
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);

        var rightCorrection = new FirFilter([0.3, 1.0, 0.3]);
        pair.Right.Fir = rightCorrection;
        pair.Right.FirSourceName = "right-correction.wav";
        sideLock.Remember([pair]);

        pair.Left.Fir = new FirFilter([0.1, 1.0, 0.1]);
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);

        WithDesignedKernel(pair.Left, HighPassDesign());
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);
        Assert.Null(pair.Right.FirDesign);

        pair.Left.Fir = null;
        pair.Left.FirDesign = null;
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCorrection, pair.Right.Fir);
        Assert.Equal("right-correction.wav", pair.Right.FirSourceName);
    }

    [Fact]
    public void ClearingAnImportedCorrection_LeavesTheOtherSidesCrossoverStanding()
    {
        // The Clear removed a correction, not a crossover, so the right side's crossover stays.
        var pair = new VirtualCrossoverChannelPairSettings();
        WithDesignedKernel(pair.Left, HighPassDesign());
        pair.Right.Fir = pair.Left.Fir;
        pair.Right.FirDesign = pair.Left.FirDesign;
        FirFilter rightCrossover = pair.Right.Fir!;
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.Fir = new FirFilter([0.2, 1.0, 0.2]);
        pair.Left.FirSourceName = "left-correction.wav";
        pair.Left.FirDesign = null;
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCrossover, pair.Right.Fir);

        pair.Left.Fir = null;
        pair.Left.FirSourceName = null;
        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Same(rightCrossover, pair.Right.Fir);
        Assert.NotNull(pair.Right.FirDesign);
    }

    [Fact]
    public void TheLock_DoesNotCarryAClear_OntoASideWithoutACrossover()
    {
        var pair = new VirtualCrossoverChannelPairSettings();
        pair.Left.Fir = new FirFilter([0.2, 1.0, 0.2]);
        var sideLock = new VirtualCrossoverSideLock();
        sideLock.Engage([pair]);

        pair.Left.Fir = null;

        Assert.False(sideLock.Follow([pair], shownRight: false));
        Assert.Null(pair.Right.Fir);
    }

    [Fact]
    public void TheFirButton_IsRed_ForADesignAtAnotherRate_OrBesideAnIirCrossover_Only()
    {
        using var control = new VirtualCrossoverChannelControl { FirControlShown = true };
        control.ProcessorSampleRateHz = 48_000;
        FirCrossoverDesign design = HighPassDesign();

        control.SetFir(design.Build(), null, design);
        Assert.StartsWith("HP 80 Hz: ", control.FirInfoLabel.Text);
        Assert.Null(control.FirConflict);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.Danger, control.FirButton.ForeColor);

        Assert.Contains($"{511 * 1_000.0 / 48_000:0.0} ms", control.FirInfoLabel.Text);

        control.ProcessorSampleRateHz = 96_000;
        Assert.Contains("rebuild", control.FirConflict);
        Assert.Equal(Resonalyze.Ui.UiPalette.Danger, control.FirButton.ForeColor);
        Assert.Contains($"{511 * 1_000.0 / 96_000:0.0} ms", control.FirInfoLabel.Text);

        control.ProcessorSampleRateHz = 48_000;
        control.CrossoverKindComboBox.SelectedItem = CrossoverKind.LowPass;
        Assert.Contains("IIR crossover", control.FirConflict);
        Assert.Equal(Resonalyze.Ui.UiPalette.Danger, control.FirButton.ForeColor);

        control.SetFir(new FirFilter([1.0], 48_000), "room.wav");
        Assert.Null(control.FirConflict);
        Assert.NotEqual(Resonalyze.Ui.UiPalette.Danger, control.FirButton.ForeColor);
    }

    [Fact]
    public void TheSheet_DescribesADesignedKernel_AsItsCrossover()
    {
        var settings = WithDesignedKernel(new VirtualCrossoverChannelSettings(), HighPassDesign());

        string text = VirtualCrossoverSheet.DescribeFir(settings);

        Assert.StartsWith("Linear-phase high-pass 80 Hz Linkwitz-Riley 24 dB/oct", text);
        Assert.Contains("1023 taps at 48 kHz", text);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Standalone_ThePanelDesignsFromItsControls_AndKeepsTheLengthOdd()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);

            Assert.NotNull(panel.CurrentDesign);
            Assert.Equal(4_095, panel.CurrentKernel!.Length);
            Assert.False(panel.InVirtualDspHandoff);

            Field<ThemedNumericUpDown>(panel, "numericTaps").Value = 2_000;
            Settle(panel);

            Assert.Equal(2_001, panel.CurrentDesign!.TapCount);
            Assert.Equal(2_001, panel.CurrentKernel!.Length);

            Select(Field<ThemedComboBox>(panel, "comboBoxType"), 2);
            Field<ThemedNumericUpDown>(panel, "numericHighPassHz").Value = 3_000;
            Settle(panel);
            Assert.Null(panel.CurrentKernel);
            Assert.Contains("band-pass", Field<Label>(panel, "labelProblem").Text);
        });
    }

    [Fact]
    public void ABurstOfEdits_RebuildsInTheBackground_AndOnlyTheLastLands()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);
            Button export = Field<Button>(panel, "buttonExport");
            ThemedNumericUpDown taps = Field<ThemedNumericUpDown>(panel, "numericTaps");

            // Wheel steps inside one settle: the edit returns at once and the previous kernel stays.
            taps.Value = 8_191;
            taps.Value = 16_383;
            taps.Value = 1_023;
            Assert.True(panel.RebuildPending);
            Assert.Equal(4_095, panel.CurrentKernel!.Length);
            Assert.False(export.Enabled);

            Settle(panel);

            Assert.Equal(1_023, panel.CurrentKernel!.Length);
            Assert.Equal(1_023, panel.CurrentDesign!.TapCount);
            Assert.True(export.Enabled);
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AHandoff_RebuildsADesignAtTheProcessorsRate_AndReturnsIt()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);
            var channel = new VirtualCrossoverChannel("B");
            WithDesignedKernel(channel.Pair.Left, HighPassDesign(rate: 96_000, taps: 2_047));
            FirConstructorHandoffRequest request = FirConstructorHandoff.Build(channel, false, 1, 48_000);
            FirCrossoverDesign? returned = null;
            panel.ReturnFirRequested = (_, _, design) => returned = design;

            panel.BeginVirtualDspHandoff(request);
            Settle(panel);

            Assert.True(panel.InVirtualDspHandoff);
            Assert.Equal(HighPassDesign(rate: 48_000, taps: 2_047), panel.CurrentDesign);
            Assert.False(Field<ThemedComboBox>(panel, "comboBoxSampleRate").Enabled);
            Assert.Contains("rebuilt", Field<Label>(panel, "labelSession").Text);

            Field<Button>(panel, "buttonReturnToDsp").PerformClick();
            Assert.Equal(panel.CurrentDesign, returned);

            panel.EndVirtualDspHandoff();
            Settle(panel);
            Assert.False(panel.InVirtualDspHandoff);
            Assert.True(Field<ThemedComboBox>(panel, "comboBoxSampleRate").Enabled);
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AHandoff_KeepsTheStandaloneWorkAside_AndPutsItBackWhenTheSessionEnds()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Select(Field<ThemedComboBox>(panel, "comboBoxType"), 2);
            Field<ThemedNumericUpDown>(panel, "numericHighPassHz").Value = 250;
            Field<ThemedNumericUpDown>(panel, "numericLowPassHz").Value = 3_000;
            Field<ThemedNumericUpDown>(panel, "numericTaps").Value = 2_047;
            Select(Field<ThemedComboBox>(panel, "comboBoxSampleRate"), 3);
            Settle(panel);
            FirCrossoverDesign standalone = panel.CurrentDesign!;
            Assert.Equal(96_000, standalone.SampleRateHz);

            var first = new VirtualCrossoverChannel("B");
            WithDesignedKernel(first.Pair.Left, HighPassDesign());
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(first, false, 1, 48_000));
            Settle(panel);
            var second = new VirtualCrossoverChannel("C");
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(second, true, 1, 48_000));
            Settle(panel);
            Assert.NotEqual(standalone, panel.CurrentDesign);

            panel.EndVirtualDspHandoff();
            Settle(panel);

            Assert.Equal(standalone, panel.CurrentDesign);
            Assert.Contains("Standalone", Field<Label>(panel, "labelSession").Text);

            using var bare = new FirConstructorPanel();
            Settle(bare);
            var file = new FirFilter([0.25, 0.5, 0.25], 48_000);
            // Handed off while the import is still rebuilding: the import is what comes back.
            Import(bare, file, settle: false);
            Assert.True(bare.RebuildPending);
            bare.BeginVirtualDspHandoff(FirConstructorHandoff.Build(first, false, 1, 48_000));
            Settle(bare);
            bare.EndVirtualDspHandoff();
            Settle(bare);
            Assert.Equal(file.Taps.ToArray(), bare.CurrentKernel!.Taps.ToArray());
            Assert.Null(bare.CurrentDesign);
        });
    }

    [Fact]
    public void AHandoffOfAnImportedKernel_ShowsItAsItIs_UntilAControlIsTouched()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            var channel = new VirtualCrossoverChannel("B");
            channel.Pair.Left.Fir = new FirFilter([0.25, 0.5, 0.25], 48_000);
            channel.Pair.Left.FirSourceName = "room.wav";
            panel.BeginVirtualDspHandoff(FirConstructorHandoff.Build(channel, false, 1, 48_000));
            Settle(panel);

            Assert.Same(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.Null(panel.CurrentDesign);
            Button returnButton = Field<Button>(panel, "buttonReturnToDsp");
            Assert.False(returnButton.Enabled);

            Field<ThemedNumericUpDown>(panel, "numericTaps").Value = 511;
            Settle(panel);

            Assert.NotNull(panel.CurrentDesign);
            Assert.NotSame(channel.Pair.Left.Fir, panel.CurrentKernel);
            Assert.True(returnButton.Enabled);
        });
    }

    [Fact]
    public void TheImpulsePlot_IsDecimated()
    {
        StaTest.Run(() =>
        {
            using var panel = new FirConstructorPanel();
            Settle(panel);

            // Drawn through the decimator, not 131072 GDI+ segments per repaint.
            var impulse = (OxyPlot.Series.LineSeries)Field<OxyPlot.WindowsForms.PlotView>(panel, "plotImpulse").Model!.Series[0];
            Assert.NotNull(impulse.Decimator);
        });
    }

    // Background rebuild continuations are posted to this thread's WinForms synchronization context.
    private static void Settle(FirConstructorPanel panel)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (panel.RebuildPending)
        {
            Assert.True(DateTime.UtcNow < deadline, "The constructor's rebuild never landed.");
            StaTest.Pump();
            Thread.Sleep(5);
        }
    }

    // What the Import button reads, answered through the panel's file dialog.
    private static void Import(FirConstructorPanel panel, FirFilter kernel, bool settle = true)
    {
        string root = CreateTemporaryDirectory();
        string path = Path.Combine(root, "room.txt");
        FirFilterFiles.Save(path, kernel, 48_000, null);
        panel.ShowFileDialog = dialog =>
        {
            dialog.FileName = path;
            return DialogResult.OK;
        };
        Field<Button>(panel, "buttonImport").PerformClick();
        if (settle)
        {
            Settle(panel);
        }

        Directory.Delete(root, recursive: true);
    }

    private static T Field<T>(Control owner, string name) where T : Control =>
        (T)owner.Controls.Find(name, searchAllChildren: true).Single();

    private static void Select(ThemedComboBox combo, int index) => combo.SelectedIndex = index;

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "resonalyze-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
