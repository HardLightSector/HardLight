using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.Kinks;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKinkSystem))]
[EntityCategory("Kink")]
public sealed partial class KinkDefinitionComponent : Component
{
    [DataField(required: true), AutoNetworkedField] public EntProtoId<KinkCategoryComponent>? Category;

    [DataField, AutoNetworkedField] public string? FListImport;
}
