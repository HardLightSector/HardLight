using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Animations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedALAnimationSystem))]
public sealed partial class ALAnimationComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<ALAnimationId, ALAnimation> Animations = new();
}
