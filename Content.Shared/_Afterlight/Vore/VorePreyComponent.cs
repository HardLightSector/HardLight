using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VorePreyComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Disconnected;
}
