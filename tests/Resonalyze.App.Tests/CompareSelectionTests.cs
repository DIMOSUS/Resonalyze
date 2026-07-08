using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Compare selection moved off Form1 into <see cref="CompareSelection"/>;
/// these pin the analysis-source mapping the mode plots rely on (a Compare
/// without a transfer IR still contributes a Frequency Response curve) and
/// the Changed notification driving the UI refresh.
/// </summary>
public sealed class CompareSelectionTests
{
    [Fact]
    public void GetAnalysisSource_ReturnsNullWithoutASelection()
    {
        var selection = new CompareSelection();

        Assert.Null(selection.Current);
        Assert.Null(selection.GetAnalysisSource());
        Assert.Null(selection.GetTimeAlignmentMeasurement());
    }

    [Fact]
    public void Set_RaisesChangedAndExposesTheSelection()
    {
        var selection = new CompareSelection();
        int changes = 0;
        selection.Changed += () => changes++;

        selection.Set("a.json", @"C:\ir\a.json", CreateSnapshot());

        Assert.Equal(1, changes);
        Assert.Equal("a.json", selection.Current!.DisplayName);
        Assert.Equal(@"C:\ir\a.json", selection.Current.SourceFilePath);

        selection.Clear();

        Assert.Equal(2, changes);
        Assert.Null(selection.Current);
    }

    [Fact]
    public void GetAnalysisSource_MapsTheSnapshotResponses()
    {
        var selection = new CompareSelection();
        Complex[] sweepIr = [new(1, 0), new(0.5, 0)];
        Complex[] transferIr = [new(0.25, 0)];
        selection.Set("a.json", null, CreateSnapshot(
            sweepIr,
            transferIr,
            transferPeakIndex: 7));

        CompareAnalysisSource? source = selection.GetAnalysisSource();

        Assert.NotNull(source);
        Assert.Equal("a.json", source!.Value.DisplayName);
        Assert.Equal(48_000, source.Value.SampleRate);
        Assert.Same(sweepIr, source.Value.SweepDeconvolutionImpulseResponse);
        Assert.Equal(3, source.Value.SweepDeconvolutionPeakIndex);
        Assert.Same(transferIr, source.Value.TransferImpulseResponse);
        Assert.Equal(7, source.Value.TransferPeakIndex);
    }

    [Fact]
    public void GetAnalysisSource_WithoutTransferIr_StillContributesTheSweepIr()
    {
        var selection = new CompareSelection();
        selection.Set("a.json", null, CreateSnapshot());

        CompareAnalysisSource? source = selection.GetAnalysisSource();

        Assert.NotNull(source);
        Assert.Empty(source!.Value.TransferImpulseResponse);
        Assert.Equal(0, source.Value.TransferPeakIndex);
    }

    [Fact]
    public void GetAnalysisSource_ReturnsNullForAnEmptySweepIr()
    {
        var selection = new CompareSelection();
        selection.Set("a.json", null, CreateSnapshot(sweepIr: Array.Empty<Complex>()));

        Assert.Null(selection.GetAnalysisSource());
        Assert.NotNull(selection.GetTimeAlignmentMeasurement());
    }

    private static MeasurementHistorySnapshot CreateSnapshot(
        Complex[]? sweepIr = null,
        Complex[]? transferIr = null,
        int? transferPeakIndex = null) =>
        new()
        {
            SampleRate = 48_000,
            SweepDeconvolutionImpulseResponse = sweepIr ?? [new(1, 0)],
            SweepDeconvolutionPeakIndex = 3,
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = transferPeakIndex,
            MeterSnapshot = InputLevelMeterSnapshot.Empty,
            Preview = new MeasurementHistoryPreview()
        };
}
