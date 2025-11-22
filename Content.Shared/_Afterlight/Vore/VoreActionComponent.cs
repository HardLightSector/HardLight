using Content.Shared.Actions.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VoreActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId<EntityTargetActionComponent> ActionId = "ALActionVore";

    [DataField, AutoNetworkedField]
    public EntityUid? Action;
}
