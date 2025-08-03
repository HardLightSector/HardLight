using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Kinks;

[Serializable, NetSerializable]
public sealed class UpdateSingleKinkClientEvent(EntProtoId<KinkDefinitionComponent> kink, KinkPreference? preference) : EntityEventArgs
{
    public EntProtoId<KinkDefinitionComponent> Kink = kink;
    public KinkPreference? Preference = preference;
}
