using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Resolved source measurement of one side plus its processed-IR cache; UI-free.</summary>
/// <remarks>The measurement and the captures read at the side's level share: a mono block measured with both inputs fed
/// plays at half amplitude when one side does. See docs/tech/virtual-dsp-panel.md#mono-side-share.</remarks>
internal sealed class VirtualCrossoverChannelState(Func<double> levelShare)
{
    private Complex[]? measuredImpulseResponse;
    private SideLevel? sideLevel;
    private (LiveCaptureDocument Capture, double Share, LiveCaptureDocument Read)? sharedCapture;

    public VirtualCrossoverChannelState()
        : this(() => 1.0)
    {
    }

    /// <summary>The measurement as this side plays it; set with the measurement as recorded.</summary>
    public Complex[]? TransferImpulseResponse
    {
        get => Level()?.ImpulseResponse;
        set
        {
            measuredImpulseResponse = value;
            sideLevel = null;
        }
    }

    public VirtualCrossoverSourceSnapshot? ProcessingSource => Level()?.Source;

    // One copy per share, swapped whole: readers on other threads see a consistent pair.
    private SideLevel? Level()
    {
        if (measuredImpulseResponse is not { } measured)
        {
            return null;
        }

        double share = levelShare();
        SideLevel? current = sideLevel;
        if (current != null && current.Share == share && ReferenceEquals(current.Measured, measured))
        {
            return current;
        }

        Complex[] read = share == 1.0 ? measured : [.. measured.Select(sample => sample * share)];
        current = new SideLevel(measured, share, read, new VirtualCrossoverSourceSnapshot(read));
        sideLevel = current;
        return current;
    }

    private sealed record SideLevel(
        Complex[] Measured, double Share, Complex[] ImpulseResponse, VirtualCrossoverSourceSnapshot Source);

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

    /// <summary>The capture of the family as this side plays it (see <see cref="TransferImpulseResponse"/>).</summary>
    public LiveCaptureDocument? SpatialAverageFor(
        VirtualCrossoverSpatialAverageMode mode)
    {
        LiveCaptureDocument? capture = mode switch
        {
            VirtualCrossoverSpatialAverageMode.MicArray => ArrayCapture,
            VirtualCrossoverSpatialAverageMode.MovingMic => SpatialAverage,
            _ => null
        };
        double share = levelShare();
        if (capture == null || share == 1.0)
        {
            return capture;
        }

        // Kept per capture and share: readers compare captures by identity.
        if (sharedCapture is { } kept && ReferenceEquals(kept.Capture, capture) && kept.Share == share)
        {
            return kept.Read;
        }

        LiveCaptureDocument read = capture.ShiftedBy(20.0 * Math.Log10(share));
        sharedCapture = (capture, share, read);
        return read;
    }
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
