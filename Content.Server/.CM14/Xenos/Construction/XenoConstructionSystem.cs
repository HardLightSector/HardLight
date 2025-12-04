using System.Numerics;
using Content.Server.Spreader;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.CM14.Xenos;
using Content.Shared.CM14.Xenos.Construction;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using XenoWeedableComponent = Content.Shared.CM14.Xenos.Construction.Nest.XenoWeedableComponent;
using XenoWeedsComponent = Content.Shared.CM14.Xenos.Construction.XenoWeedsComponent;

namespace Content.Server.CM14.Xenos.Construction;

[UsedImplicitly]
public sealed class XenoConstructionServerSystem : SharedXenoConstructionSystem
{
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    private readonly List<EntityUid> _anchored = new();

    public override void Initialize()
    {
        base.Initialize();
        Log.Info("[XenoWeeds] (server) XenoConstructionSystem.Initialize()");

        SubscribeLocalEvent<XenoWeedsComponent, SpreadNeighborsEvent>(OnWeedsSpreadNeighbors);
        SubscribeLocalEvent<XenoWeedsComponent, AnchorStateChangedEvent>(OnWeedsAnchorChanged);
        SubscribeLocalEvent<XenoWeedableComponent, AnchorStateChangedEvent>(OnWeedableAnchorStateChanged);
        // Server-side guarantee: perform weeds spawn & mark handled.
        SubscribeLocalEvent<XenoComponent, XenoPlantWeedsEvent>(OnXenoPlantWeedsServer);
        // Server-side guarantee: make sure xenos actually have the Plant Weeds action attached.
        SubscribeLocalEvent<XenoComponent, MapInitEvent>(OnXenoMapInitServer);
        SubscribeLocalEvent<XenoComponent, ComponentStartup>(OnXenoStartupServer);

        // Fallback: Intercept action requests and handle weeds planting even if action wasn't fully registered server-side yet.
        SubscribeAllEvent<RequestPerformActionEvent>(OnWeedsActionRequest);
    }

    private void OnWeedsAnchorChanged(Entity<XenoWeedsComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            QueueDel(ent);
    }

    private void EnsureWeedsAction(Entity<XenoComponent> ent)
    {
        if (!ent.Comp.Actions.TryGetValue("ActionXenoPlantWeeds", out var actionEnt))
        {
            _actions.AddAction(ent, ref actionEnt, "ActionXenoPlantWeeds");
        }

        if (actionEnt != null && TryComp<InstantActionComponent>(actionEnt.Value, out var instant))
        {
            instant.Event ??= new XenoPlantWeedsEvent();
            instant.RaiseOnUser = true;
            instant.RaiseOnAction = false;
            Dirty(actionEnt.Value, instant);
            _actions.SetEnabled(actionEnt, true);

            Log.Info($"[XenoWeeds] (server) EnsureWeedsAction attached for {ToPrettyString(ent)} -> {ToPrettyString(actionEnt.Value)}; Enabled={instant.Enabled} RaiseOnUser={instant.RaiseOnUser} RaiseOnAction={instant.RaiseOnAction}");
        }
        else
        {
            Log.Warning($"[XenoWeeds] (server) EnsureWeedsAction failed for {ToPrettyString(ent)}; actionEnt={(actionEnt?.ToString() ?? "null")} ");
        }
    }

    private void OnXenoMapInitServer(Entity<XenoComponent> ent, ref MapInitEvent args)
    {
        // Add the action if missing on server and configure it to raise on user.
        EnsureWeedsAction(ent);
    }

    private void OnXenoStartupServer(Entity<XenoComponent> ent, ref ComponentStartup args)
    {
        EnsureWeedsAction(ent);
    }

