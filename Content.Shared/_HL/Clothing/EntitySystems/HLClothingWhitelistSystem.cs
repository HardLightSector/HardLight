using Content.Shared._HL.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Whitelist;

namespace Content.Shared._HL.Clothing.EntitySystems;

/// <summary>
///     System that controls the logic for the clothing whitelist component.
/// </summary>
public sealed class HLClothingWhitelistSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HLClothingWhitelistComponent, BeingEquippedAttemptEvent>(OnEquip);
        SubscribeLocalEvent<HLClothingWhitelistComponent, BeingUnequippedAttemptEvent>(OnUnequip);
    }

    private void OnEquip(Entity<HLClothingWhitelistComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (!_whitelist.CheckBoth(args.Equipee, ent.Comp.Whitelist, ent.Comp.Blacklist))
            return;

        args.Reason = Loc.GetString(ent.Comp.DenyReason);
        args.Cancel();
    }

    private void OnUnequip(Entity<HLClothingWhitelistComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (!_whitelist.CheckBoth(args.Unequipee, ent.Comp.Whitelist, ent.Comp.Blacklist))
            return;

        args.Reason = Loc.GetString(ent.Comp.DenyReason);
        args.Cancel();
    }
}
