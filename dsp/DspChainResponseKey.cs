namespace Resonalyze.Dsp;

/// <summary>A chain at a processor rate, equal to another when their responses are: the PEQ is compared band by band,
/// not by instance (every settings read builds a new one). Keys memos of a chain's response.</summary>
public sealed record DspChainResponseKey(DspChannelChain Chain, int ProcessorSampleRate)
{
    public bool Equals(DspChainResponseKey? other) =>
        other != null &&
        ProcessorSampleRate == other.ProcessorSampleRate &&
        Chain with { Peq = null } == other.Chain with { Peq = null } &&
        (Chain.Peq?.PreampDb ?? 0) == (other.Chain.Peq?.PreampDb ?? 0) &&
        (Chain.Peq?.Bands ?? []).SequenceEqual(other.Chain.Peq?.Bands ?? []);

    public override int GetHashCode() =>
        HashCode.Combine(Chain with { Peq = null }, ProcessorSampleRate, Chain.Peq?.Bands.Count ?? 0);
}
