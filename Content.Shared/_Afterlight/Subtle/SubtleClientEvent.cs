using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Subtle;

[Serializable, NetSerializable]
public sealed class SubtleClientEvent(string emote, bool antiGhost) : EntityEventArgs
{
    public readonly string Emote = emote;
    public readonly bool AntiGhost = antiGhost;
}