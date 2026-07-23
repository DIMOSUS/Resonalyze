using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class AuralizationTests
{
    private const int Rate = 48_000;

    // A synthetic cabin-like response: an arrival at `peakIndex` followed by an
    // exponential decay, sitting in a record padded with numerical noise far
    // below it — the shape TrimResponse exists to cut.
    private static Complex[] DecayingResponse(
        int length, int peakIndex, double decayPerSample, double noiseFloor)
    {
        var random = new Random(11);
        var response = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            response[i] = noiseFloor * (random.NextDouble() * 2.0 - 1.0);
        }

        double amplitude = 1.0;
        for (int i = peakIndex; i < length; i++)
        {
            response[i] += amplitude * Math.Cos(0.3 * (i - peakIndex));
            amplitude *= decayPerSample;
        }

        return response;
    }

    [Fact]
    public void TrimResponse_CutsTheNoiseTailButKeepsTheArrival()
    {
        // 200 ms of record; the decay reaches −60 dB after ~70 ms.
        int length = Rate / 5;
        const int PeakIndex = 480;
        double decay = Math.Pow(10.0, -60.0 / 20.0 / (0.07 * Rate));
        Complex[] response = DecayingResponse(length, PeakIndex, decay, 1e-6);

        double[] kernel = Auralization.TrimResponse(
            response, Rate, out AuralizationTrim trim);

        Assert.True(trim.Cut);
        Assert.Equal(kernel.Length, trim.Length);
        // The head is untouched: sample 0 stays sample 0, so the measured
        // propagation delay survives.
        Assert.Equal(response[PeakIndex].Real, kernel[PeakIndex], 10);
        // The cut lands after the decay reached the floor but well before the
        // record's end.
        Assert.True(kernel.Length < length);
        Assert.True(trim.TailMilliseconds >= 60.0);
        // The kernel ends faded to zero, not on a step.
        Assert.Equal(0.0, Math.Abs(kernel[^1]), 3);
    }

    [Fact]
    public void TrimResponse_ShortCleanResponseIsKeptWhole()
    {
        // A response shorter than the minimum tail has nothing to cut.
        var response = new Complex[1_024];
        response[100] = 1.0;

        double[] kernel = Auralization.TrimResponse(
            response, Rate, out AuralizationTrim trim);

        Assert.False(trim.Cut);
        Assert.Equal(response.Length, kernel.Length);
    }

    [Fact]
    public void Render_AppliesTheSideKernelsAndSharesOneGain()
    {
        // Left kernel: unit impulse. Right kernel: half-amplitude impulse. The
        // rendered channels must keep that 2:1 ratio — normalization is one
        // shared gain, never per channel — and peak at the target.
        double[] leftKernel = [1.0];
        double[] rightKernel = [0.5];
        var source = new float[Rate];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (float)(0.25 * Math.Sin(2.0 * Math.PI * 440.0 * i / Rate));
        }

        AuralizationResult result = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = leftKernel,
            RightKernel = rightKernel,
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });

        Assert.False(result.Resampled);
        Assert.Equal(Rate, result.SampleRate);
        float leftPeak = result.Channels[0].Max(Math.Abs);
        float rightPeak = result.Channels[1].Max(Math.Abs);
        double target = Math.Pow(10.0, Auralization.DefaultPeakTarget / 20.0);
        Assert.Equal(target, leftPeak, 3);
        Assert.Equal(0.5, rightPeak / leftPeak, 3);
        // The applied gain is reported: 0.25 source peak → −1 dBFS is ~+11.5 dB.
        Assert.InRange(result.AppliedGainDb, 10.0, 13.0);
    }

    [Fact]
    public void Render_LevelMatchesToTheReferenceKernels()
    {
        // The output kernel attenuates (as a cabin subtraction does); the
        // reference kernel — the same tune without that subtraction — does not.
        // The render must normalize to the LOUDER reference peak, so the output
        // lands BELOW full scale by exactly the attenuation: the removed energy
        // stays audibly removed instead of being normalized back up.
        var source = new float[500];
        source[0] = 1.0f;
        double target = Math.Pow(10.0, Auralization.DefaultPeakTarget / 20.0);

        AuralizationResult matched = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = [0.5],
            RightKernel = [0.5],
            ReferenceLeftKernel = [1.0],
            ReferenceRightKernel = [1.0],
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });

        // Reference peak 1.0 sets the gain; the 0.5 output rides at half of it,
        // and the reported gain is the reference's own (−1 dBFS on its peak).
        Assert.Equal(0.5 * target, matched.Channels[0].Max(Math.Abs), 4);
        Assert.Equal(Auralization.DefaultPeakTarget, matched.AppliedGainDb, 2);

        // Control: the SAME output kernel with NO reference normalizes to its
        // own peak and reaches full scale — the difference the reference makes.
        AuralizationResult unmatched = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = [0.5],
            RightKernel = [0.5],
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });
        Assert.Equal(target, unmatched.Channels[0].Max(Math.Abs), 4);
    }

    [Fact]
    public void Render_ReferenceLouderOrQuieter_NeverClipsTheOutput()
    {
        // "Min of the two gains" = divide by the LARGER peak. Whichever of the
        // output and the reference is louder, the output must stay at or below
        // full scale — the divisor is never smaller than the output's own peak.
        var source = new float[500];
        source[0] = 1.0f;
        double target = Math.Pow(10.0, Auralization.DefaultPeakTarget / 20.0);

        AuralizationResult louderOutput = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = [1.0],
            RightKernel = [1.0],
            ReferenceLeftKernel = [0.5],
            ReferenceRightKernel = [0.5],
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });

        // Output peak 1.0 > reference 0.5, so the output itself sets the gain
        // and reaches — but does not exceed — full scale.
        Assert.Equal(target, louderOutput.Channels[0].Max(Math.Abs), 4);
    }

    [Fact]
    public void Render_MonoSourceFeedsBothSides()
    {
        double[] kernel = [1.0];
        var source = new float[1_000];
        source[100] = 0.5f;

        AuralizationResult result = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = kernel,
            RightKernel = kernel,
            KernelSampleRate = Rate,
            SourceChannels = [source],
            SourceSampleRate = Rate
        });

        Assert.Equal(result.Channels[0].Length, result.Channels[1].Length);
        for (int i = 0; i < result.Channels[0].Length; i++)
        {
            Assert.Equal(result.Channels[0][i], result.Channels[1][i]);
        }
    }

    [Fact]
    public void Render_ResamplesTheMaterialToTheKernelRate()
    {
        double[] kernel = [1.0];
        var source = new float[44_100];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 1_000.0 * i / 44_100));
        }

        AuralizationResult result = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = kernel,
            RightKernel = kernel,
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = 44_100
        });

        Assert.True(result.Resampled);
        Assert.Equal(Rate, result.SampleRate);
        // One second of material stays one second at the new rate.
        Assert.InRange(result.Channels[0].Length, Rate - 10, Rate + 10);
    }

    [Fact]
    public void Render_DelayDifferenceBetweenSidesSurvives()
    {
        // The left kernel arrives 48 samples (1 ms) later than the right: the
        // rendered channels must carry exactly that inter-side lag, because the
        // lag IS the alignment being auditioned.
        var leftKernel = new double[128];
        var rightKernel = new double[128];
        leftKernel[58] = 1.0;
        rightKernel[10] = 1.0;
        var source = new float[500];
        source[0] = 1.0f;

        AuralizationResult result = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = leftKernel,
            RightKernel = rightKernel,
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });

        int leftPeak = IndexOfPeak(result.Channels[0]);
        int rightPeak = IndexOfPeak(result.Channels[1]);
        Assert.Equal(48, leftPeak - rightPeak);
    }

    [Fact]
    public void Render_PadsTheSidesToOneLength_WhenTheKernelsDiffer()
    {
        // TrimResponse trims each side by its own peak and decay, so unequal
        // kernel lengths are the NORMAL case — and each convolution's length is
        // source + kernel − 1. A stereo file has one frame count, so the render
        // must equalize the channels; the first release crashed the WAV writer
        // on exactly this (right side shorter → index out of bounds).
        double[] leftKernel = new double[300];
        double[] rightKernel = new double[100];
        leftKernel[0] = 1.0;
        rightKernel[0] = 1.0;
        var source = new float[500];
        source[0] = 1.0f;

        AuralizationResult result = Auralization.Render(new AuralizationRequest
        {
            LeftKernel = leftKernel,
            RightKernel = rightKernel,
            KernelSampleRate = Rate,
            SourceChannels = [source, source],
            SourceSampleRate = Rate
        });

        Assert.Equal(source.Length + leftKernel.Length - 1, result.Channels[0].Length);
        Assert.Equal(result.Channels[0].Length, result.Channels[1].Length);
        // The shorter side's extension is silence, not garbage.
        for (int i = source.Length + rightKernel.Length - 1;
            i < result.Channels[1].Length;
            i++)
        {
            Assert.Equal(0.0f, result.Channels[1][i]);
        }
    }

    private static int IndexOfPeak(float[] samples)
    {
        int index = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > Math.Abs(samples[index]))
            {
                index = i;
            }
        }

        return index;
    }
}
