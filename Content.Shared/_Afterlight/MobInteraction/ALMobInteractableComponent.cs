using System.Collections.Immutable;
using Content.Shared.Database._Afterlight;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.MobInteraction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedALMobInteractionSystem))]
public sealed partial class ALMobInteractableComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<EntProtoId<ALContentPreferenceComponent>> Preferences = new();
}
