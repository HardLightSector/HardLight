using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Kinks;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKinkSystem))]
public sealed partial class KinkAlternateSpriteComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId<KinkDefinitionComponent>? Kink;

    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier.Rsi? OffSprite;

    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier.Rsi? OnSprite;
}
