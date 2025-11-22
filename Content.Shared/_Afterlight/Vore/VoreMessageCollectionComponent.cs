using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VoreMessageCollectionComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public VoreMessageType MessageType;

    [DataField(required: true), AutoNetworkedField]
    public List<string> Messages = new();
}
