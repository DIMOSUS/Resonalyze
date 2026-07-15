using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

public sealed class SyntheticIntegrationTests
{
    private const int SampleRate = 48_000;
    private const int SignalLength = 4096;

    [Fact]
    public void TimeAlignmentChain_RecoversKnownFractionalDelay()
    {
        const double expectedDelaySamples = 86.5;
        // A unit impulse is a flat (white) reference, so the transfer estimate's
        // power gate passes every bin; fractionally delaying it exercises the
        // full time-alignment chain on genuinely broadband content.
        double[] reference = CreateImpulseSignal(SignalLength);
        double[] target = ApplyFractionalDelay(reference, expectedDelaySamples, kernelHalfLength: 64);

        double measuredDelay = MeasureDelaySamples(
            reference,
            target,
            filter: null,
            PeakSearchMode.StrongestPeak);

        Assert.InRange(
            measuredDelay,
            expectedDelaySamples - 0.1,
            expectedDelaySamples + 0.1);
    }

    [Theory]
    [InlineData(250.0)]
    [InlineData(1_000.0)]
    [InlineData(4_000.0)]
    public void TimeAlignmentBandpass_KeepsDelayStable(double centerFrequency)
    {
        const double expectedDelaySamples = 43.0;
        double[] reference = CreateImpulseSignal(SignalLength);
        double[] target = ApplyIntegerDelay(reference, (int)expectedDelaySamples);
        double[] filter = BandpassWindow.Create(
            fftSize: DspMath.NextPowerOfTwo(SignalLength * 2),
            sampleRate: SampleRate,
            centerHz: centerFrequency,
            passOctaves: 1.0,
            fadeOctaves: 0.5);

        double measuredDelay = MeasureDelaySamples(
            reference,
            target,
            filter,
            PeakSearchMode.StrongestPeak);

        Assert.InRange(
            measuredDelay,
            expectedDelaySamples - 0.05,
            expectedDelaySamples + 0.05);
    }

    [Fact]
    public void GroupDelay_TwoTapAveragingFilterMatchesHalfSampleDelay()
    {
        var response = new Complex[4096];
        response[0] = new Complex(0.5, 0.0);
        response[1] = new Complex(0.5, 0.0);
        var measurement = new SyntheticMeasurement(response, SampleRate, maxMagnitudeIndex: 0);

        IReadOnlyList<SignalPoint> groupDelay = DataHelper.GetGroupDelay(
            measurement,
            gateOffsetMs: 0,
            leftMs: 0,
            plateauMs: 4096 * 1000.0 / SampleRate,
            rightMs: 0,
            smoothingInverseOctaves: 96).Points;

        double expectedDelayMilliseconds = 0.5 * 1000.0 / SampleRate;
        List<SignalPoint> analysisBand = groupDelay
            .Where(point => point.X >= 500 && point.X <= 8_000)
            .ToList();

        Assert.NotEmpty(analysisBand);
        Assert.All(
            analysisBand,
            point => Assert.InRange(
                point.Y,
                expectedDelayMilliseconds - 1e-9,
                expectedDelayMilliseconds + 1e-9));
    }

    [Fact]
    public void PhaseSlope_TwoTapAveragingFilterMatchesHalfSampleDelay()
    {
        var response = new Complex[4096];
        response[0] = new Complex(0.5, 0.0);
        response[1] = new Complex(0.5, 0.0);
        var measurement = new SyntheticMeasurement(response, SampleRate, maxMagnitudeIndex: 0);
        double[] rectangularWindow = Enumerable.Repeat(1.0, 4096).ToArray();

        List<SignalPoint> phase = DataHelper.GetPhaseData(
            measurement,
            offset: 0,
            length: 4096,
            window: rectangularWindow,
            unwrap: true);

        List<SignalPoint> analysisBand = phase
            .Where(point => point.X >= 500 && point.X <= 8_000)
            .ToList();
        double slope = LinearRegressionSlope(analysisBand);
        double measuredDelaySeconds = -slope / Math.Tau;
        double expectedDelaySeconds = 0.5 / SampleRate;

        Assert.InRange(
            measuredDelaySeconds,
            expectedDelaySeconds - 1e-10,
            expectedDelaySeconds + 1e-10);
    }

