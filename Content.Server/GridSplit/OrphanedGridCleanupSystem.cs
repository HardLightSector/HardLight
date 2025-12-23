using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Station.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server.GridSplit;

/// <summary>
/// Handles cleanup of orphaned/freefloating grids created from grid splits.
/// Small debris grids with no meaningful content are automatically deleted.
/// </summary>
public sealed class OrphanedGridCleanupSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    /// <summary>
    /// Minimum tile count for a grid to be considered worth keeping.
    /// Grids with fewer tiles than this will be deleted unless they have important entities.
    /// </summary>
    private int _minimumTileCount = 5;

    /// <summary>
    /// If true, enables automatic cleanup of orphaned grids.
    /// </summary>
    private bool _enabled = true;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        
        // TODO: Add CVars for these when this system is finalized
        // _cfg.OnValueChanged(CCVars.OrphanedGridCleanupEnabled, v => _enabled = v, true);
        // _cfg.OnValueChanged(CCVars.OrphanedGridMinimumTiles, v => _minimumTileCount = v, true);
    }

    private void OnGridSplit(ref GridSplitEvent ev)
    {
        if (!_enabled)
            return;

        // Check each newly created grid to see if it should be deleted
        foreach (var newGridUid in ev.NewGrids)
        {
            if (!ShouldDeleteGrid(newGridUid))
                continue;

            Log.Info($"Deleting orphaned grid {ToPrettyString(newGridUid)} created from split of {ToPrettyString(ev.Grid)}");
            QueueDel(newGridUid);
        }
    }

    /// <summary>
    /// Determines if a grid should be deleted based on its size and contents.
    /// </summary>
    private bool ShouldDeleteGrid(EntityUid gridUid)
    {
        // Don't delete if it doesn't have a grid component
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        // Count total tiles by iterating through all tiles
        var mapSystem = EntityManager.System<SharedMapSystem>();
        var tileCount = mapSystem.GetAllTiles(gridUid, grid).Count();

        // If the grid is large enough, keep it regardless of contents
        if (tileCount >= _minimumTileCount)
            return false;

        // For small grids, check if they have any important entities
        if (HasImportantEntities(gridUid))
            return false;

        // Small grid with no important content - delete it
        return true;
    }

    /// <summary>
    /// Checks if a grid contains entities that are important enough to preserve the grid.
    /// </summary>
    private bool HasImportantEntities(EntityUid gridUid)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        
        // Get all entities on the grid
        var xform = xformQuery.GetComponent(gridUid);
        var children = xform.ChildEnumerator;
        
        while (children.MoveNext(out var child))
        {
            // Check for players
            if (HasComp<ActorComponent>(child))
                return true;

            // Check for mobs (NPCs, animals, etc.)
            if (HasComp<MobStateComponent>(child))
                return true;

            // Check for power producers (APCs, generators, etc.)
            if (HasComp<PowerSupplierComponent>(child))
                return true;

            // Check for power consumers that draw significant power (machinery)
            if (HasComp<PowerSupplierComponent>(child))
                return true;

            // Check for power consumers that draw significant power
            if (TryComp<PowerConsumerComponent>(child, out var consumer) && consumer.DrawRate > 100)
                return true;

            // Check for station membership (station grids should never be deleted)
            if (HasComp<StationMemberComponent>(child))
                return true;

            // Check for airlocks/doors (indicates structure worth preserving)
            if (HasComp<DoorComponent>(child))
                return true;

            // Check for valuable/complex machinery
            if (HasComp<ApcComponent>(child))
                return true;
        }

        return false;
    }


    /// <summary>
    /// Sets the minimum tile count threshold for grid cleanup.
    /// </summary>
    public void SetMinimumTileCount(int count)
    {
        _minimumTileCount = Math.Max(1, count);
        Log.Info($"Orphaned grid cleanup minimum tile count set to {_minimumTileCount}");
    }

    /// <summary>
    /// Manually checks and cleans up a specific grid if it meets the orphan criteria.
    /// </summary>
    public bool TryCleanupGrid(EntityUid gridUid)
    {
        if (!ShouldDeleteGrid(gridUid))
            return false;

        Log.Info($"Manually deleting orphaned grid {ToPrettyString(gridUid)}");
        QueueDel(gridUid);
        return true;
    }
    /// <summary>
    /// Enables or disables automatic grid cleanup.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Log.Info($"Orphaned grid cleanup {(enabled ? "enabled" : "disabled")}");
    }
}
