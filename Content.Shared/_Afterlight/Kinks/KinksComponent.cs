using Content.Shared.Database._Afterlight;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Kinks;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKinkSystem))]
public sealed partial class KinksComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> Settings = new();
}