    private static double MeasureDelaySamples(
        IReadOnlyList<double> reference,
        IReadOnlyList<double> target,
        IReadOnlyList<double>? filter,
        PeakSearchMode peakSearchMode)
    {
        double[] relativeIr = TransferFunction.ComputeAveragedRelativeIr(
            [new TransferFunctionFrame(reference, target)]).ImpulseResponse;
        if (filter != null)
        {
            relativeIr = ApplySpectralFilter(relativeIr, filter);
        }

        double[] envelope = SignalEnvelope.Envelope(relativeIr);
        PeakSearchResult peak = SignalEnvelope.FindPeak(
            envelope,
            SampleRate,
            new PeakSearchOptions
            {
                Mode = peakSearchMode,
                SearchWindowMilliseconds = 100,
                FirstPeakThresholdBelowMaxDb = 20,
                FirstPeakMinimumSnrDb = 6
            });

        int previousIndex = (peak.SelectedIndex - 1 + envelope.Length) % envelope.Length;
        int nextIndex = (peak.SelectedIndex + 1) % envelope.Length;
        double fractionalOffset = SignalEnvelope.FindFractionalPeakOffset(
            envelope[previousIndex],
            envelope[peak.SelectedIndex],
            envelope[nextIndex]);

        return peak.SelectedIndex + fractionalOffset;
    }

    // Shapes the impulse response with a zero-phase spectral window — the same
    // frequency-domain multiply the H1 estimate applies before its IFFT, so a
    // bandpass over the estimate is reproduced on the returned IR.
    private static double[] ApplySpectralFilter(
        double[] impulseResponse,
        IReadOnlyList<double> filter)
    {
        var spectrum = new Complex[impulseResponse.Length];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            spectrum[i] = new Complex(impulseResponse[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        int bins = Math.Min(spectrum.Length, filter.Count);
        for (int bin = 0; bin < bins; bin++)
        {
            spectrum[bin] *= filter[bin];
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        var result = new double[impulseResponse.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = spectrum[i].Real;
        }

        return result;
    }

    private static double[] CreateImpulseSignal(int length)
    {
        var signal = new double[length];
        signal[0] = 1.0;
        return signal;
    }

    private static double[] ApplyFractionalDelay(
        IReadOnlyList<double> input,
        double delaySamples,
        int kernelHalfLength)
    {
        int integerDelay = (int)Math.Floor(delaySamples);
        double fractionalDelay = delaySamples - integerDelay;
        double[] kernel = CreateFractionalDelayKernel(fractionalDelay, kernelHalfLength);
        var output = new double[input.Count];

        for (int sample = 0; sample < output.Length; sample++)
        {
            double acc = 0.0;
            for (int tap = 0; tap < kernel.Length; tap++)
            {
                int sourceIndex = sample - integerDelay - (tap - kernelHalfLength);
                if ((uint)sourceIndex >= (uint)input.Count)
                {
                    continue;
                }

                acc += input[sourceIndex] * kernel[tap];
            }

            output[sample] = acc;
        }

        return output;
    }

    private static double[] ApplyIntegerDelay(
        IReadOnlyList<double> input,
        int delaySamples)
    {
        var output = new double[input.Count];
        for (int i = 0; i + delaySamples < input.Count; i++)
        {
            output[i + delaySamples] = input[i];
        }

        return output;
    }

    private static double[] CreateFractionalDelayKernel(
        double fractionalDelay,
        int halfLength)
    {
        int length = halfLength * 2 + 1;
        var kernel = new double[length];
        double sum = 0.0;

        for (int tap = 0; tap < length; tap++)
        {
            double position = tap - halfLength - fractionalDelay;
            double sinc = Math.Abs(position) < 1e-12
                ? 1.0
                : Math.Sin(Math.PI * position) / (Math.PI * position);
            double window = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * tap / (length - 1));
            double value = sinc * window;
            kernel[tap] = value;
            sum += value;
        }

        for (int tap = 0; tap < length; tap++)
        {
            kernel[tap] /= sum;
        }

        return kernel;
    }

    private static double LinearRegressionSlope(IReadOnlyList<SignalPoint> points)
    {
        Assert.NotEmpty(points);

        double averageX = points.Average(point => point.X);
        double averageY = points.Average(point => point.Y);
        double numerator = 0.0;
        double denominator = 0.0;

        foreach (SignalPoint point in points)
        {
            double centeredX = point.X - averageX;
            numerator += centeredX * (point.Y - averageY);
            denominator += centeredX * centeredX;
        }

        return numerator / denominator;
    }
}
