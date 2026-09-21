using System.Drawing;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class OverlaySlotStateTests
{
    private static readonly NumericFieldRange OffsetRange = new(-180m, 180m, 0);
    private static readonly double[] Correction =
        Enumerable.Range(0, RawCurveRenderer.PointCount).Select(i => i / 1000.0).ToArray();
    private static readonly OverlayAppearance Appearance = new(Color.FromArgb(255, 10, 20, 30), 3, OverlayLineStyle.Dash, 70);

    [Fact]
    public void ACapturedSlot_SurvivesItsFile()
    {
        var state = new OverlaySlotState(
            Mode.FrequencyResponse,
            "Main",
            -3m,
            Appearance,
            6,
            Captured: new CapturedCurve(
                [new DataPoint(20, 1), new DataPoint(20_000, 2)],
                MagnitudeScale.SoundPressureLevel,
                PlotModelFactory.CoherenceAxisKey,
                PhaseUnwrapped: true,
                CurveKind: AnalysisCurveKind.Primary,
                RawSpectrum: [new SignalPoint(20, 3), new SignalPoint(40, 4)],
                RawCalibrationCorrectionDb: Correction,
                MeasuredBand: new MeasuredBand(30, 18_000),
                PointsCalibrationCorrectionDb: [1.5, 2.5],
                BakedSmoothingCode: 12,
                SampleRateHz: 96_000,
                Impulse: new ImpulseOverlayCapture(
                    [new SignalPoint(0, 0.5), new SignalPoint(1, -0.25)],
                    AnalysisCurveKind.ImpulseStep,
                    0.75,
                    96_000)));

        OverlaySlotState loaded = RoundTrip(state, slot: 3);

        CapturedCurve captured = loaded.Captured!;
        Assert.Equal(OverlayKind.Captured, loaded.Kind);
        Assert.Equal(("Main", -3m, Appearance, 6), (loaded.Title, loaded.Offset, loaded.Appearance, loaded.SmoothingInverseOctaves));
        Assert.Equal(state.Captured!.Points, captured.Points);
        Assert.Equal(MagnitudeScale.SoundPressureLevel, captured.MagnitudeScale);
        Assert.Equal(PlotModelFactory.CoherenceAxisKey, captured.YAxisKey);
        Assert.Equal((true, AnalysisCurveKind.Primary), (captured.PhaseUnwrapped, captured.CurveKind));
        Assert.Equal(state.Captured.RawSpectrum, captured.RawSpectrum);
        Assert.Equal(Correction, captured.RawCalibrationCorrectionDb!);
        Assert.Equal(new MeasuredBand(30, 18_000), captured.MeasuredBand);
        Assert.Equal([1.5, 2.5], captured.PointsCalibrationCorrectionDb!);
        Assert.Equal((12, 96_000), (captured.BakedSmoothingCode, captured.SampleRateHz));
        Assert.Equal(state.Captured.Impulse!.Value.Samples, captured.Impulse!.Value.Samples);
        Assert.Equal(0.75, captured.Impulse.Value.PeakReference);
    }

    [Fact]
    public void AnOperationSlot_SurvivesItsFile_AndStatesNoScale()
    {
        var operation = new OverlayOperationSettings(
            1, null, 0, "FrequencyResponse:Primary:Main", OverlayOperation.Blend, 800, 2, true, true, 3, 500, 1.25, true);
        var state = new OverlaySlotState(Mode.FrequencyResponse, "Blend", 2m, Appearance, 0, Operation: operation);

        OverlaySlotState loaded = RoundTrip(state, slot: 5);

        Assert.Equal(OverlayKind.Operation, loaded.Kind);
        Assert.Equal(operation, loaded.Operation);
        Assert.Equal(MagnitudeScale.Relative, loaded.MagnitudeScale);
        Assert.Null(loaded.Captured);
    }

    [Fact]
    public void ATargetSlot_SurvivesItsFile()
    {
        var target = new OverlayTargetSettings(
            2,
            TargetPreset.Custom,
            TargetCurveSpec.FromPreset(TargetPreset.CarBass) with { PresenceGainDb = 1.5 },
            2.5,
            TargetDeviationMode.Correction);
        var state = new OverlaySlotState(Mode.FrequencyResponse, "Target", 0m, Appearance, 3, Target: target);

        OverlaySlotState loaded = RoundTrip(state, slot: 7);

        Assert.Equal(OverlayKind.Target, loaded.Kind);
        Assert.Equal(target, loaded.Target);
        Assert.False(loaded.IsCurrentMeasurementTarget);
    }

    [Theory]
    [InlineData(3.5, 4)]
    [InlineData(-2.5, -3)]
    [InlineData(500, 180)]
    [InlineData(-1e9, -180)]
    public void AFilesOffset_IsReadAsTheOffsetFieldShowsIt(double stored, int shown)
    {
        var file = new OverlaySlotState(Mode.FrequencyResponse, "x", 0m, Appearance, 0, Operation: OverlayOperationSettings.Default)
            .ToFile(1);
        file.Offset = stored;

        Assert.Equal(shown, OverlaySlotState.FromFile(file, OffsetRange).Offset);
    }

    [Fact]
    public void ALegacyCoherenceCapture_FindsItsAxisByTitle()
    {
        var state = new OverlaySlotState(
            Mode.FrequencyResponse,
            "Overlay 2: Coherence",
            0m,
            Appearance,
            0,
            Captured: new CapturedCurve([new DataPoint(20, 0.9), new DataPoint(40, 0.8)], MagnitudeScale.Relative));
        OverlayFile file = state.ToFile(2);

        Assert.Equal(PlotModelFactory.CoherenceAxisKey, OverlaySlotState.FromFile(file, OffsetRange).Captured!.YAxisKey);
    }

    [Fact]
    public void AnEmptySlot_HoldsNothing()
    {
        OverlaySlotState empty = OverlaySlotState.Empty(Color.Red, 0m);

        Assert.Equal(OverlayKind.Captured, empty.Kind);
        Assert.False(empty.HasContent);
        Assert.False(empty.HasCaptureData);
    }

    private static OverlaySlotState RoundTrip(OverlaySlotState state, int slot)
    {
        string root = Directory.CreateTempSubdirectory("resonalyze-overlay-state-").FullName;
        try
        {
            state.ToFile(slot).Save(root);
            return OverlaySlotState.FromFile(OverlayFile.Load(state.Mode, slot, root)!, OffsetRange);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
