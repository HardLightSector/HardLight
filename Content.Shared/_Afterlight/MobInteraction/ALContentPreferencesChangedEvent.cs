using System.Collections.Immutable;
using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.MobInteraction;

[Serializable, NetSerializable]
public sealed partial class ALContentPreferencesChangedEvent(
    HashSet<EntProtoId<ALContentPreferenceComponent>> preferences) : EntityEventArgs
{
    public readonly HashSet<EntProtoId<ALContentPreferenceComponent>> Preferences = preferences;
}
