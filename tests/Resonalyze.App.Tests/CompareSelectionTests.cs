using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Compare selection moved off Form1 into <see cref="CompareSelection"/>;
/// these pin the analysis-source mapping the mode plots rely on (all Compare
/// analysis runs on the transfer IR — loopback is mandatory) and the Changed
/// notification driving the UI refresh.
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
        Complex[] transferIr = [new(0.25, 0)];
        double[] coherence = [1.0, 0.5, 0.9];
        selection.Set("a.json", null, CreateSnapshot(
            transferIr: transferIr,
            transferPeakIndex: 7,
            coherence: coherence));

        CompareAnalysisSource? source = selection.GetAnalysisSource();

        Assert.NotNull(source);
        Assert.Equal("a.json", source!.Value.DisplayName);
        Assert.Equal(48_000, source.Value.SampleRate);
        Assert.Same(transferIr, source.Value.TransferImpulseResponse);
        Assert.Equal(7, source.Value.TransferPeakIndex);
        Assert.Same(coherence, source.Value.TransferCoherence);
    }

    // The Compare curve is drawn on the absolute axis with its OWN K, so the
    // selection has to carry it: loopback peak (-6 dBFS) + anchor offset
    // (94 - -20 = 114) = 108 dB SPL at 0 dBr.
    [Fact]
    public void GetAnalysisSource_CarriesTheComparedMeasurementsSplOffset()
    {
        var selection = new CompareSelection();
        selection.Set("a.json", null, CreateSnapshot(
            transferIr: [new(0.25, 0)],
            calibration: new SplCalibration
            {
                ReferenceLevelDbSpl = 94,
                MeasuredLevelDbFs = -20
            },
            meterSnapshot: new InputLevelMeterSnapshot(
                new InputLevelMeterEntry(true, -3, -6, false, false),
                new InputLevelMeterEntry(true, -6, -9, false, false))));

        CompareAnalysisSource? source = selection.GetAnalysisSource();

        Assert.Equal(108.0, source!.Value.SplOffsetDb!.Value, tolerance: 1e-9);
    }

    // Half a recipe is no recipe: an anchor without the measurement's loopback
    // level cannot place the curve absolutely, and neither can a level alone.
    [Fact]
    public void GetAnalysisSource_HasNoSplOffsetWithoutBothHalves()
    {
        var anchor = new SplCalibration { ReferenceLevelDbSpl = 94, MeasuredLevelDbFs = -20 };
        var levels = new InputLevelMeterSnapshot(
            new InputLevelMeterEntry(true, -3, -6, false, false),
            new InputLevelMeterEntry(true, -6, -9, false, false));

        var withoutLevels = new CompareSelection();
        withoutLevels.Set("a.json", null, CreateSnapshot(
            transferIr: [new(0.25, 0)], calibration: anchor));
        Assert.Null(withoutLevels.GetAnalysisSource()!.Value.SplOffsetDb);

        var withoutAnchor = new CompareSelection();
        withoutAnchor.Set("a.json", null, CreateSnapshot(
            transferIr: [new(0.25, 0)], meterSnapshot: levels));
        Assert.Null(withoutAnchor.GetAnalysisSource()!.Value.SplOffsetDb);
    }

    [Fact]
    public void GetAnalysisSource_ReturnsNullWithoutATransferIr()
    {
        var selection = new CompareSelection();
        selection.Set("a.json", null, CreateSnapshot());

        Assert.Null(selection.GetAnalysisSource());
        Assert.NotNull(selection.GetTimeAlignmentMeasurement());
    }

    // A measurement imported from a recorded sweep may be compared as a CURVE —
    // a magnitude response does not care what time it arrived at — but never as a
    // timing partner: Time Alignment compares one arrival against another, and
    // this one's is set by when its recorder was started.
    [Fact]
    public void AnImportedMeasurementComparesAsACurveButNotAsATimingPartner()
    {
        var selection = new CompareSelection();
        selection.Set("recorded.json", null, CreateSnapshot(
            transferIr: [new(0.25, 0)],
            timingReference: TimingReference.RecordedSweep));

        Assert.NotNull(selection.GetAnalysisSource());
        Assert.Null(selection.GetTimeAlignmentMeasurement());
    }

    private static MeasurementHistorySnapshot CreateSnapshot(
        Complex[]? sweepIr = null,
        Complex[]? transferIr = null,
        int? transferPeakIndex = null,
        double[]? coherence = null,
        SplCalibration? calibration = null,
        InputLevelMeterSnapshot? meterSnapshot = null,
        TimingReference timingReference = TimingReference.SynchronizedLoopback) =>
        new()
        {
            SampleRate = 48_000,
            TimingReference = timingReference,
            SweepDeconvolutionImpulseResponse = sweepIr ?? [new(1, 0)],
            SweepDeconvolutionPeakIndex = 3,
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = transferPeakIndex,
            TransferCoherence = coherence,
            MeterSnapshot = meterSnapshot ?? InputLevelMeterSnapshot.Empty,
            SplCalibration = calibration,
            Preview = new MeasurementHistoryPreview()
        };
}
