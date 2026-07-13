namespace Resonalyze.Audio;

public sealed record InputChannelOption(int? Offset, string Name)
{
    public override string ToString() => Name;
}
