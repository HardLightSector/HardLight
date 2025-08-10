using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Animations;

[Serializable, NetSerializable]
public sealed class ALPlayAnimationEvent(NetEntity entity, ALAnimationId animation) : EntityEventArgs
{
    public NetEntity Entity = entity;
    public ALAnimationId Animation = animation;
}
