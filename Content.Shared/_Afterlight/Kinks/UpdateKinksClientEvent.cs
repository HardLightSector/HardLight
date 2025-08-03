using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Kinks;

[Serializable, NetSerializable]
public sealed class UpdateKinksClientEvent(Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks) : EntityEventArgs
{
    public Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> Kinks = kinks;
}
