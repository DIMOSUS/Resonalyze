using System.Buffers.Binary;
using System.Text.Json;
using Resonalyze.Integration.Rew;

namespace Resonalyze.App.Tests;

/// <summary>
/// The payload layer, which is where every decision about what REW receives is
/// made. It touches no HTTP, so these run everywhere and REW is never needed to
/// prove that the framing is lossless and the encoding is the one REW reads.
/// </summary>
public sealed class RewImpulseResponsePayloadTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Build_EncodesTheSamplesAsBigEndianFloat32()
    {
        double[] impulseResponse = Ramp(4_096, peakIndex: 1_000);

        RewImpulseResponseImport import = Build(impulseResponse, peakIndex: 1_000);

        float[] decoded = Decode(import.Body.Data);
        Assert.Equal(impulseResponse.Length, decoded.Length);
        // Read back through the roll: the encoding is what is under test here, not
        // the framing, so compare each sample with the one it was taken from.
        for (int i = 0; i < decoded.Length; i++)
        {
            int source = (i + impulseResponse.Length - import.PreRollSamples) % impulseResponse.Length;
            Assert.Equal((float)impulseResponse[source], decoded[i]);
        }
    }

    [Fact]
    public void Build_RollsTheBufferWithoutLosingASample()
    {
        double[] impulseResponse = Ramp(4_096, peakIndex: 1_000);

        RewImpulseResponseImport import = Build(impulseResponse, peakIndex: 1_000);

        // Rolling back has to reproduce the input exactly. A circular roll moves no
        // energy; anything that truncated or padded would fail here.
        float[] decoded = Decode(import.Body.Data);
        var restored = new float[decoded.Length];
        for (int i = 0; i < decoded.Length; i++)
        {
            restored[(i + decoded.Length - import.PreRollSamples) % decoded.Length] = decoded[i];
        }

        for (int i = 0; i < restored.Length; i++)
        {
            Assert.Equal((float)impulseResponse[i], restored[i]);
        }
    }

    [Fact]
    public void Build_StatesTheRollAsANegativeStartTime()
    {
        RewImpulseResponseImport import = Build(Ramp(262_144, peakIndex: 5_000), peakIndex: 5_000);

        Assert.Equal((int)Math.Round(RewImpulseResponsePayload.PreRollSeconds * SampleRate), import.PreRollSamples);
        Assert.Equal(-import.PreRollSamples / (double)SampleRate, import.Body.StartTime);
        Assert.True(import.Body.StartTime < 0.0);
    }

    [Fact]
    public void Build_LeavesTheArrivalAtTheTimeItWasMeasuredAt()
    {
        // t = 0 is the loopback reference, so the arrival must land at exactly the
        // delay the measurement found — the roll must not move it.
        RewImpulseResponseImport import = Build(Ramp(262_144, peakIndex: 5_000), peakIndex: 5_000);

        Assert.Equal(5_000 / (double)SampleRate, import.PeakTimeSeconds, 12);
    }

    [Fact]
    public void Build_CapsThePreRollAtAQuarterOfAShortBuffer()
    {
        // 100 ms is 4800 samples, which a 1024-sample buffer cannot lend.
        RewImpulseResponseImport import = Build(Ramp(1_024, peakIndex: 300), peakIndex: 300);

        Assert.Equal(256, import.PreRollSamples);
        Assert.Equal(300 / (double)SampleRate, import.PeakTimeSeconds, 12);
    }

    [Fact]
    public void Build_SendsTheSplOffsetOnlyWhenTheMeasurementCarriesOne()
    {
        double[] impulseResponse = Ramp(4_096, peakIndex: 1_000);

        string withAnchor = JsonSerializer.Serialize(
            Build(impulseResponse, peakIndex: 1_000, splOffsetDb: -12.5).Body);
        string without = JsonSerializer.Serialize(
            Build(impulseResponse, peakIndex: 1_000).Body);

        Assert.Contains("\"splOffset\":-12.5", withAnchor);
        // Not a zero, and not any other placeholder: an absent field is REW's way of
        // being told nothing, and a number here would be a level claim nobody made.
        Assert.DoesNotContain("splOffset", without);
    }

    [Fact]
    public void Build_UsesTheFieldNamesRewReads()
    {
        string json = JsonSerializer.Serialize(Build(Ramp(1_024, peakIndex: 300), peakIndex: 300).Body);

        Assert.Contains("\"identifier\":", json);
        Assert.Contains("\"startTime\":", json);
        Assert.Contains("\"sampleRate\":", json);
        Assert.Contains("\"applyCal\":false", json);
        Assert.Contains("\"data\":", json);
    }

    [Theory]
    [InlineData("http://localhost:4735", "http://localhost:4735/")]
    [InlineData("http://localhost:4735/", "http://localhost:4735/")]
    [InlineData("  http://192.168.1.5:4735  ", "http://192.168.1.5:4735/")]
    public void TryParseBaseAddress_AcceptsAnAddressThatCanCarryARelativePath(
        string entered,
        string expected)
    {
        Assert.True(RewApiClient.TryParseBaseAddress(entered, out Uri? parsed));
        Assert.Equal(expected, parsed!.ToString());
        // The trailing slash is why it is added: without it the last segment is
        // replaced rather than appended, and "/version" would overwrite the port path.
        Assert.Equal(expected + "version", new Uri(parsed!, "version").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:4735")]
    [InlineData("file:///etc/passwd")]
    public void TryParseBaseAddress_RefusesWhatIsNotAnHttpAddress(string entered)
    {
        Assert.False(RewApiClient.TryParseBaseAddress(entered, out Uri? parsed));
        Assert.Null(parsed);
    }

    private static RewImpulseResponseImport Build(
        double[] impulseResponse,
        int peakIndex,
        double? splOffsetDb = null) =>
        RewImpulseResponsePayload.Build(
            impulseResponse,
            peakIndex,
            SampleRate,
            "probe",
            splOffsetDb);

    /// <summary>
    /// A buffer whose every sample is distinguishable, so a roll that dropped or
    /// duplicated one is visible, with a clear maximum at the arrival.
    /// </summary>
    private static double[] Ramp(int length, int peakIndex)
    {
        var samples = new double[length];
        for (int i = 0; i < length; i++)
        {
            samples[i] = (i + 1) / (double)length * 0.25;
        }

        samples[peakIndex] = 1.0;
        return samples;
    }

    private static float[] Decode(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        var values = new float[bytes.Length / sizeof(float)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return values;
    }
}
