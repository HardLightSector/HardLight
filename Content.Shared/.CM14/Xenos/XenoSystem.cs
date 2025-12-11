using System;
using System.Collections.Generic;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Access.Components;
using Content.Shared.CM14.Xenos.Evolution;
using Content.Shared.CM14.Xenos.Construction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared.CM14.Xenos;

public sealed class XenoSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _action = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoComponent, MapInitEvent>(OnXenoMapInit);
        SubscribeLocalEvent<XenoComponent, EntityUnpausedEvent>(OnXenoUnpaused);
        SubscribeLocalEvent<XenoComponent, XenoOpenEvolutionsEvent>(OnXenoEvolve);
        SubscribeLocalEvent<XenoComponent, EvolveBuiMessage>(OnXenoEvolveBui);
        SubscribeLocalEvent<XenoComponent, GetAccessTagsEvent>(OnXenoGetAdditionalAccess);
    }

    private void OnXenoMapInit(Entity<XenoComponent> ent, ref MapInitEvent args)
    {
        // Log.Info($"[Xeno] ({(_net.IsServer ? "server" : "client")}) MapInit {ToPrettyString(ent)} actionIds={ent.Comp.ActionIds.Count}"); // verbose init log disabled
        if (_net.IsServer)
        {
            // Server-authoritative action registration (replicated to clients)
            foreach (var actionId in ent.Comp.ActionIds)
            {
                if (!ent.Comp.Actions.ContainsKey(actionId) &&
                    _action.AddAction(ent, actionId) is { } newAction)
                {
                    ent.Comp.Actions[actionId] = newAction;
                }
            }

            // Ensure key actions are properly configured server-side.
            // - Plant Weeds: always has an event instance and is raised on user.
            // - Choose Structure: ensure it's raised on user so the shared handler gets the event.
            // - Secrete Structure: ensure it is a world target action raised on user as well.
            if (ent.Comp.Actions.TryGetValue("ActionXenoPlantWeeds", out var weedsAction))
            {
                if (TryComp<InstantActionComponent>(weedsAction, out var instant))
                {
                    instant.Event ??= new XenoPlantWeedsEvent();
                    instant.RaiseOnUser = true;
                    instant.RaiseOnAction = false;
                    Dirty(weedsAction, instant);
                    _action.SetEnabled(weedsAction, true);
                }
            }

            if (ent.Comp.Actions.TryGetValue("ActionXenoChooseStructure", out var chooseAction))
            {
                if (TryComp<InstantActionComponent>(chooseAction, out var instant))
                {
                    instant.Event ??= new Content.Shared.CM14.Xenos.Construction.Events.XenoChooseStructureActionEvent();
                    instant.RaiseOnUser = true;
                    instant.RaiseOnAction = false;
                    instant.CheckCanInteract = false;
                    instant.CheckConsciousness = true;
                    Dirty(chooseAction, instant);
                }
            }

            if (ent.Comp.Actions.TryGetValue("ActionXenoSecreteStructure", out var secreteAction))
            {
                if (TryComp<WorldTargetActionComponent>(secreteAction, out var wta))
                {
                    wta.Event ??= new Content.Shared.CM14.Xenos.Construction.Events.XenoSecreteStructureEvent();
                    wta.RaiseOnUser = true;
                    wta.RaiseOnAction = false;
                    wta.CheckCanInteract = false;
                    wta.CheckConsciousness = true;
                    // Critical: disable access/LOS gating and align target range with the xeno's build range.
                    // Otherwise ValidateWorldTarget can fail before our own tile validation runs, leading to handled=false.
                    wta.CheckCanAccess = false;
                    wta.Range = ent.Comp.BuildRange;
                    Dirty(secreteAction, wta);
                }
            }

            // Ensure a sane default build choice so secrete never has a null selection.
            if (ent.Comp.BuildChoice == null)
            {
                EntProtoId defaultChoice = "WallXenoResin";
                if (ent.Comp.CanBuild.Count > 0)
                {
                    if (!ent.Comp.CanBuild.Contains(defaultChoice))
                        defaultChoice = ent.Comp.CanBuild[0];

                    ent.Comp.BuildChoice = defaultChoice;
                    Dirty(ent);

                    // Notify actions that a selection exists (helps icons/tooltips sync up).
                    foreach (var (_, actionUid) in ent.Comp.Actions)
                    {
                        var chosenEv = new Content.Shared.CM14.Xenos.Construction.Events.XenoConstructionChosenEvent(defaultChoice);
                        RaiseLocalEvent(actionUid, ref chosenEv);
                    }
                }
            }

            // Log.Info($"[Xeno] (server) Actions registered={ent.Comp.Actions.Count} for {ToPrettyString(ent)}"); // verbose action registration log disabled
        }

        // Evolution action: prefer an existing evolve action from ActionIds to avoid duplicates.
        if (_net.IsServer && ent.Comp.EvolvesTo.Count > 0)
        {
            // Prefer ActionXenoEvolve60 if present, otherwise use configured EvolveActionId
            EntityUid? evolveAction = null;
            if (ent.Comp.Actions.TryGetValue("ActionXenoEvolve60", out var evo60))
                evolveAction = evo60;
            else if (ent.Comp.Actions.TryGetValue(ent.Comp.EvolveActionId, out var evo))
                evolveAction = evo;
            else
                _action.AddAction(ent, ref evolveAction, ent.Comp.EvolveActionId);

            ent.Comp.EvolveAction = evolveAction;

            // Only set cooldown here if the action doesn't have a specialized cooldown component.
            if (evolveAction != null && !HasComp<CM14.Xenos.Evolution.XenoEvolveActionComponent>(evolveAction.Value))
            {
                _action.SetCooldown(evolveAction, _timing.CurTime, _timing.CurTime + ent.Comp.EvolveIn);
                // var end = _timing.CurTime + ent.Comp.EvolveIn;
                // Log.Info($"[Xeno] Set evolve cooldown for {ToPrettyString(ent)} evolveIn={ent.Comp.EvolveIn.TotalSeconds:F1}s endsAt={end.TotalSeconds:F1}s action={ToPrettyString(evolveAction.Value)}"); // verbose cooldown log disabled
            }
            else if (evolveAction != null)
            {
                // Log.Info($"[Xeno] Using specialized evolve action component for {ToPrettyString(ent)} action={ToPrettyString(evolveAction.Value)} cooldownCompPresent=true"); // verbose cooldown component log disabled
            }
            else
            {
                // Log.Info($"[Xeno] No evolve action could be created for {ToPrettyString(ent)}"); // verbose evolve action failure log disabled
            }

            // Ensure evolve action raises on the user entity so our xeno handlers receive it.
            if (evolveAction != null && TryComp<InstantActionComponent>(evolveAction.Value, out var evolveInstant))
            {
                evolveInstant.RaiseOnUser = true;
                evolveInstant.RaiseOnAction = false;
                Dirty(evolveAction.Value, evolveInstant);
            }

            // (Scheduling moved to Update to avoid briefly setting a pending time before a player mind transfers in on evolution.)
        }
    }

    private void OnXenoUnpaused(Entity<XenoComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.NextPlasmaRegenTime += args.PausedTime;
    }
    private void OnXenoGetAdditionalAccess(Entity<XenoComponent> ent, ref GetAccessTagsEvent args)
    {
        args.Tags.UnionWith(ent.Comp.AccessLevels);
    }
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<XenoComponent>();
        var time = _timing.CurTime;
        // Defer evolution events to avoid modifying collections mid-enumeration.
        var toEvolve = new List<EntityUid>();

        while (query.MoveNext(out var uid, out var xeno))
        {
            // Schedule if needed (only once, only for AI-controlled xenos).
            if (_net.IsServer && xeno.PendingAutoEvolveTime == null && xeno.EvolvesTo.Count > 0 && xeno.EvolveIn > TimeSpan.Zero && !TryComp<ActorComponent>(uid, out _))
            {
                xeno.PendingAutoEvolveTime = time + xeno.EvolveIn;
                Dirty(uid, xeno);
                // Log.Info($"[Xeno] Scheduled AI auto-evolution (Update) for {ToPrettyString(uid)} dueAt={xeno.PendingAutoEvolveTime.Value.TotalSeconds:F1}s"); // verbose scheduling log disabled
            }
            // If a player took control, cancel pending auto-evolution.
            if (xeno.PendingAutoEvolveTime != null && TryComp<ActorComponent>(uid, out _))
            {
                xeno.PendingAutoEvolveTime = null;
                Dirty(uid, xeno);
                // Log.Info($"[Xeno] Cleared pending auto-evolution due to actor control for {ToPrettyString(uid)}"); // verbose cancellation log disabled
            }
            // Process scheduled AI auto-evolution (deferred)
            else if (_net.IsServer && xeno.PendingAutoEvolveTime is { } due && time >= due && xeno.EvolvesTo.Count > 0)
            {
                xeno.PendingAutoEvolveTime = null;
                Dirty(uid, xeno);
                toEvolve.Add(uid);
                // Log.Info($"[Xeno] Queued scheduled auto-evolution for {ToPrettyString(uid)}"); // verbose queue log disabled
            }

            if (time >= xeno.NextPlasmaRegenTime)
            {
                xeno.Plasma += xeno.PlasmaRegen;
                xeno.NextPlasmaRegenTime = time + xeno.PlasmaRegenCooldown;
                Dirty(uid, xeno);
            }
        }

        // Fire evolution events after enumeration.
        foreach (var uid in toEvolve)
        {
            var ev = new XenoOpenEvolutionsEvent();
            RaiseLocalEvent(uid, ev);
        }
    }

    public bool HasPlasma(Entity<XenoComponent> xeno, int plasma)
    {
        return xeno.Comp.Plasma >= plasma;
    }

    public bool TryRemovePlasmaPopup(Entity<XenoComponent> xeno, int plasma)
    {
        if (!HasPlasma(xeno, plasma))
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-not-enough-plasma"), xeno, xeno);
            return false;
        }

        RemovePlasma(xeno, plasma);
        return true;
    }

    public void RemovePlasma(Entity<XenoComponent> xeno, int plasma)
    {
        xeno.Comp.Plasma = Math.Max(xeno.Comp.Plasma - plasma, 0);
        Dirty(xeno);
        if (xeno.Comp.EvolvesTo.Count == 0)
            return;

        // Ensure evolve action exists and set cooldown if needed (but avoid overriding specialized component cooldowns)
        if (xeno.Comp.EvolvesTo.Count > 0)
        {
            EntityUid? evolveAction = null;
            if (xeno.Comp.Actions.TryGetValue("ActionXenoEvolve60", out var evo60))
                evolveAction = evo60;
            else if (xeno.Comp.Actions.TryGetValue(xeno.Comp.EvolveActionId, out var evo))
                evolveAction = evo;
            else
                _action.AddAction(xeno, ref evolveAction, xeno.Comp.EvolveActionId);

            xeno.Comp.EvolveAction = evolveAction;

            if (evolveAction != null && !HasComp<CM14.Xenos.Evolution.XenoEvolveActionComponent>(evolveAction.Value))
                _action.SetCooldown(evolveAction, _timing.CurTime, _timing.CurTime + xeno.Comp.EvolveIn);
        }
    }

    private void OnXenoEvolve(Entity<XenoComponent> ent, ref XenoOpenEvolutionsEvent args)
    {
        if (_net.IsClient)
            return;

        // Don't allow dead xenos to evolve
        if (_mobState.IsDead(ent))
            return;

        Log.Debug($"[Xeno] Evolution handler start for {ToPrettyString(ent)} evolvesTo={ent.Comp.EvolvesTo.Count} hasActor={TryComp(ent, out ActorComponent? _)}");
        if (TryComp(ent, out ActorComponent? actor))
        {
            if (_ui.TryOpenUi(ent.Owner, XenoEvolutionUIKey.Key, actor.Owner))
            {
                //Log.Debug($"[Xeno] Opened evolution UI for {ToPrettyString(ent)} actor={ToPrettyString(actor.Owner)}");
                return;
            }
            else
            {
                //Log.Debug($"[Xeno] Failed to open evolution UI for {ToPrettyString(ent)} actor={ToPrettyString(actor.Owner)} falling back");
            }
        }

        if (ent.Comp.EvolvesTo.Count > 0)
        {
            var targetProto = ent.Comp.EvolvesTo[0];
            //Log.Info($"[Xeno] Auto-evolving {ToPrettyString(ent)} to {targetProto}");
            var evolution = Spawn(targetProto, _transform.GetMoverCoordinates(ent.Owner));
            //Log.Info($"[Xeno] Spawned evolution entity {ToPrettyString(evolution)} for {ToPrettyString(ent)}");

            if (_mind.TryGetMind(ent, out var mindId, out _))
            {
                _mind.TransferTo(mindId, evolution);
                _mind.UnVisit(mindId);
                //Log.Debug($"[Xeno] Transferred mind {mindId} to evolution {ToPrettyString(evolution)} from {ToPrettyString(ent)}");
            }

            Del(ent.Owner);
            //Log.Debug($"[Xeno] Deleted original xeno {ToPrettyString(ent)} after evolution");
        }
        else
        {
           //Log.Warning($"[Xeno] Evolution event received but no EvolvesTo entries for {ToPrettyString(ent)}");
        }
    }

    private void OnXenoEvolveBui(Entity<XenoComponent> ent, ref EvolveBuiMessage args)
    {
        // Don't allow dead xenos to evolve
        if (_mobState.IsDead(ent))
            return;

        if (!_mind.TryGetMind(ent, out var mindId, out _))
            return;

        var choices = ent.Comp.EvolvesTo.Count;
        if (args.Choice >= choices || args.Choice < 0)
        {
            //Log.Warning($"User {ToPrettyString(args.Actor)} sent an out of bounds evolution choice: {args.Choice}. Choices: {choices}");
            return;
        }

        var evolution = Spawn(ent.Comp.EvolvesTo[args.Choice], _transform.GetMoverCoordinates(ent.Owner));
        _mind.TransferTo(mindId, evolution);
        _mind.UnVisit(mindId);
        Del(ent.Owner);

        if (TryComp(ent, out ActorComponent? actor))
            _ui.CloseUi(ent.Owner, XenoEvolutionUIKey.Key, actor.PlayerSession);
    }
}
