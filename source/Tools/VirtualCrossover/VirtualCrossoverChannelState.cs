using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Resolved source measurement of one side plus its processed-IR cache; UI-free.</summary>
internal sealed class VirtualCrossoverChannelState
{
    private Complex[]? transferImpulseResponse;

    public Complex[]? TransferImpulseResponse
    {
        get => transferImpulseResponse;
        set
        {
            transferImpulseResponse = value;
            ProcessingSource = value == null
                ? null
                : new VirtualCrossoverSourceSnapshot(value);
        }
    }
    public VirtualCrossoverSourceSnapshot? ProcessingSource { get; private set; }

    /// <summary>Attached average of this driver (moving-mic capture or response file); only replaces the magnitude the hybrid view draws.</summary>
    public LiveCaptureDocument? SpatialAverage { get; set; }

    public LiveCaptureDocument? ArrayCapture { get; set; }

    /// <summary>The calibration this side's measurement was read through, as its file recorded it ("Own (as measured)").</summary>
    public VirtualCrossoverCalibrationSettings? MicrophoneCalibration
    {
        get => microphoneCalibration;
        set
        {
            microphoneCalibration = value;
            microphoneCalibrationCurve = value?.ToCalibrationFile();
        }
    }

    public CalibrationFile? MicrophoneCalibrationCurve => microphoneCalibrationCurve;

    private VirtualCrossoverCalibrationSettings? microphoneCalibration;
    private CalibrationFile? microphoneCalibrationCurve;

    /// <summary>What the measurement divided out; offered as the answer for a response file attached beside it.</summary>
    public ProtectiveHighPassConfiguration? ProtectiveHighPass { get; set; }

    /// <summary>Per-band spread of the array's positions; the EQ Wizard gates boosts on it.</summary>
    public double[]? ArraySpreadDb { get; set; }

    /// <summary>Zeroed outside this band (divided-out high-pass, unswept range): curves stop at its edges, sums do not.</summary>
    public MeasuredBand MeasuredBand { get; set; } = MeasuredBand.Everything;

    public LiveCaptureDocument? SpatialAverageFor(
        VirtualCrossoverSpatialAverageMode mode) =>
        mode switch
        {
            VirtualCrossoverSpatialAverageMode.MicArray => ArrayCapture,
            VirtualCrossoverSpatialAverageMode.MovingMic => SpatialAverage,
            _ => null
        };
    public int TransferPeakIndex { get; set; }
    public int SampleRate { get; set; }

    public double[]? TransferCoherence { get; set; }

    public IReadOnlyList<SignalPoint>? DistortionCurve { get; set; }

    // Keyed by processed-array identity and band: the Hilbert read of a full IR is too heavy per redraw.
    // The latch verdict is not cached: it depends on the other side's SNR, so it is decided per pair.
    public (Complex[] ProcessedIr, double LowHz, double HighHz,
        TimeAlignmentAnalysisResult Result, double? LevelDb,
        TimeAlignmentAnalysisResult? Probe)?
        ArrivalCache
    { get; set; }

    // A load may write back only while its revision matches; newer loads and Clear() bump it, so the latest pick wins.
    public int SourceRevision { get; private set; }

    public int BeginSourceLoad() => ++SourceRevision;

    public void Clear()
    {
        TransferImpulseResponse = null;
        SpatialAverage = null;
        ArrayCapture = null;
        // Everything ResolvedVirtualDspSource.ApplyTo writes must be cleared here.
        ArraySpreadDb = null;
        MeasuredBand = MeasuredBand.Everything;
        MicrophoneCalibration = null;
        ProtectiveHighPass = null;
        TransferPeakIndex = 0;
        SampleRate = 0;
        TransferCoherence = null;
        DistortionCurve = null;
        ArrivalCache = null;
        SourceRevision++;
    }
}
