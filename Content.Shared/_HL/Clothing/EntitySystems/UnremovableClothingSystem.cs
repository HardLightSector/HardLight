using Content.Shared._HL.Clothing.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared._HL.Clothing.EntitySystems;

/// <summary>
///     A system for the operation of a component that prohibits anyone from taking off clothes that have this component.
/// </summary>
public sealed class UnremovableClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UnremovableClothingComponent, BeingUnequippedAttemptEvent>(OnUnequip);
        SubscribeLocalEvent<UnremovableClothingComponent, ExaminedEvent>(OnUnequipMarkup);
    }

    private void OnUnequip(Entity<UnremovableClothingComponent> unremovableClothing, ref BeingUnequippedAttemptEvent args)
    {
        if (TryComp<ClothingComponent>(unremovableClothing, out var clothing) && (clothing.Slots & args.SlotFlags) == SlotFlags.NONE)
            return;

        args.Cancel();
    }

    private void OnUnequipMarkup(Entity<UnremovableClothingComponent> unremovableClothing, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(unremovableClothing.Comp.ReasonMessage));
    }
}
