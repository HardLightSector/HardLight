using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Vore;

[Serializable, NetSerializable]
public sealed class VorePromptEvent(Guid prompt, NetEntity predator, NetEntity prey, NetEntity user) : EntityEventArgs
{
    public readonly Guid Prompt = prompt;
    public readonly NetEntity Predator = predator;
    public readonly NetEntity Prey = prey;
    public readonly NetEntity User = user;
}