    private void OnXenoPlantWeedsServer(Entity<XenoComponent> ent, ref XenoPlantWeedsEvent args)
    {
        // If another handler already processed this (e.g., shared handler), don't double-spawn.
        if (args.Handled)
            return;

        // Only act server-side (defensive)
        if (!_net.IsServer)
            return;

        var coordinates = _transform.GetMoverCoordinates(ent).SnapToGrid(EntityManager, _map);
        Log.Info($"[XenoWeeds] (server) Received XenoPlantWeedsEvent from {ToPrettyString(ent)} at {coordinates} cfgProto={ent.Comp.Weedprototype}");

        // Prefer to check grid for duplication, but don’t hard-fail if we’re not on a grid.
        var hasGrid = coordinates.GetGridUid(EntityManager) is { } gridUid && TryComp(gridUid, out MapGridComponent? grid);

        // Prevent duplicate weeds on the same tile by checking anchored entities at the target tile.
        if (hasGrid)
        {
            var tile = _mapSystem.CoordinatesToTile(gridUid!.Value, grid!, coordinates);
            _anchored.Clear();
            _mapSystem.GetAnchoredEntities((gridUid!.Value, grid!), tile, _anchored);
            foreach (var anchored in _anchored)
            {
                if (HasComp<XenoWeedsComponent>(anchored))
                {
                    Log.Info("[XenoWeeds] (server) Skipping spawn; weeds already present at " + coordinates);
                    args.Handled = true;
                    return;
                }
            }
        }

        Log.Info($"[XenoWeeds] (server) Spawning weeds at {coordinates} for {ToPrettyString(ent)} using {ent.Comp.Weedprototype}");
        Spawn(ent.Comp.Weedprototype, coordinates);
        args.Handled = true;
    }

    private void OnWeedsActionRequest(RequestPerformActionEvent ev, EntitySessionEventArgs args)
    {
        // Only server handles this fallback
        if (!_net.IsServer)
            return;

        if (args.SenderSession.AttachedEntity is not { } performer)
            return;

        // Resolve the action entity and check it's the Plant Weeds action
        var actionEnt = GetEntity(ev.Action);
        if (!TryComp(actionEnt, out MetaDataComponent? meta))
            return;

        // Plant Weeds fallback
        if (meta.EntityPrototype?.ID == "ActionXenoPlantWeeds")
        {
            // Ensure performer is a xeno and get its weeds proto
            if (!TryComp(performer, out XenoComponent? xeno))
                return;

            var coordinates = _transform.GetMoverCoordinates(performer).SnapToGrid(EntityManager, _map);
            Log.Info("[XenoWeeds] (server) RequestPerformActionEvent fallback for " + ToPrettyString(performer) + " at " + coordinates);

            var hasGrid = coordinates.GetGridUid(EntityManager) is { } gridUid && TryComp(gridUid, out MapGridComponent? grid);
            if (hasGrid)
            {
                var tile = _mapSystem.CoordinatesToTile(gridUid!.Value, grid!, coordinates);
                _anchored.Clear();
                _mapSystem.GetAnchoredEntities((gridUid!.Value, grid!), tile, _anchored);
                foreach (var anchored in _anchored)
                {
                    if (HasComp<XenoWeedsComponent>(anchored))
                    {
                        Log.Info("[XenoWeeds] (server) Fallback skip; weeds already present at " + coordinates);
                        _actions.StartUseDelay(actionEnt);
                        return;
                    }
                }
            }

            Spawn(xeno.Weedprototype, coordinates);
            _actions.StartUseDelay(actionEnt);
            Log.Info("[XenoWeeds] (server) Fallback spawned weeds at " + coordinates + " using proto " + xeno.Weedprototype + ".");
            return;
        }

        // Choose Structure fallback: raise event on performer if action flags were wrong
        if (meta.EntityPrototype?.ID == "ActionXenoChooseStructure")
        {
            if (!TryComp(performer, out XenoComponent? _))
                return;

            var chooseEv = new Content.Shared.CM14.Xenos.Construction.Events.XenoChooseStructureActionEvent();
            Log.Info($"[XenoChooseStructure] (server) Fallback raising choose event on {ToPrettyString(performer)}");
            RaiseLocalEvent(performer, ref chooseEv);
            return;
        }

        // Secrete Structure fallback: raise event on performer with target coordinates
        if (meta.EntityPrototype?.ID == "ActionXenoSecreteStructure")
        {
            if (!TryComp(performer, out XenoComponent? _))
                return;

            if (ev.EntityCoordinatesTarget is not { } netCoords)
                return;

            var coords = Coordinates.FromMap(EntityManager, netCoords);
            var secretEv = new Content.Shared.CM14.Xenos.Construction.Events.XenoSecreteStructureEvent
            {
                Target = coords
            };
            Log.Info($"[XenoBuild] (server) Fallback raising secrete event on {ToPrettyString(performer)} at {coords}");
            RaiseLocalEvent(performer, ref secretEv);
            return;
        }

    }

