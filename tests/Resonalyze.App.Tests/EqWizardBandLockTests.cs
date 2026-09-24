using System.Text.Json.Nodes;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>A locked band is the tuner's to keep: the bank undoes the lock, a fit leaves the band and corrects around it.</summary>
public sealed class EqWizardBandLockTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void LockingABand_IsOneStep_AndUndoUnlocksIt()
    {
        var bank = new EqWizardBank(EqWizardLimits.BandGain(-15, 6), () => { });
        bank.Add(PeqBandType.Peaking);

        Assert.True(bank.SetLocked(0, true));
        Assert.False(bank.SetLocked(0, true));
        Assert.True(bank.Bands[0].Locked);

        Assert.True(bank.Undo());
        Assert.False(bank.Bands[0].Locked);
        Assert.True(bank.Redo());
        Assert.True(bank.Bands[0].Locked);
    }

    [Fact]
    public void AnEditOfALockedBand_KeepsTheLock()
    {
        var bank = new EqWizardBank(EqWizardLimits.BandGain(-15, 6), () => { });
        bank.Add(PeqBandType.Peaking);
        bank.SetLocked(0, true);

        bank.Edit(0, bank.Bands[0] with { GainDb = -40 });

        Assert.True(bank.Bands[0].Locked);
        Assert.Equal(-15, bank.Bands[0].GainDb);
    }

    [Fact]
    public void DeletingABand_IsOneStep_AndKeepsTheOthersInOrder()
    {
        var bank = new EqWizardBank(EqWizardLimits.BandGain(-15, 6), () => { });
        bank.SetCount(3);
        PeqBand first = bank.Bands[0];
        PeqBand third = bank.Bands[2];

        bank.Delete(1);

        Assert.Equal([first, third], bank.Bands);
        Assert.True(bank.Undo());
        Assert.Equal(3, bank.Bands.Count);
    }

    [Fact]
    public void AFinishedFit_CarriesTheLockedBandsAsTheyWere_InFrequencyOrder()
    {
        var locked = new PeqBand(700, 3.3, -4.5, PeqBandType.Peaking, Locked: true);
        var tuned = new EqualizationCurve([new PeqBand(2_000, 2, -3), new PeqBand(100, 1, -2)], -1);

        EqualizationCurve finished = EqWizardFit.Finish(tuned, [locked]);

        Assert.Equal([100.0, 700.0, 2_000.0], finished.Bands.Select(band => band.FrequencyHz));
        Assert.Equal(locked, finished.Bands[1]);
        Assert.Equal(-1, finished.PreampDb);
    }

    [Fact]
    public void OnAPlainCurve_TheFitCorrectsTheSourceThroughTheLockedGainBands()
    {
        var session = new EqWizardSession();
        session.Load(TextCurve());
        var bell = new PeqBand(1_000, 2, -6, PeqBandType.Peaking, Locked: true);
        var allPass = new PeqBand(500, 1, 0, PeqBandType.AllPassSecondOrder, Locked: true);
        EqWizardCurve source = session.SourceCurve!;

        IReadOnlyList<DataPoint> fitSource = EqWizardFit.FitSource(session, source, [bell, allPass]);

        Assert.Equal(source.Points.Count, fitSource.Count);
        for (int index = 0; index < source.Points.Count; index++)
        {
            double hz = source.Points[index].X;
            Assert.Equal(hz, fitSource[index].X);
            Assert.Equal(
                source.Points[index].Y + DigitalEqualizationResponse.MagnitudeDbAt(bell, hz, session.ProcessorSampleRateHz),
                fitSource[index].Y,
                9);
        }

        // An all-pass alone moves no magnitude outside a window, so nothing is rendered for it.
        Assert.Same(source.Points, EqWizardFit.FitSource(session, source, [allPass]));
    }

    [Fact]
    public void OnlyUnlockedAllPassBands_AreAskedAbout()
    {
        var session = new EqWizardSession();
        session.Bank.Replace(new EqualizationCurve(
        [
            new PeqBand(300, 1, 0, PeqBandType.AllPassSecondOrder, Locked: true),
            new PeqBand(600, 1, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(900, 2, -3, PeqBandType.Peaking, Locked: true)
        ]));

        Assert.Equal([600.0], EqWizardFit.AllPassBands(session).Select(band => band.FrequencyHz));
        Assert.Equal([300.0, 900.0], EqWizardFit.LockedBands(session).Select(band => band.FrequencyHz));
        Assert.Equal(
            "2 locked filters and an all-pass filter",
            EqWizardFit.DescribeKeptCount(session.Bank.Bands));
    }

    [Fact]
    public void TheWizardSettings_KeepTheLock()
    {
        var session = new EqWizardSession();
        session.Bank.Replace(new EqualizationCurve(
            [new PeqBand(900, 2, -3), new PeqBand(90, 1, 2, PeqBandType.LowShelf, Locked: true)]));

        var restored = new EqWizardSession();
        restored.ApplySettings(session.CaptureSettings());

        Assert.Equal(session.Bank.Bands, restored.Bank.Bands);
    }

    [Fact]
    public void AVirtualDspProject_WritesTheLockOnlyWhereItIsSet_AndReadsItBack()
    {
        string root = Path.Combine(Path.GetTempPath(), $"resonalyze-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var saved = new VirtualCrossoverProjectFile();
            saved.Pairs[0].Left.PeqBands.Add(new PeqBand(100, 2, -1));
            saved.Pairs[0].Left.PeqBands.Add(new PeqBand(200, 2, -1, PeqBandType.Peaking, Locked: true));
            saved.Save(root);

            JsonArray bands = JsonNode.Parse(File.ReadAllText(VirtualCrossoverProjectFile.GetPath(root)))!
                ["pairs"]![0]!["left"]!["peqBands"]!.AsArray();
            Assert.Null(bands[0]!["locked"]);
            Assert.True(bands[1]!["locked"]!.GetValue<bool>());

            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadOrDefault(root);
            Assert.Equal(saved.Pairs[0].Left.PeqBands, loaded.Pairs[0].Left.PeqBands);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EqWizardCurveSource TextCurve() => new()
    {
        Kind = EqWizardSourceKind.TextCurve,
        DisplayName = "curve",
        Description = "test",
        Points = Enumerable.Range(0, 200)
            .Select(index => 20 * Math.Pow(1_000, index / 199.0))
            .Select(hz => new SignalPoint(hz, 3 * Math.Sin(Math.Log(hz))))
            .ToList(),
        Scale = MagnitudeScale.Relative
    };
}
