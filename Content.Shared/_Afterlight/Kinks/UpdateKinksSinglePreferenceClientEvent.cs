using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Kinks;

[Serializable, NetSerializable]
public sealed class UpdateKinksSinglePreferenceClientEvent(List<EntProtoId<KinkDefinitionComponent>> kinks, KinkPreference preference) : EntityEventArgs
{
    public readonly List<EntProtoId<KinkDefinitionComponent>> Kinks = kinks;
    public readonly KinkPreference Preference = preference;
}
