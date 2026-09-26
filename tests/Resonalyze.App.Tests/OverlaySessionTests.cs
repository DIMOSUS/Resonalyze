using System.Drawing;
using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlaySessionTests : IDisposable
{
    private static readonly NumericFieldRange OffsetRange = new(-180m, 180m, 0);

    private readonly string root = Directory.CreateTempSubdirectory("resonalyze-overlay-session-").FullName;
    private readonly PlotModel model = new();
    private readonly OverlayPlotSources sources;
    private readonly OverlaySession session;
    private MagnitudeScale shownScale = MagnitudeScale.Relative;
    private Mode mode = Mode.FrequencyResponse;
    private int repaints;

    public OverlaySessionTests()
    {
        sources = new OverlayPlotSources(() => model, () => mode);
        sources.SetMagnitudeScaleProvider(() => shownScale);
        session = new OverlaySession(sources, OffsetRange, 0m, _ => repaints++, () => { }, root);
        session.Prepare(mode);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void ACapture_ShowsTheCurveUnderTheSlotsName_AndPersistsIt()
    {
        LineSeries primary = AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0);

        session.Capture(Slot(1), primary);

        Assert.Equal("Overlay 1: Frequency Response", Slot(1).Title);
        Assert.True(Slot(1).Checked);
        Assert.True(Slot(1).CheckEnabled);
        Assert.Equal(primary.Points, OverlaySeriesOf(1).Points);
        OverlayFile saved = OverlayFile.Load(Mode.FrequencyResponse, 1, root)!;
        Assert.Equal(OverlayKind.Captured, saved.Kind);
        Assert.Equal(primary.Points.Count, saved.Points.Length);
    }

    [Fact]
    public void ACaptureIntoAnOccupiedSlot_KeepsTheSlotsName()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.ApplyCaptured(Slot(1), "Mine", Slot(1).State.Appearance, 0);

        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));

        Assert.Equal("Mine", Slot(1).Title);
        Assert.Equal(-30.0, OverlaySeriesOf(1).Points[0].Y);
    }

    [Fact]
    public void AnOffset_MovesTheCurveAtOnce_AndReachesTheFileOnlyWhenFlushed()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));

        Assert.True(session.SetOffset(Slot(1), 6m));

        Assert.Equal(6.0, OverlaySeriesOf(1).Points[0].Y);
        Assert.Equal(0.0, OverlayFile.Load(Mode.FrequencyResponse, 1, root)!.Offset);
        session.FlushPendingSaves();
        Assert.Equal(6.0, OverlayFile.Load(Mode.FrequencyResponse, 1, root)!.Offset);
    }

    [Fact]
    public void SwitchingModes_FlushesAPendingOffset_AndReloadsTheSlot()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.SetOffset(Slot(1), -4m);

        session.Prepare(Mode.PhaseResponse);
        Assert.Equal(Mode.PhaseResponse, Slot(1).SeriesMode);
        Assert.Equal("", Slot(1).Title);
        session.Prepare(Mode.FrequencyResponse);

        Assert.Equal(-4m, Slot(1).State.Offset);
        Assert.Equal("Overlay 1: Frequency Response", Slot(1).Title);
    }

    [Fact]
    public void AnOperation_ComputesFromASlotAndALiveCurve()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));
        OverlayOperationSettings difference = OverlayOperationSettings.Default with
        {
            SourceSlotA = 1,
            SourceCurveKeyB = new CurveTag(Mode.FrequencyResponse, AnalysisCurveKind.SecondHarmonic).Key
        };

        session.ApplyOperation(Slot(3), "Difference", difference, Slot(3).State.Appearance, 0);

        Assert.Equal(OverlayKind.Operation, Slot(3).Kind);
        Assert.True(Slot(3).Checked);
        Assert.All(OverlaySeriesOf(3).Points, point => Assert.Equal(30.0, point.Y, 9));
    }

    [Fact]
    public void AnOperationOnSplAndRelativeCurves_IsUnavailable()
    {
        shownScale = MagnitudeScale.SoundPressureLevel;
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 80.0));
        shownScale = MagnitudeScale.Relative;
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));

        session.ApplyOperation(
            Slot(3),
            "Nonsense",
            OverlayOperationSettings.Default with { SourceSlotA = 1, SourceSlotB = 2 },
            Slot(3).State.Appearance,
            0);

        Assert.False(Slot(3).CheckEnabled);
        Assert.False(Slot(3).Checked);
        Assert.Null(OverlaySeriesOrNull(3));
    }

    [Fact]
    public void ClearingASourceSlot_TakesItsOperationDownWithIt()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.ApplyOperation(
            Slot(2),
            "Copy",
            OverlayOperationSettings.Default with { Operation = OverlayOperation.CurveA, SourceSlotA = 1 },
            Slot(2).State.Appearance,
            0);
        Assert.True(Slot(2).CheckEnabled);

        session.ClearSlot(Slot(1));

        Assert.Null(OverlayFile.Load(Mode.FrequencyResponse, 1, root));
        Assert.Equal("", Slot(1).Title);
        Assert.False(Slot(2).CheckEnabled);
        Assert.Null(OverlaySeriesOrNull(2));
    }

    [Fact]
    public void AClearedSlot_StaysInItsMode_SoAnOperationPutThereIsSaved()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));
        session.ClearSlot(Slot(2));

        session.ApplyOperation(
            Slot(2),
            "Copy",
            OverlayOperationSettings.Default with { Operation = OverlayOperation.CurveA, SourceSlotA = 1 },
            Slot(2).State.Appearance,
            0);

        Assert.Equal(Mode.FrequencyResponse, Slot(2).SeriesMode);
        Assert.Equal(OverlayKind.Operation, OverlayFile.Load(Mode.FrequencyResponse, 2, root)!.Kind);
        Assert.True(session.CanConfigure(Slot(2)));
    }

    [Fact]
    public void AnSplCapture_StaysCheckedButUndrawnOnTheRelativeAxis_AndReturnsWithIt()
    {
        shownScale = MagnitudeScale.SoundPressureLevel;
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 80.0));

        shownScale = MagnitudeScale.Relative;
        session.Show(mode);
        Assert.True(Slot(1).Checked);
        Assert.Null(OverlaySeriesOrNull(1));

        session.SetShown(Slot(1), false);
        session.SetShown(Slot(1), true);
        Assert.False(Slot(1).Checked);

        shownScale = MagnitudeScale.SoundPressureLevel;
        session.SetShown(Slot(1), true);
        Assert.NotNull(OverlaySeriesOrNull(1));
    }

    [Fact]
    public void ATargetOnTheCurrentMeasurement_StaysArmedWithoutIt_AndFollowsItOnceItIsThere()
    {
        var settings = new OverlayTargetSettings(
            0,
            TargetPreset.Flat,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            3,
            TargetDeviationMode.Deviation);
        session.ApplyTarget(Slot(1), "Target", settings, Slot(1).State.Appearance, 0);
        Assert.True(Slot(1).Checked);
        Assert.DoesNotContain(model.Series, series => (series.Title ?? "").EndsWith("(deviation)"));

        AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 5.0);
        session.BeginPreview(Slot(1));
        Assert.False(session.RefreshCurrentMeasurementTargets());
        session.EndPreview(Slot(1));
        Assert.True(session.RefreshCurrentMeasurementTargets());

        LineSeries deviation = model.Series.OfType<LineSeries>().Single(series => series.Title == "Target (deviation)");
        Assert.NotEmpty(deviation.Points);
        Assert.All(deviation.Points, point => Assert.Equal(deviation.Points[0].Y, point.Y, 9));
    }

    [Fact]
    public void ACorruptSlotFile_IsSetAsideAndReported()
    {
        string path = OverlayFile.GetPath(Mode.FrequencyResponse, 4, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var reported = new List<string>();
        session.StorageFailed += (message, _) => reported.Add(message);

        session.Prepare(Mode.FrequencyResponse);

        Assert.False(File.Exists(path));
        Assert.Contains(reported, message => message.StartsWith("Overlay slot 4 for FrequencyResponse", StringComparison.Ordinal));
        Assert.Equal("", Slot(4).Title);
    }

    [Fact]
    public void AnOperationDialogOverACapture_OpensWithDefaults_NotTheOperationTheSlotHeldBefore()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        OverlayAppearance look = new(Color.FromArgb(255, 1, 2, 3), 2, OverlayLineStyle.Dot, 100);
        session.ApplyOperation(
            Slot(2),
            "Copy",
            OverlayOperationSettings.Default with { Operation = OverlayOperation.CurveA, SourceSlotA = 1, TiltEnabled = true },
            look,
            0);
        Assert.Equal(new OverlayDialogSeed("Copy", look.Color, OverlayLineStyle.Dot), Slot(2).DialogSeed(OverlayKind.Operation));

        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));

        Assert.Equal(OverlayOperationSettings.Default with { UseAmplitudeSpace = true }, Slot(2).OperationSeed);
        Assert.Equal(
            new OverlayDialogSeed("Calculated overlay 2", Slot(2).Empty.Appearance.Color, OverlayLineStyle.Dash),
            Slot(2).DialogSeed(OverlayKind.Operation));
    }

    [Fact]
    public void ATargetDialogOverACapture_OpensWithDefaults_NotTheTargetTheSlotHeldBefore()
    {
        var custom = new OverlayTargetSettings(
            0, TargetPreset.Custom, TargetCurveSpec.FromPreset(TargetPreset.Flat) with { PresenceGainDb = 4 }, 1, TargetDeviationMode.Correction);
        session.ApplyTarget(Slot(3), "Mine", custom, Slot(3).State.Appearance, 0);
        Assert.Equal(custom, Slot(3).TargetSeed);

        session.Capture(Slot(3), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));

        Assert.Equal(
            new OverlayTargetSettings(
                0,
                OverlayTargets.DefaultPreset,
                TargetCurveSpec.FromPreset(OverlayTargets.DefaultPreset),
                3,
                TargetDeviationMode.Deviation),
            Slot(3).TargetSeed);
        Assert.Equal(
            new OverlayDialogSeed("Target 3", Slot(3).Empty.Appearance.Color, OverlayLineStyle.Dash),
            Slot(3).DialogSeed(OverlayKind.Target));
    }

    [Fact]
    public void AnOffAxisSlotRestoredAfterAModeSwitch_StaysArmed_AndDrawsWhenItsScaleReturns()
    {
        shownScale = MagnitudeScale.SoundPressureLevel;
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 80.0));
        List<int> active = session.CaptureActiveSlots(mode);

        session.Prepare(mode);
        shownScale = MagnitudeScale.Relative;
        session.RestoreActiveSlots(mode, active);

        Assert.True(Slot(1).Checked);
        Assert.Null(OverlaySeriesOrNull(1));

        shownScale = MagnitudeScale.SoundPressureLevel;
        session.Show(mode);
        Assert.NotNull(OverlaySeriesOrNull(1));
    }

    [Fact]
    public void ACalculatedOverlayOrTargetIsNotOfferedItsOwnSlotAsASource()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));

        Assert.Equal(new[] { 2 }, session.CaptureSourceOptions(Slot(1)).Select(option => option.Slot));
        Assert.Equal(new[] { 1, 2 }, session.CaptureSourceOptions(Slot(3)).Select(option => option.Slot));
    }

    [Fact]
    public void ShowAllAfterHideAll_ShowsEverySlotThatCanShow()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));

        session.HideAll();
        Assert.Null(OverlaySeriesOrNull(1));
        session.ShowAll(mode);

        Assert.True(Slot(1).Checked);
        Assert.True(Slot(2).Checked);
        Assert.NotNull(OverlaySeriesOrNull(1));
        Assert.NotNull(OverlaySeriesOrNull(2));
        Assert.False(Slot(3).Checked);
    }

    [Fact]
    public void ShowingOrHidingEverySlot_RepaintsOnce()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));
        session.Capture(Slot(3), AddLiveCurve(AnalysisCurveKind.ThirdHarmonic, "HD3", -40.0));

        repaints = 0;
        session.HideAll();
        Assert.Equal(1, repaints);

        repaints = 0;
        session.ShowAll(mode);
        Assert.Equal(1, repaints);

        repaints = 0;
        session.HideAll();
        session.HideAll();
        Assert.Equal(1, repaints);
    }

    [Fact]
    public void ReplacingTheActiveSlots_HidesTheOnesNotListed()
    {
        session.Capture(Slot(1), AddLiveCurve(AnalysisCurveKind.Primary, "Frequency Response", 0.0));
        session.Capture(Slot(2), AddLiveCurve(AnalysisCurveKind.SecondHarmonic, "HD2", -30.0));
        session.Hide(Slot(2));

        session.ReplaceActiveSlots(mode, [2]);

        Assert.False(Slot(1).Checked);
        Assert.Null(OverlaySeriesOrNull(1));
        Assert.True(Slot(2).Checked);
        Assert.NotNull(OverlaySeriesOrNull(2));
        Assert.Equal(new[] { 2 }, session.CaptureActiveSlots(mode));
    }

    [Fact]
    public void RestoringAnEmptySlot_LeavesItUnchecked()
    {
        session.RestoreActiveSlots(mode, [5]);

        Assert.False(Slot(5).Checked);
    }

    [Fact]
    public void AnImpulseSlot_LoadsBeforeTheModesFirstBuild_AndRedrawsUnderEachNewFraming()
    {
        mode = Mode.ImpulseResponse;
        ImpulseOverlayFrame? frame = ImpulseFrame(origin: 0);
        sources.SetImpulseFrameProvider(() => frame);
        sources.SetImpulseCaptureProvider(_ => new ImpulseOverlayCapture(
            [new SignalPoint(100, 0.0), new SignalPoint(110, 1.0), new SignalPoint(120, 0.0)],
            AnalysisCurveKind.Primary,
            1.0,
            48_000));
        session.Prepare(mode);
        var impulse = new LineSeries { Title = "Impulse", Tag = new CurveTag(Mode.ImpulseResponse, AnalysisCurveKind.Primary) };
        impulse.Points.AddRange([new DataPoint(100, 0.0), new DataPoint(110, 1.0), new DataPoint(120, 0.0)]);
        model.Series.Add(impulse);
        session.Capture(Slot(1), impulse);

        // A new start: the slots load on entering the mode, before its first build has framed anything.
        frame = null;
        session.Prepare(Mode.FrequencyResponse);
        session.Prepare(mode);

        Assert.Equal("Overlay 1: Impulse", Slot(1).Title);
        Assert.False(File.Exists(OverlayFile.GetPath(mode, 1, root) + ".corrupt"));
        Assert.NotNull(OverlayFile.Load(mode, 1, root));

        // Each build may move the origin; the stored record coordinates are re-framed on every draw.
        frame = ImpulseFrame(origin: 100);
        session.Show(Slot(1));
        Assert.Equal(0.0, ImpulseSeriesOf(1).Points[0].X, 9);
        frame = ImpulseFrame(origin: 110);
        session.Show(Slot(1));
        Assert.Equal(-10.0, ImpulseSeriesOf(1).Points[0].X, 9);
    }

    private static ImpulseOverlayFrame ImpulseFrame(double origin) =>
        new(
            new ImpulseResponseOptions
            {
                TimeUnit = ImpulseTimeUnit.Samples,
                AmplitudeScale = ImpulseAmplitudeScale.Linear
            },
            origin,
            1.0,
            48_000);

    private LineSeries ImpulseSeriesOf(int slot) =>
        model.Series.OfType<LineSeries>().Single(series =>
            series.Tag is string tag && tag == $"overlay:ImpulseResponse:{slot}:curve");

    private OverlaySlot Slot(int index) => session.Slots[index - 1];

    private LineSeries AddLiveCurve(AnalysisCurveKind kind, string title, double level)
    {
        var series = new LineSeries
        {
            Title = title,
            Tag = new CurveTag(Mode.FrequencyResponse, kind)
        };
        for (int i = 0; i < 16; i++)
        {
            series.Points.Add(new DataPoint(20 * Math.Pow(2, i * 0.625), level));
        }

        model.Series.Add(series);
        return series;
    }

    private LineSeries OverlaySeriesOf(int slot) =>
        OverlaySeriesOrNull(slot) ?? throw new InvalidOperationException($"Slot {slot} draws nothing.");

    private LineSeries? OverlaySeriesOrNull(int slot) =>
        model.Series.OfType<LineSeries>().SingleOrDefault(series =>
            series.Tag is string tag && tag == $"overlay:FrequencyResponse:{slot}:curve");
}
