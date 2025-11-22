using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VoreSoundCollectionComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public VoreSoundType CollectionType;

    [DataField(required: true), AutoNetworkedField]
    public List<(string Name, SoundPathSpecifier? Sound)> Sounds = new();
}
