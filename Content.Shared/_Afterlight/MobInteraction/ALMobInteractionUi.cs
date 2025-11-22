using System.Collections.Immutable;
using Content.Shared.Database._Afterlight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.MobInteraction;

[Serializable, NetSerializable]
public enum ALMobInteractionUi
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ALMobInteractionSetContentPreferenceBuiMsg(
    EntProtoId<ALContentPreferenceComponent> preference,
    bool enabled
) : BoundUserInterfaceMessage
{
    public readonly EntProtoId<ALContentPreferenceComponent> Preference = preference;
    public readonly bool Enabled = enabled;
}
