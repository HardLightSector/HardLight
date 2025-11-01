using Content.Shared._HL.Clothing.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Shared._HL.Clothing.Components;

/// <summary>
///     The component prohibits ANYONE from taking off clothes that have this component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(UnremovableClothingSystem))]
public sealed partial class UnremovableClothingComponent : Component
{
    /// <summary>
    /// The loc string used to provide a reason for the item being unremovable
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId ReasonMessage = "comp-unremovable-clothing";
}
