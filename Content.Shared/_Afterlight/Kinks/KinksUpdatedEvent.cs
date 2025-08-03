using Content.Shared.Database._Afterlight;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Kinks;

[Serializable, NetSerializable]
public sealed class KinksUpdatedEvent(
    NetUserId player,
    Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks)
    : EntityEventArgs
{
    public NetUserId Player = player;
    // TODO AFTERLIGHT immutable dictionary when https://github.com/space-wizards/netserializer/pull/5 is merged
    public Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> Kinks = kinks;
}
