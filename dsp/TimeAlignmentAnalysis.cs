using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed class TimeAlignmentAnalysisOptions
{
    public bool UseBandpassWindow { get; init; }
    public double BandpassCenterHz { get; init; } = 1000;
    public double BandpassPassOctaves { get; init; } = 1;
    public double BandpassFadeOctaves { get; init; } = 0.5;
    public double FirstPeakThresholdBelowMaxDb { get; init; } = 25;
    public double FirstPeakMinimumSnrDb { get; init; } = 12;
    public double PeakSearchWindowMilliseconds { get; init; } = 80;
    public bool WrapPeakPositions { get; init; }
}

public readonly record struct TimeAlignmentAnalysisResult(
    double[] EnvelopeSamples,
    int EnvelopePeakIndex,
    double EnvelopePeak,
    int StrongestEnvelopePeakIndex,
    double StrongestEnvelopePeak,
    double ConfidenceDecibels,
    double FirstArrivalPeakSample,
    double FirstArrivalDelayMilliseconds,
    double StrongestPeakSample,
    double StrongestDelayMilliseconds);

public static class TimeAlignmentAnalysis
{
    public static TimeAlignmentAnalysisResult Analyze(
        IReadOnlyList<double> impulseResponse,
        int sampleRate,
        TimeAlignmentAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        ArgumentNullException.ThrowIfNull(options);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException(
                "Impulse response must not be empty.",
                nameof(impulseResponse));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        double[] analysisSignal = options.UseBandpassWindow
            ? ApplyBandpassWindow(
                impulseResponse,
                sampleRate,
                options.BandpassCenterHz,
                options.BandpassPassOctaves,
                options.BandpassFadeOctaves)
            : impulseResponse.ToArray();

        double[] envelope = SignalEnvelope.Envelope(analysisSignal);
        PeakSearchResult peakSearchResult = SignalEnvelope.FindPeak(
            envelope,
            sampleRate,
            new PeakSearchOptions
            {
                Mode = PeakSearchMode.FirstArrival,
                FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
                FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
                SearchWindowMilliseconds = options.PeakSearchWindowMilliseconds
            });

        int envelopePeakIndex = peakSearchResult.SelectedIndex;
        double envelopePeak = envelope[envelopePeakIndex];
        double strongestPeak = peakSearchResult.StrongestPeak;
        int strongestPeakIndex = peakSearchResult.StrongestIndex;
        double firstArrivalFractionalOffset =
            FindFractionalPeakOffset(envelope, envelopePeakIndex);
        double strongestFractionalOffset =
            FindFractionalPeakOffset(envelope, strongestPeakIndex);

        double firstArrivalPeakSample = envelopePeakIndex + firstArrivalFractionalOffset;
        double strongestPeakSample = strongestPeakIndex + strongestFractionalOffset;
        if (options.WrapPeakPositions)
        {
            firstArrivalPeakSample = ToSignedDelaySamples(
                firstArrivalPeakSample,
                envelope.Length);
            strongestPeakSample = ToSignedDelaySamples(
                strongestPeakSample,
                envelope.Length);
        }

        return new TimeAlignmentAnalysisResult(
            envelope,
            envelopePeakIndex,
            envelopePeak,
            strongestPeakIndex,
            strongestPeak,
            SignalEnvelope.EstimatePeakConfidenceDecibels(
                envelope,
                envelopePeakIndex,
                envelopePeak),
            firstArrivalPeakSample,
            firstArrivalPeakSample * 1000.0 / sampleRate,
            strongestPeakSample,
            strongestPeakSample * 1000.0 / sampleRate);
    }

    private static double[] ApplyBandpassWindow(
        IReadOnlyList<double> signal,
        int sampleRate,
        double centerHz,
        double passOctaves,
        double fadeOctaves)
    {
        var spectrum = new Complex[signal.Count];
        for (int i = 0; i < signal.Count; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        double[] window = BandpassWindow.Create(
            spectrum.Length,
            sampleRate,
            centerHz,
            passOctaves,
            fadeOctaves);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= window[i];
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var filtered = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; i++)
        {
            filtered[i] = spectrum[i].Real;
        }

        return filtered;
    }

    private static double FindFractionalPeakOffset(
        IReadOnlyList<double> envelope,
        int peakIndex)
    {
        if (peakIndex <= 0 || peakIndex >= envelope.Count - 1)
        {
            return 0.0;
        }

        return SignalEnvelope.FindFractionalPeakOffset(
            envelope[peakIndex - 1],
            envelope[peakIndex],
            envelope[peakIndex + 1]);
    }

    private static double ToSignedDelaySamples(double wrappedPeakSample, int length) =>
        wrappedPeakSample <= length * 0.5
            ? wrappedPeakSample
            : wrappedPeakSample - length;
}
