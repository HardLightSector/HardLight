using System.Collections.Immutable;
using Content.Shared._Afterlight.Input;
using Content.Shared._Afterlight.Prototypes;
using Content.Shared._Afterlight.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._Afterlight.MobInteraction;

public abstract class SharedALMobInteractionSystem : EntitySystem
{
    [Dependency] private readonly ALPrototypeSystem _alPrototype = default!;
    [Dependency] private readonly ALUserInterfaceSystem _alUi = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public ImmutableArray<(EntityPrototype Entity, ALContentPreferenceComponent Comp)> ContentPreferencePrototypes =
        ImmutableArray<(EntityPrototype Entity, ALContentPreferenceComponent Comp)>.Empty;

    public override void Initialize()
    {
        SubscribeNetworkEvent<ALMobInteractionSetContentPreferenceBuiMsg>(OnSetPreference);

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        SubscribeLocalEvent<ALMobInteractableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ALMobInteractableComponent, GetVerbsEvent<Verb>>(OnGetVerbs);

        Subs.BuiEvents<ALMobInteractableComponent>(ALMobInteractionUi.Key, subs =>
        {
            subs.Event<ALMobInteractionSetContentPreferenceBuiMsg>(OnSetContentPreferenceMsg);
        });

        CommandBinds.Builder
            .Bind(ALKeyFunctions.ALOpenMobInteraction, new PointerInputCmdHandler(HandleOpenMobInteraction))
            .Register<SharedALMobInteractionSystem>();

        ReloadPrototypes();
    }

    private void OnSetPreference(ALMobInteractionSetContentPreferenceBuiMsg msg, EntitySessionEventArgs args)
    {
        if (msg.Enabled)
            EnablePreference(null, msg.Preference, args.SenderSession);
        else
            DisablePreference(null, msg.Preference, args.SenderSession);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SharedALMobInteractionSystem>();
    }

    private void OnMapInit(Entity<ALMobInteractableComponent> ent, ref MapInitEvent args)
    {
        // TODO AFTERLIGHT
        _alUi.EnsureUI(ent.Owner, ALMobInteractionUi.Key, "ALMobInteractionBui", requireInputValidation: false);
    }

    private void OnGetVerbs(Entity<ALMobInteractableComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        // TODO AFTERLIGHT
        var user = args.User;
        if (user != args.Target)
            return;

        args.Verbs.Add(new Verb
        {
            Text = "Open Mob Interaction Menu",
            Act = () =>
            {
                _ui.OpenUi(user, ALMobInteractionUi.Key, user);
            },
        });
    }

    private void OnSetContentPreferenceMsg(Entity<ALMobInteractableComponent> ent, ref ALMobInteractionSetContentPreferenceBuiMsg args)
    {
        if (args.Enabled)
            EnablePreference(ent, args.Preference);
        else
            DisablePreference(ent, args.Preference);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<EntityPrototype>())
            ReloadPrototypes();
    }

    private void ReloadPrototypes()
    {
        var contentPreferences = ImmutableArray.CreateBuilder<(EntityPrototype Entity, ALContentPreferenceComponent Comp)>();
        foreach (var (proto, comp) in _alPrototype.EnumerateComponents<ALContentPreferenceComponent>())
        {
            contentPreferences.Add((proto, comp));
        }

        contentPreferences.Sort((a, b) => string.Compare(a.Entity.Name, b.Entity.Name, StringComparison.InvariantCultureIgnoreCase));
        ContentPreferencePrototypes = contentPreferences.ToImmutable();
    }

    private bool HandleOpenMobInteraction(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user ||
            !coords.IsValid(EntityManager))
        {
            return true;
        }

        TryOpen(user, uid);
        return false;
    }

    private void TryOpen(EntityUid user, EntityUid target)
    {
        if (IsClientSide(target) ||
            TerminatingOrDeleted(user) ||
            TerminatingOrDeleted(target))
        {
            return;
        }

        // TODO AFTERLIGHT
        if (!TryComp(user, out ALMobInteractableComponent? userInteractable))
            // !TryComp(target, out ALMobInteractableComponent? targetInteractable))
        {
            return;
        }

        // if (!_transform.InRange(user, target, 30f))
        //     return;

        // TODO AFTERLIGHT target
        _ui.OpenUi(user, ALMobInteractionUi.Key, user);
    }

    public bool HasPreference(Entity<ALMobInteractableComponent?> ent, EntProtoId<ALContentPreferenceComponent> preference)
    {
        return Resolve(ent, ref ent.Comp, false) &&
               ent.Comp.Preferences.Contains(preference);
    }

    public bool HasPreference(Entity<ALMobInteractableComponent?>? ent, EntProtoId<ALContentPreferenceComponent> preference)
    {
        return ent != null && HasPreference(ent.Value, preference);
    }

    protected virtual void DisablePreference(
        Entity<ALMobInteractableComponent>? ent,
        EntProtoId<ALContentPreferenceComponent> preference,
        ICommonSession? session = null)
    {
    }

    protected virtual void EnablePreference(
        Entity<ALMobInteractableComponent>? ent,
        EntProtoId<ALContentPreferenceComponent> preference,
        ICommonSession? session = null)
    {
    }
}
