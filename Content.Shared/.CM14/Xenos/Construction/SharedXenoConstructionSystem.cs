using Content.Shared.Coordinates.Helpers;
using Content.Shared.Popups;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.CM14.Xenos.Construction;
using Content.Shared.CM14.Xenos.Construction.Events;
using Content.Shared.CM14.Xenos;
using Content.Shared.UserInterface;
using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;

namespace Content.Shared.CM14.Xenos.Construction;

public abstract class SharedXenoConstructionSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private EntityQuery<XenoWeedsComponent> _weedsQuery;

    public override void Initialize()
    {
        base.Initialize();

        Log.Info($"[XenoWeeds] ({(_net.IsServer ? "server" : "client")}) SharedXenoConstructionSystem.Initialize()");
        _weedsQuery = GetEntityQuery<XenoWeedsComponent>();
        SubscribeLocalEvent<XenoComponent, XenoPlantWeedsEvent>(OnXenoPlantWeeds);
        // Choose structure UI open/selection (shared side wiring)
        SubscribeLocalEvent<XenoComponent, XenoChooseStructureActionEvent>(OnXenoChooseStructureAction);
    SubscribeLocalEvent<XenoComponent, XenoChooseStructureBuiMessage>(OnXenoChooseStructureBui);
        // Secrete/build resin structure
        SubscribeLocalEvent<XenoComponent, XenoSecreteStructureEvent>(OnXenoSecreteStructureAction);
        SubscribeLocalEvent<XenoComponent, XenoSecreteStructureDoAfterEvent>(OnXenoSecreteStructureDoAfter);
    }

    private void OnXenoPlantWeeds(Entity<XenoComponent> ent, ref XenoPlantWeedsEvent args)
    {
        var coordinates = _transform.GetMoverCoordinates(ent).SnapToGrid(EntityManager, _map);
        Log.Info("[XenoWeeds] (" + (_net.IsServer ? "server" : "client") + ") Plant attempt by " + ToPrettyString(ent) + " at " + coordinates);

        // Do not spawn on the client. Server handler will perform authoritative spawn and set Handled.
        // On server, let the server-specific system handle it to avoid double-processing.
        if (_net.IsServer)
            return;
    }

    protected virtual void OnXenoChooseStructureAction(Entity<XenoComponent> xeno, ref XenoChooseStructureActionEvent args)
    {
        Log.Info($"[XenoChooseStructure] ({(_net.IsServer ? "server" : "client")}) Received action for {ToPrettyString(xeno)}; HasUi={_ui.HasUi(xeno.Owner, XenoChooseStructureUI.Key)}");
        // Only open from server to avoid predictive/UI desync; server will replicate the open to the client.
        if (_net.IsClient)
            return;

        // If there's no actor (e.g., NPC), mark handled so the pipeline reflects that we consumed the event,
        // even though we won't open a UI.
        if (!TryComp(xeno, out ActorComponent? actor))
        {
            Log.Info($"[XenoChooseStructure] (server) No actor on {ToPrettyString(xeno)}; marking handled without opening UI.");
            args.Handled = true;
            return;
        }

        // Ensure the entity actually has the UI mapped
        if (!_ui.HasUi(xeno.Owner, XenoChooseStructureUI.Key))
        {
            Log.Warning($"[XenoChooseStructure] (server) {ToPrettyString(xeno)} has no UserInterface mapping for {nameof(XenoChooseStructureUI)}.{XenoChooseStructureUI.Key}.");
            _popup.PopupClient("This xeno doesn't have a choose-structure UI.", xeno, xeno);
            args.Handled = true;
            return;
        }

        // Ensure a clean state: close any existing UI for this session, then open again
        _ui.CloseUi(xeno.Owner, XenoChooseStructureUI.Key, actor.PlayerSession);

        // Prefer TryOpenUi with the actor entity (matches evolution flow and validates open state)
        var opened = _ui.TryOpenUi(xeno.Owner, XenoChooseStructureUI.Key, actor.Owner);
        if (!opened)
        {
            // Fallback to the session-based overload used elsewhere in the codebase.
            _ui.OpenUi(xeno.Owner, XenoChooseStructureUI.Key, actor.PlayerSession);
            // Re-check whether the UI is now open for anyone.
            opened = _ui.TryGetOpenUi(xeno.Owner, XenoChooseStructureUI.Key, out _);
        }

        if (!opened)
        {
            _popup.PopupClient("Failed to open choose-structure UI.", xeno, xeno);
        }
        Log.Info($"[XenoChooseStructure] (server) Attempted to open UI for {ToPrettyString(xeno)}. Result={(opened ? "opened" : "failed")}.");
        args.Handled = true;
    }

    private void OnXenoChooseStructureBui(Entity<XenoComponent> xeno, ref XenoChooseStructureBuiMessage args)
    {
        if (!xeno.Comp.CanBuild.Contains(args.StructureId))
            return;

        xeno.Comp.BuildChoice = args.StructureId;
        if (TryComp(xeno, out ActorComponent? actor))
            _ui.CloseUi(xeno.Owner, XenoChooseStructureUI.Key, actor.PlayerSession);

        // Notify any choose-construction actions to update their icon, etc.
        var ev = new XenoConstructionChosenEvent(args.StructureId);
        foreach (var (id, _) in _actions.GetActions(xeno))
        {
            RaiseLocalEvent(id, ref ev);
        }
    }

    private void OnXenoSecreteStructureAction(Entity<XenoComponent> xeno, ref XenoSecreteStructureEvent args)
    {
        // Only the server should validate and start the do-after; clients wait for server authority
        if (_net.IsClient)
            return;

    Log.Info($"[XenoBuild] (server) Secrete action received for {ToPrettyString(xeno)} with choice={(xeno.Comp.BuildChoice?.ToString() ?? "<null>")} at target={args.Target}");

        // Ensure a choice has been made and target is valid
        if (xeno.Comp.BuildChoice == null)
        {
            _popup.PopupClient("No resin structure selected.", xeno, xeno);
            Log.Info($"[XenoBuild] (server) Secrete invoked without selection by {ToPrettyString(xeno)}");
            return;
        }

        if (!CanBuildOnTilePopup(xeno, args.Target))
        {
            Log.Info($"[XenoBuild] (server) Secrete invalid target for {ToPrettyString(xeno)} at {args.Target}");
            return;
        }

        args.Handled = true;

        // Start a short do-after then spawn on completion (server-side)
        // Use a local non-null variable to satisfy nullable flow analysis.
        var choice = xeno.Comp.BuildChoice!.Value;
        var ev = new XenoSecreteStructureDoAfterEvent(GetNetCoordinates(args.Target), choice);
        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.BuildDelay, ev, xeno)
        {
            BreakOnMove = true
        };

        var started = _doAfter.TryStartDoAfter(doAfter);
        Log.Info($"[XenoBuild] (server) Secrete started={started} for {ToPrettyString(xeno)} choice={choice} at {args.Target}");
    }

    private void OnXenoSecreteStructureDoAfter(Entity<XenoComponent> xeno, ref XenoSecreteStructureDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        Log.Info($"[XenoBuild] ({(_net.IsServer ? "server" : "client")}) DoAfter completed for {ToPrettyString(xeno)} choice={args.StructureId}");
        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager) ||
            !xeno.Comp.CanBuild.Contains(args.StructureId) ||
            !CanBuildOnTilePopup(xeno, coordinates))
        {
            return;
        }

        args.Handled = true;

        if (_net.IsServer)
        {
            var ent = Spawn(args.StructureId, coordinates);
            Log.Info($"[XenoBuild] (server) Spawned {args.StructureId} at {coordinates} for {ToPrettyString(xeno)} -> {ToPrettyString(ent)}");
        }
    }

    private bool CanBuildOnTilePopup(Entity<XenoComponent> xeno, EntityCoordinates target)
    {
        if (target.GetGridUid(EntityManager) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
        {
            Log.Info($"[XenoBuild][dbg] No grid at target {target}; gridId={(target.GetGridUid(EntityManager)?.ToString() ?? "<null>")}");
            _popup.PopupClient("You can't build there!", target, xeno);
            return false;
        }

        target = target.SnapToGrid(EntityManager, _map);
        if (!IsOnWeeds((gridId, grid), target))
        {
            Log.Info($"[XenoBuild][dbg] Not on weeds at snapped {target}");
            _popup.PopupClient("You can only shape on weeds. Find some resin before you start building!", target, xeno);
            return false;
        }

        if (!InRangePopup(xeno, target, xeno.Comp.BuildRange))
            return false;

        if (!TileSolidAndNotBlocked(target))
        {
            Log.Info($"[XenoBuild][dbg] Tile blocked or invalid at {target}");
            _popup.PopupClient("You can't build there!", target, xeno);
            return false;
        }

        return true;
    }

    private bool InRangePopup(EntityUid xeno, EntityCoordinates target, float range)
    {
        var origin = _transform.GetMoverCoordinates(xeno);
        target = target.SnapToGrid(EntityManager, _map);
        if (!origin.InRange(EntityManager, _transform, target, range))
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-cant-reach-there"), target, xeno);
            return false;
        }

        return true;
    }

    private bool TileSolidAndNotBlocked(EntityCoordinates target)
    {
     // Allow building on normal floor tiles (non-space) that are not blocked by impassable collisions.
     // Requiring Sturdy would incorrectly limit building to walls/solid tiles and break building on weeds floors.
     return target.GetTileRef(EntityManager, _map) is { } tile &&
         !tile.IsSpace() &&
         !_turf.IsTileBlocked(tile, CollisionGroup.Impassable);
    }

    private bool IsOnWeeds(Entity<MapGridComponent> grid, EntityCoordinates coordinates)
    {
        var position = _mapSystem.LocalToTile(grid, grid, coordinates);
        var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(grid, grid, position);

        while (enumerator.MoveNext(out var anchored))
        {
            if (_weedsQuery.HasComponent(anchored))
                return true;
        }

        return false;
    }
}
