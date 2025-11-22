using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VorePredatorComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<VoreSpace> Spaces = new();

    [DataField, AutoNetworkedField]
    public int ActiveSpace;

    [DataField, AutoNetworkedField]
    public TimeSpan EatDelay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    public bool Disconnected;
}
