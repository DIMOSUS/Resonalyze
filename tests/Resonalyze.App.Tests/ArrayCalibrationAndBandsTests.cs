using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// Three ways an array could reach a consumer describing itself wrongly, all found
/// by review rather than by a failing curve — which is what they have in common:
/// each produced a plausible number rather than a visible fault.
/// </summary>
public sealed class ArrayCalibrationAndBandsTests
{
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static VirtualCrossoverCalibrationSettings Calibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [
                    new CalibrationPoint(20.0, correctionDb),
                    new CalibrationPoint(20_000.0, correctionDb)
                ],
                "flat"),
            $"flat {correctionDb:0.#}",
            null);

    private static ArrayMicrophoneCurve Microphone(
        double levelDb,
        bool measurement,
        int channel,
        VirtualCrossoverCalibrationSettings? calibration) =>
        new(
            channel,
            measurement,
            Enumerable.Repeat(levelDb, Grid.Count).ToArray(),
            AcceptedRuns: 1,
            Issues: [])
        {
            Calibration = calibration
        };

    [Fact]
    public void AMatchedArrayDeclaresTheCorrectionItSubtracted()
    {
        // The document's curve carries the microphone correction, so it has to say so:
        // a consumer reads an empty correction as "uncalibrated" and applies the
        // panel's calibration on top of one already there, which corrects twice.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(70.0, measurement: false, channel: 2, Calibration(-2.0))
        ],
            48_000,
            null)!;

        Assert.Equal(document.CurveDb.Length, document.CalibrationCorrectionDb.Length);
        Assert.All(
            document.CalibrationCorrectionDb,
            correction => Assert.Equal(-2.0, correction, 6));

        // Undoing it — adding it back, the convention the pipeline uses — returns the
        // level that was measured.
        for (int band = 0; band < document.CurveDb.Length; band++)
        {
            Assert.Equal(
                70.0,
                document.CurveDb[band] + document.CalibrationCorrectionDb[band],
                6);
        }
    }

    [Fact]
    public void AMixedArrayDeclaresWhatWasActuallySubtracted()
    {
        // No single calibration file describes a mixed array, which is why the
        // document names none. The correction is still exact, because it is MEASURED
        // as the difference between the calibrated average and the raw one rather
        // than copied from a file.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(70.0, measurement: false, channel: 2, Calibration(3.0))
        ],
            48_000,
            null)!;

        Assert.Null(document.Calibration);
        Assert.Equal(document.CurveDb.Length, document.CalibrationCorrectionDb.Length);
        for (int band = 0; band < document.CurveDb.Length; band++)
        {
            Assert.True(
                double.IsFinite(document.CurveDb[band] + document.CalibrationCorrectionDb[band]),
                "undoing the correction must land on a measured level");
        }
    }

    [Fact]
    public void AMixedArrayReachesVirtualDspWithItsOwnCorrectionsIntact()
    {
        // The accurate case, not the odd one: an array of individually calibrated
        // capsules is what the feature is for. No single curve undoes a mixed
        // correction and none could replace it, so the hybrid honours the panel's
        // INTENT — calibrated, and every position keeps the file it was measured
        // with; uncalibrated, and the exact undo gives the raw average back. Stripping
        // the mixture and applying the measurement microphone's file to all of them
        // put the Virtual DSP a decibel away from the frequency response's answer for
        // the same array.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(76.0, measurement: false, channel: 2, Calibration(3.0))
        ],
            48_000,
            null)!;

        IReadOnlyList<double> frequencies = [Grid[100], Grid[500], Grid[900]];
        int[] bands = [100, 500, 900];

        List<SignalPoint> calibrated = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 5.0), new CalibrationPoint(20_000.0, 5.0)],
                "the panel's"),
            frequencies,
            smoothingCode: 0)!;
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(document.CurveDb[bands[i]], calibrated[i].Y, 6);
        }

        List<SignalPoint> raw = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            calibration: null,
            frequencies,
            smoothingCode: 0)!;
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(
                document.CurveDb[bands[i]] + document.CalibrationCorrectionDb[bands[i]],
                raw[i].Y,
                6);
        }
    }

    [Fact]
    public void AMatchedArrayIsStillRebasedOntoThePanelsCalibration()
    {
        // The rule above must not swallow the ordinary case. One shared file is a
        // correction that CAN be undone and replaced exactly, and the hybrid has to
        // keep doing it — otherwise the array would be the one curve on the plot that
        // ignores the calibration selector.
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0)),
            Microphone(76.0, measurement: false, channel: 2, Calibration(-2.0))
        ],
            48_000,
            null)!;

        List<SignalPoint> curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            48_000,
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, 5.0), new CalibrationPoint(20_000.0, 5.0)],
                "the panel's"),
            [Grid[500]],
            smoothingCode: 0)!;

        // Its own −2 dB added back, the panel's +5 dB taken off.
        Assert.Equal(
            document.CurveDb[500] + document.CalibrationCorrectionDb[500] - 5.0,
            curve[0].Y,
            6);
    }

    [Fact]
    public void AnUncalibratedArrayDeclaresNoCorrection()
    {
        LiveCaptureDocument document = ArrayCaptureDocument.TryCreate(
        [
            Microphone(70.0, measurement: true, channel: 0, calibration: null),
            Microphone(70.0, measurement: false, channel: 2, calibration: null)
        ],
            48_000,
            null)!;

        Assert.All(
            document.CalibrationCorrectionDb,
            correction => Assert.Equal(0.0, correction, 9));
    }

    [Fact]
    public void AHistoryEntryHandsOverItsArrayAndItsFilter()
    {
        // Loading a measurement from history and loading the same file from disk have
        // to be the same measurement. They were not: the conversion dropped both, so
        // the EQ Wizard offered only the point response and read its band from a
        // filter nobody recorded.
        var snapshot = new MeasurementHistorySnapshot
        {
            SampleRate = 48_000,
            MeterSnapshot = InputLevelMeterSnapshot.Empty,
            Preview = new MeasurementHistoryPreview(),
            SweepDeconvolutionImpulseResponse = new System.Numerics.Complex[8],
            ArrayMicrophones =
            [
                Microphone(70.0, measurement: true, channel: 0, Calibration(-2.0))
            ],
            ProtectiveHighPass = new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Butterworth, 1_000, 48),
            MicrophoneCalibration = Calibration(-2.0)
        };

        ImpulseResponseFile file = snapshot.ToImpulseResponseFile();

        Assert.NotNull(file.ArrayMicrophones);
        Assert.Single(file.ArrayMicrophones!.Microphones);
        Assert.NotNull(file.ProtectiveHighPass);
        Assert.Equal(1_000, file.ProtectiveHighPass!.FrequencyHz, 6);
        // And the microphone correction the response is READ through, which
        // ImpulseResponseFile.Capture stamps: without it the two ways of writing the
        // same measurement to disk produce files that mean different things.
        Assert.NotNull(file.MicrophoneCalibration);
    }

    [Fact]
    public void ASumBreaksInTheHoleBetweenTwoDisjointSweeps()
    {
        // A woofer swept to 500 Hz beside a tweeter swept from 1 kHz: between them the
        // summed response is zero from every contributor at once, so the curve there
        // is the analysis window. The outer edges alone cannot say that — the hull of
        // the two bands is one continuous interval.
        var channels = new[]
        {
            Channel(new MeasuredBand(20, 500)),
            Channel(new MeasuredBand(1_000, 20_000))
        };
        SignalPoint[] curve =
            [new(100, -30), new(700, -30), new(2_000, -30), new(30_000, -30)];

        IReadOnlyList<SignalPoint> masked =
            ProcessedChannels.MeasuredBySomeChannel(curve, channels);

        Assert.True(double.IsFinite(masked[0].Y), "100 Hz is the woofer's");
        Assert.False(double.IsFinite(masked[1].Y), "700 Hz is nobody's");
        Assert.True(double.IsFinite(masked[2].Y), "2 kHz is the tweeter's");
        Assert.False(double.IsFinite(masked[3].Y), "30 kHz is past both");

        // And the hull still describes the ends, for the callers that only need those.
        MeasuredBand hull = ProcessedChannels.UnionOfMeasuredBands(channels);
        Assert.Equal(20, hull.LowEdgeHz, 6);
        Assert.Equal(20_000, hull.HighEdgeHz, 6);
    }

    private static ProcessedChannel Channel(MeasuredBand band) =>
        new(
            new VirtualCrossoverChannel("channel"),
            new System.Numerics.Complex[8],
            PeakIndex: 0,
            SampleRate: 48_000,
            OxyPlot.OxyColors.White,
            default,
            band);
}
