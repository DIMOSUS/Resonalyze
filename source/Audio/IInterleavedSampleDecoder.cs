namespace Resonalyze;

public interface IInterleavedSampleDecoder
{
    int ChannelCount { get; }

    int Decode(ReadOnlySpan<byte> source, float[][] destination);
}
