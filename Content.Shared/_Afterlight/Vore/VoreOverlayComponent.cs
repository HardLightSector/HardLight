using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Vore;

// The only reason this component exists is that SPRITE COMPONENT IS NOT IN SHARED WOOO
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedVoreSystem))]
public sealed partial class VoreOverlayComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier.Texture? Overlay;
}
