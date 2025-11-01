using Content.Shared._HL.Clothing.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._HL.Clothing.Components;

/// <summary>
///     Only allows entities that pass the blacklist and whitelist to equip this clothing item.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(HLClothingWhitelistSystem))]
public sealed partial class HLClothingWhitelistComponent : Component
{
    /// <summary>
    ///     Only entities that pass this whitelist will be allowed to equip this clothing item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    ///     Only entities that pass this blacklist will be allowed to equip this clothing item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    ///     The popup's LocId that shows when you attempt to equip/unequip this item as a non-whitelisted entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId DenyReason = "inventory-component-can-equip-does-not-fit";
}