    // Note: If HiveCoreComponent exists in this codebase, wire its MapInit here.
    //private void OnHiveCoreMapInit(Entity<HiveCoreComponent> ent, ref MapInitEvent args)
    //{
    //    var coordinates = _transform.GetMoverCoordinates(ent).SnapToGrid(EntityManager, _map);
    //    Spawn(ent.Comp.Spawns, coordinates);
    //}
    private void OnWeedsSpreadNeighbors(Entity<XenoWeedsComponent> ent, ref SpreadNeighborsEvent args)
    {
        var source = ent.Comp.IsSource ? ent.Owner : ent.Comp.Source;

        // TODO CM14
        // There is an edge case right now where existing weeds can block new weeds
        // from expanding further. If this is the case then the weeds should reassign
        // their source to this one and reactivate if it is closer to them than their
        // original source and only if it is still within range
        if (args.NeighborFreeTiles.Count <= 0 ||
            !Exists(source) ||
            !TryComp(source, out TransformComponent? transform) ||
            ent.Comp.Spawns.Id is not { } prototype)
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(ent);
            return;
        }

        var any = false;
        foreach (var neighbor in args.NeighborFreeTiles)
        {
            var gridOwner = neighbor.Grid.Owner;
            var coords = _mapSystem.GridTileToLocal(gridOwner, neighbor.Grid, neighbor.Tile);

            var sourceLocal = _mapSystem.CoordinatesToTile(gridOwner, neighbor.Grid, transform.Coordinates);
            var diff = Vector2.Abs(neighbor.Tile - sourceLocal);
            if (diff.X >= ent.Comp.Range || diff.Y >= ent.Comp.Range)
                continue;

            var neighborWeeds = Spawn(prototype, coords);
            var neighborWeedsComp = EnsureComp<XenoWeedsComponent>(neighborWeeds);

            neighborWeedsComp.IsSource = false;
            neighborWeedsComp.Source = source;

            EnsureComp<ActiveEdgeSpreaderComponent>(neighborWeeds);

            any = true;

            // Respect spread budget per tick
            args.Updates--;
            if (args.Updates <= 0)
                return;

            for (var i = 0; i < 4; i++)
            {
                var dir = (AtmosDirection)(1 << i);
                var pos = neighbor.Tile.GridIndices.Offset(dir);
                if (!_mapSystem.TryGetTileRef(gridOwner, neighbor.Grid, pos, out var adjacent))
                    continue;

                _anchored.Clear();
                _mapSystem.GetAnchoredEntities((gridOwner, neighbor.Grid), adjacent.GridIndices, _anchored);
                foreach (var anchored in _anchored)
                {
                    if (!TryComp(anchored, out XenoWeedableComponent? weedable) ||
                        weedable.Entity != null ||
                        !TryComp(anchored, out TransformComponent? weedableTransform) ||
                        !weedableTransform.Anchored)
                    {
                        continue;
                    }

                    weedable.Entity = SpawnAtPosition(weedable.Spawn, anchored.ToCoordinates());
                }
            }
        }

        if (!any)
            RemCompDeferred<ActiveEdgeSpreaderComponent>(ent);
    }

    private void OnWeedableAnchorStateChanged(Entity<XenoWeedableComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            QueueDel(ent.Comp.Entity);
    }
}
