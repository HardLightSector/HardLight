using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Strip;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.EntitySystems;

public sealed class ToggleableClothingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedStrippableSystem _strippable = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoidSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableClothingComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ToggleableClothingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ToggleableClothingComponent, ToggleClothingEvent>(OnToggleClothing);
        SubscribeLocalEvent<ToggleableClothingComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ToggleableClothingComponent, ComponentRemove>(OnRemoveToggleable);
        SubscribeLocalEvent<ToggleableClothingComponent, GotUnequippedEvent>(OnToggleableUnequip);

        SubscribeLocalEvent<AttachedClothingComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<AttachedClothingComponent, GotUnequippedEvent>(OnAttachedUnequip);
        SubscribeLocalEvent<AttachedClothingComponent, ComponentRemove>(OnRemoveAttached);
        SubscribeLocalEvent<AttachedClothingComponent, BeingUnequippedAttemptEvent>(OnAttachedUnequipAttempt);

        SubscribeLocalEvent<ToggleableClothingComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(GetRelayedVerbs);
        SubscribeLocalEvent<ToggleableClothingComponent, GetVerbsEvent<EquipmentVerb>>(OnGetVerbs);
        SubscribeLocalEvent<AttachedClothingComponent, GetVerbsEvent<EquipmentVerb>>(OnGetAttachedStripVerbsEvent);
        SubscribeLocalEvent<ToggleableClothingComponent, ToggleClothingDoAfterEvent>(OnDoAfterComplete);
    }

    /// <summary>
    ///     Automatically configures the component based on the clothing prototype or marking prototype.
    ///     For marking mode: Sets RequiredFlags based on this clothing's slots.
    ///     For legacy clothing mode: Sets both RequiredFlags and target Slot.
    /// </summary>
    private void AutoConfigureToggleableClothing(EntityUid uid, ToggleableClothingComponent component)
    {
        // Get the clothing component of this item (the hardsuit/jumpsuit/etc)
        if (TryComp<ClothingComponent>(uid, out var thisClothing))
        {
            component.RequiredFlags = thisClothing.Slots;
        }

        // Legacy mode: configure target slot based on spawned clothing entity
        if (component.ClothingUid != null && component.ClothingPrototype != null)
        {
            // Get the clothing component of the target item (helmet/belt) to determine target slot
            if (TryComp<ClothingComponent>(component.ClothingUid.Value, out var targetClothing))
            {
                component.Slot = GetSlotNameFromFlags(targetClothing.Slots);
            }
        }
        // New marking mode: no additional configuration needed, markings are handled dynamically

        Dirty(uid, component);
    }

    /// <summary>
    ///     Toggles the visibility of all markings on a specific body part.
    /// </summary>
    private void ToggleMarkingsOnBodyPart(EntityUid target, HumanoidAppearanceComponent humanoid, HumanoidVisualLayers bodyPart, bool visible)
    {
        // Get the corresponding marking category for this body part
        var category = MarkingCategoriesConversion.FromHumanoidVisualLayers(bodyPart);

        // Get all markings in this category and toggle their visibility
        if (humanoid.MarkingSet.TryGetCategory(category, out var markings))
        {
            foreach (var marking in markings)
            {
                _humanoidSystem.SetMarkingVisibility(target, humanoid, marking.MarkingId, visible);
            }
        }
    }

    /// <summary>
    ///     Gets the primary body part covered by the given slot flags.
    /// </summary>
    private HumanoidVisualLayers? GetBodyPartFromSlotFlags(SlotFlags slotFlags)
    {
        // Map slot flags to their corresponding visual layers/body parts
        if ((slotFlags & SlotFlags.HEAD) != 0) return HumanoidVisualLayers.Head;
        if ((slotFlags & SlotFlags.EYES) != 0) return HumanoidVisualLayers.Head;
        if ((slotFlags & SlotFlags.EARS) != 0) return HumanoidVisualLayers.Head;
        if ((slotFlags & SlotFlags.MASK) != 0) return HumanoidVisualLayers.Head;
        if ((slotFlags & SlotFlags.NECK) != 0) return HumanoidVisualLayers.Head;
        if ((slotFlags & SlotFlags.INNERCLOTHING) != 0) return HumanoidVisualLayers.Chest;
        if ((slotFlags & SlotFlags.OUTERCLOTHING) != 0) return HumanoidVisualLayers.Chest;
        if ((slotFlags & SlotFlags.GLOVES) != 0) return HumanoidVisualLayers.LHand; // Could also be RHand, Arms
        if ((slotFlags & SlotFlags.FEET) != 0) return HumanoidVisualLayers.LFoot; // Could also be RFoot, Legs
        if ((slotFlags & SlotFlags.BELT) != 0) return HumanoidVisualLayers.Chest; // Belts are worn around waist/chest area

        return null; // Unknown or unsupported slot
    }

    /// <summary>
    ///     Converts SlotFlags to the corresponding slot name.
    /// </summary>
    private string GetSlotNameFromFlags(SlotFlags slotFlags)
    {
        // Return the first matching slot name (most clothing only uses one slot)
        if ((slotFlags & SlotFlags.HEAD) != 0) return "head";
        if ((slotFlags & SlotFlags.EYES) != 0) return "eyes";
        if ((slotFlags & SlotFlags.EARS) != 0) return "ears";
        if ((slotFlags & SlotFlags.MASK) != 0) return "mask";
        if ((slotFlags & SlotFlags.NECK) != 0) return "neck";
        if ((slotFlags & SlotFlags.INNERCLOTHING) != 0) return "jumpsuit";
        if ((slotFlags & SlotFlags.OUTERCLOTHING) != 0) return "outerClothing";
        if ((slotFlags & SlotFlags.GLOVES) != 0) return "gloves";
        if ((slotFlags & SlotFlags.FEET) != 0) return "shoes";
        if ((slotFlags & SlotFlags.BELT) != 0) return "belt";
        if ((slotFlags & SlotFlags.BACK) != 0) return "back";
        if ((slotFlags & SlotFlags.IDCARD) != 0) return "id";
        if ((slotFlags & SlotFlags.POCKET) != 0) return "pocket";
        if ((slotFlags & SlotFlags.SUITSTORAGE) != 0) return "suitstorage";
        if ((slotFlags & SlotFlags.WALLET) != 0) return "wallet";

        // Default fallback
        return "head";
    }

    private void GetRelayedVerbs(EntityUid uid, ToggleableClothingComponent component, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        OnGetVerbs(uid, component, args.Args);
    }

    private void OnGetVerbs(EntityUid uid, ToggleableClothingComponent component, GetVerbsEvent<EquipmentVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || component.ClothingUid == null || component.Container == null)
            return;

        var text = component.VerbText ?? (component.ActionEntity == null ? null : Name(component.ActionEntity.Value));
        if (text == null)
            return;

        if (!_inventorySystem.InSlotWithFlags(uid, component.RequiredFlags))
            return;

        var wearer = Transform(uid).ParentUid;
        if (args.User != wearer && component.StripDelay == null)
            return;

        var verb = new EquipmentVerb()
        {
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
            Text = Loc.GetString(text),
        };

        if (args.User == wearer)
        {
            verb.EventTarget = uid;
            verb.ExecutionEventArgs = new ToggleClothingEvent() { Performer = args.User };
        }
        else
        {
            verb.Act = () => StartDoAfter(args.User, uid, Transform(uid).ParentUid, component);
        }

        args.Verbs.Add(verb);
    }

    private void StartDoAfter(EntityUid user, EntityUid item, EntityUid wearer, ToggleableClothingComponent component)
    {
        if (component.StripDelay == null)
            return;

        var (time, stealth) = _strippable.GetStripTimeModifiers(user, wearer, item, component.StripDelay.Value);

        var args = new DoAfterArgs(EntityManager, user, time, new ToggleClothingDoAfterEvent(), item, wearer, item)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            // This should just re-use the BUI range checks & cancel the do after if the BUI closes. But that is all
            // server-side at the moment.
            // TODO BUI REFACTOR.
            DistanceThreshold = 2,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return;

        if (!stealth)
        {
            var popup = Loc.GetString("strippable-component-alert-owner-interact", ("user", Identity.Entity(user, EntityManager)), ("item", item));
            _popupSystem.PopupEntity(popup, wearer, wearer, PopupType.Large);
        }
    }

    private void OnGetAttachedStripVerbsEvent(EntityUid uid, AttachedClothingComponent component, GetVerbsEvent<EquipmentVerb> args)
    {
        // redirect to the attached entity.
        OnGetVerbs(component.AttachedUid, Comp<ToggleableClothingComponent>(component.AttachedUid), args);
    }

    private void OnDoAfterComplete(EntityUid uid, ToggleableClothingComponent component, ToggleClothingDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        ToggleClothing(args.User, uid, component);
    }

    private void OnInteractHand(EntityUid uid, AttachedClothingComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleCom)
            || toggleCom.Container == null)
            return;

        if (!_inventorySystem.TryUnequip(Transform(uid).ParentUid, toggleCom.Slot, force: true))
            return;

        _containerSystem.Insert(uid, toggleCom.Container);
        args.Handled = true;
    }

    /// <summary>
    ///     Called when the suit is unequipped, to ensure that the helmet also gets unequipped.
    /// </summary>
    private void OnToggleableUnequip(EntityUid uid, ToggleableClothingComponent component, GotUnequippedEvent args)
    {
        // If it's a part of PVS departure then don't handle it.
        if (_timing.ApplyingState)
            return;

        // If the attached clothing is not currently in the container, this just assumes that it is currently equipped.
        // This should maybe double check that the entity currently in the slot is actually the attached clothing, but
        // if its not, then something else has gone wrong already...
        if (component.Container != null && component.Container.ContainedEntity == null && component.ClothingUid != null)
            _inventorySystem.TryUnequip(args.Equipee, component.Slot, force: true, triggerHandContact: true);
    }

    private void OnRemoveToggleable(EntityUid uid, ToggleableClothingComponent component, ComponentRemove args)
    {
        // If the parent/owner component of the attached clothing is being removed (entity getting deleted?) we will
        // delete the attached entity. We do this regardless of whether or not the attached entity is currently
        // "outside" of the container or not. This means that if a hardsuit takes too much damage, the helmet will also
        // automatically be deleted.

        _actionsSystem.RemoveAction(component.ActionEntity);

        if (component.ClothingUid != null && !_netMan.IsClient)
            QueueDel(component.ClothingUid.Value);
    }

    private void OnAttachedUnequipAttempt(EntityUid uid, AttachedClothingComponent component, BeingUnequippedAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnRemoveAttached(EntityUid uid, AttachedClothingComponent component, ComponentRemove args)
    {
        // if the attached component is being removed (maybe entity is being deleted?) we will just remove the
        // toggleable clothing component. This means if you had a hard-suit helmet that took too much damage, you would
        // still be left with a suit that was simply missing a helmet. There is currently no way to fix a partially
        // broken suit like this.

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleComp))
            return;

        if (toggleComp.LifeStage > ComponentLifeStage.Running)
            return;

        _actionsSystem.RemoveAction(toggleComp.ActionEntity);
        RemComp(component.AttachedUid, toggleComp);
    }

    /// <summary>
    ///     Called if the helmet was unequipped, to ensure that it gets moved into the suit's container.
    /// </summary>
    private void OnAttachedUnequip(EntityUid uid, AttachedClothingComponent component, GotUnequippedEvent args)
    {
        // Let containers worry about it.
        if (_timing.ApplyingState)
            return;

        if (component.LifeStage > ComponentLifeStage.Running)
            return;

        if (!TryComp(component.AttachedUid, out ToggleableClothingComponent? toggleComp))
            return;

        if (toggleComp.LifeStage > ComponentLifeStage.Running)
            return;

        // As unequipped gets called in the middle of container removal, we cannot call a container-insert without causing issues.
        // So we delay it and process it during a system update:
        if (toggleComp.ClothingUid != null && toggleComp.Container != null)
            _containerSystem.Insert(toggleComp.ClothingUid.Value, toggleComp.Container);
    }

    /// <summary>
    ///     Equip or unequip the toggleable clothing.
    /// </summary>
    private void OnToggleClothing(EntityUid uid, ToggleableClothingComponent component, ToggleClothingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleClothing(args.Performer, uid, component);
    }

    public void ToggleClothing(EntityUid user, EntityUid target, ToggleableClothingComponent component) // Frontier: private to public
    {
        var parent = Transform(target).ParentUid;

        // New marking mode
        if (component.MarkingPrototype != null)
        {
            ToggleMarkings(target, user, parent, component);
            return;
        }

        // Legacy clothing mode
        if (component.Container == null || component.ClothingUid == null)
            return;

        if (component.Container.ContainedEntity == null)
            _inventorySystem.TryUnequip(user, parent, component.Slot, force: true);
        else if (_inventorySystem.TryGetSlotEntity(parent, component.Slot, out var existing))
        {
            _popupSystem.PopupClient(Loc.GetString("toggleable-clothing-remove-first", ("entity", existing)),
                user, user);
        }
        else
            _inventorySystem.TryEquip(user, parent, component.ClothingUid.Value, component.Slot, triggerHandContact: true);
    }

    /// <summary>
    ///     Toggles the visibility of markings. If a specific marking prototype is specified,
    ///     only that marking is toggled. Otherwise, toggles all markings on the body part
    ///     that this clothing covers.
    /// </summary>
    private void ToggleMarkings(EntityUid clothingItem, EntityUid user, EntityUid target, ToggleableClothingComponent component)
    {
        if (!TryComp<HumanoidAppearanceComponent>(target, out var humanoid))
        {
            _popupSystem.PopupClient(Loc.GetString("toggleable-clothing-not-humanoid"), user, user);
            return;
        }

        // Toggle the marking visibility
        component.MarkingsVisible = !component.MarkingsVisible;

        if (component.MarkingPrototype != null)
        {
            // Toggle specific marking
            var markingId = component.MarkingPrototype.Value;
            _humanoidSystem.SetMarkingVisibility(target, humanoid, markingId, component.MarkingsVisible);

            // Show feedback to the user
            var markingName = "Unknown";
            if (_prototypeManager.TryIndex(markingId, out MarkingPrototype? markingProto))
            {
                markingName = markingProto.Name ?? markingId;
            }

            var message = component.MarkingsVisible
                ? Loc.GetString("toggleable-clothing-show-marking", ("marking", markingName))
                : Loc.GetString("toggleable-clothing-hide-marking", ("marking", markingName));

            _popupSystem.PopupClient(message, user, user);
        }
        else
        {
            // Toggle all markings on the body part this clothing covers
            var bodyPart = GetBodyPartFromSlotFlags(component.RequiredFlags);
            if (bodyPart != null)
            {
                ToggleMarkingsOnBodyPart(target, humanoid, bodyPart.Value, component.MarkingsVisible);

                var message = component.MarkingsVisible
                    ? Loc.GetString("toggleable-clothing-show-all-markings", ("bodyPart", bodyPart.Value.ToString()))
                    : Loc.GetString("toggleable-clothing-hide-all-markings", ("bodyPart", bodyPart.Value.ToString()));

                _popupSystem.PopupClient(message, user, user);
            }
        }

        Dirty(clothingItem, component);
    }

    private void OnGetActions(EntityUid uid, ToggleableClothingComponent component, GetItemActionsEvent args)
    {
        if (component.ActionEntity == null)
            return;

        // For marking mode: show action when item is equipped in its natural slot
        if (component.MarkingPrototype != null)
        {
            // Check if this item is equipped in any of its valid slots
            if ((args.SlotFlags & component.RequiredFlags) != 0)
            {
                args.AddAction(component.ActionEntity.Value);
            }
        }
        // For legacy mode: show action only if clothing entity exists and slot requirements are fully met
        else if (component.ClothingUid != null && (args.SlotFlags & component.RequiredFlags) == component.RequiredFlags)
        {
            args.AddAction(component.ActionEntity.Value);
        }
    }

    private void OnInit(EntityUid uid, ToggleableClothingComponent component, ComponentInit args)
    {
        // Only create container for legacy clothing mode, not for marking mode
        if (component.ClothingPrototype != null)
        {
            // Try to get existing container first, and if it's not a ContainerSlot, use a different ID
            var containerId = component.ContainerId;
            if (_containerSystem.TryGetContainer(uid, containerId, out var existingContainer))
            {
                if (existingContainer is not ContainerSlot)
                {
                    // Use a different container ID to avoid conflicts
                    containerId = "toggleable-clothing-slot";
                    component.ContainerId = containerId;
                }
                else
                {
                    // It's already the correct type, use it
                    component.Container = (ContainerSlot)existingContainer;
                    return;
                }
            }

            // Create the container with the (possibly updated) container ID
            component.Container = _containerSystem.EnsureContainer<ContainerSlot>(uid, containerId);
        }
    }

    /// <summary>
    ///     On map init, either spawn the appropriate entity into the suit slot (legacy mode),
    ///     or setup marking toggle functionality (marking mode). Also sets up the toggle action.
    /// </summary>
    private void OnMapInit(EntityUid uid, ToggleableClothingComponent component, MapInitEvent args)
    {
        // Legacy clothing mode - spawn clothing entity
        if (component.ClothingPrototype != null)
        {
            if (component.Container!.ContainedEntity is { } ent)
            {
                DebugTools.Assert(component.ClothingUid == ent, "Unexpected entity present inside of a toggleable clothing container.");
                return;
            }

            if (component.ClothingUid != null && component.ActionEntity != null)
            {
                DebugTools.Assert(Exists(component.ClothingUid), "Toggleable clothing is missing expected entity.");
                DebugTools.Assert(TryComp(component.ClothingUid, out AttachedClothingComponent? comp), "Toggleable clothing is missing an attached component");
                DebugTools.Assert(comp?.AttachedUid == uid, "Toggleable clothing uid mismatch");
            }
            else
            {
                var xform = Transform(uid);
                component.ClothingUid = Spawn(component.ClothingPrototype, xform.Coordinates);
                var attachedClothing = EnsureComp<AttachedClothingComponent>(component.ClothingUid.Value);
                attachedClothing.AttachedUid = uid;
                Dirty(component.ClothingUid.Value, attachedClothing);
                _containerSystem.Insert(component.ClothingUid.Value, component.Container!, containerXform: xform);
                Dirty(uid, component);
            }

            // Set action icon to show the spawned clothing entity
            if (_actionContainer.EnsureAction(uid, ref component.ActionEntity, out var action, component.Action))
                _actionsSystem.SetEntityIcon(component.ActionEntity.Value, component.ClothingUid, action);
        }
        else
        {
            // Marking mode - just ensure the action exists, no clothing entity to spawn
            _actionContainer.EnsureAction(uid, ref component.ActionEntity, component.Action);
        }

        // Auto-configure the component
        AutoConfigureToggleableClothing(uid, component);
    }
}

public sealed partial class ToggleClothingEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public sealed partial class ToggleClothingDoAfterEvent : SimpleDoAfterEvent
{
}
