using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared.Actions;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Popups;

namespace Content.Shared._DV.Abilities.Kitsune;

public abstract class SharedKitsuneSystem : EntitySystem
{
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedChargesSystem _charges = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KitsuneComponent, CreateFoxfireActionEvent>(OnCreateFoxfire);
        SubscribeLocalEvent<FoxfireComponent, ComponentShutdown>(OnFoxfireShutdown);
        SubscribeLocalEvent<KitsuneComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KitsuneComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
    }

    private void OnProfileLoadFinished(Entity<KitsuneComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        // Eye color is stored on component to be used for fox fire/fox form color.
        if (TryComp<HumanoidAppearanceComponent>(ent, out var humanComp))
        {
            ent.Comp.Color = humanComp.EyeColor;
        }
    }

    private void OnMapInit(Entity<KitsuneComponent> ent, ref MapInitEvent args)
    {
        // Kitsune Fox form should not have action to transform into fox form.
        if (!HasComp<KitsuneFoxComponent>(ent))
            _actions.AddAction(ent, ref ent.Comp.KitsuneActionEntity, ent.Comp.KitsuneAction);
        ent.Comp.FoxfireAction = _actions.AddAction(ent, ent.Comp.FoxfireActionId);
    }

    private void OnCreateFoxfire(Entity<KitsuneComponent> ent, ref CreateFoxfireActionEvent args)
    {
        // Kitsune fox can make fox fires from their mouth otherwise they need hands.
        if ((!TryComp<HandsComponent>(ent, out var hands) || hands.Count < 1) && !HasComp<KitsuneFoxComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("fox-no-hands"), ent, ent);
            return;
        }

        // This caps the amount of fox fire summons at a time to the charge count, deleting the oldest fire when exceeded.
        if (ent.Comp.FoxfireAction is { } foxfireAction && TryComp<LimitedChargesComponent>(foxfireAction, out var foxfireCharges))
        {
            TryComp<AutoRechargeComponent>(foxfireAction, out var foxfireRecharge);
            if (_charges.GetCurrentCharges(new Entity<LimitedChargesComponent?, AutoRechargeComponent?>(foxfireAction, foxfireCharges, foxfireRecharge)) < 1)
            {
                QueueDel(ent.Comp.ActiveFoxFires[0]);
                ent.Comp.ActiveFoxFires.RemoveAt(0);
            }
        }

        var fireEnt = Spawn(ent.Comp.FoxfirePrototype, Transform(ent).Coordinates);
        var fireComp = EnsureComp<FoxfireComponent>(fireEnt);
        fireComp.Kitsune = ent;
        ent.Comp.ActiveFoxFires.Add(fireEnt);
        Dirty(fireEnt, fireComp);
        Dirty(ent);

        _light.SetColor(fireEnt, ent.Comp.Color ?? Color.Purple);

        args.Handled = true;
    }

    private void OnFoxfireShutdown(Entity<FoxfireComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Kitsune is not { } kitsune || !TryComp<KitsuneComponent>(kitsune, out var kitsuneComp))
            return;

        // Stop tracking the removed fox fire
        kitsuneComp.ActiveFoxFires.Remove(ent);

        // Refund the fox fire charge
        if (kitsuneComp.FoxfireAction is not { } foxfireAction)
            return;

        if (!TryComp<LimitedChargesComponent>(foxfireAction, out var chargesComp))
            return;

        TryComp<AutoRechargeComponent>(foxfireAction, out var rechargeComp);
        var chargesEntity = new Entity<LimitedChargesComponent?, AutoRechargeComponent?>(foxfireAction, chargesComp, rechargeComp);
        _charges.AddCharges(chargesEntity, 1);

        // If charges exceeds the maximum then set charges to max
        if (_charges.GetCurrentCharges(chargesEntity) > chargesComp.MaxCharges)
            _charges.SetCharges(chargesEntity, chargesComp.MaxCharges);

        Dirty(kitsune, kitsuneComp);
    }
}

public sealed partial class MorphIntoKitsune : InstantActionEvent;
