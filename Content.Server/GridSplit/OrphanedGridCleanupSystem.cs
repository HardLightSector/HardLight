using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Procedural;
using Content.Server.Station.Components;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.GridSplit;

/// <summary>
/// Handles cleanup of orphaned/freefloating grids created from grid splits,
/// as well as periodic cleanup of empty/nameless grids that spawn during gameplay.
/// Small debris grids with no meaningful content are automatically deleted.
/// </summary>
public sealed class OrphanedGridCleanupSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    /// <summary>
    /// Minimum tile count for a grid to be considered worth keeping.
    /// Grids with fewer tiles than this will be deleted unless they have important entities.
    /// </summary>
    private int _minimumTileCount = 5;

    /// <summary>
    /// If true, enables automatic cleanup of orphaned grids from splits.
    /// </summary>
    private bool _enabled = true;

    /// <summary>
    /// If true, enables periodic cleanup of empty/nameless grids.
    /// </summary>
    private bool _emptyGridCleanupEnabled = true;

    /// <summary>
    /// How often (in seconds) to check for empty grids to clean up.
    /// </summary>
    private float _emptyGridCleanupInterval = 60f;

    /// <summary>
    /// Minimum age (in seconds) a grid must be before it can be cleaned up as empty.
    /// </summary>
    private float _emptyGridMinAge = 30f;

    /// <summary>
    /// Next time to run the empty grid cleanup.
    /// </summary>
    private TimeSpan _nextEmptyGridCleanup = TimeSpan.Zero;

    /// <summary>
    /// Tracks when grids were first seen for age-based cleanup.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _gridSpawnTimes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);

        // Register CVars
        _cfg.OnValueChanged(CCVars.OrphanedGridCleanupEnabled, v => _enabled = v, true);
        _cfg.OnValueChanged(CCVars.OrphanedGridMinimumTiles, v => _minimumTileCount = v, true);
        _cfg.OnValueChanged(CCVars.EmptyGridCleanupEnabled, v => _emptyGridCleanupEnabled = v, true);
        _cfg.OnValueChanged(CCVars.EmptyGridCleanupInterval, v => _emptyGridCleanupInterval = v, true);
        _cfg.OnValueChanged(CCVars.EmptyGridCleanupMinAge, v => _emptyGridMinAge = v, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _cfg.UnsubValueChanged(CCVars.OrphanedGridCleanupEnabled, v => _enabled = v);
        _cfg.UnsubValueChanged(CCVars.OrphanedGridMinimumTiles, v => _minimumTileCount = v);
        _cfg.UnsubValueChanged(CCVars.EmptyGridCleanupEnabled, v => _emptyGridCleanupEnabled = v);
        _cfg.UnsubValueChanged(CCVars.EmptyGridCleanupInterval, v => _emptyGridCleanupInterval = v);
        _cfg.UnsubValueChanged(CCVars.EmptyGridCleanupMinAge, v => _emptyGridMinAge = v);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_emptyGridCleanupEnabled)
            return;

        var curTime = _timing.CurTime;
        if (curTime < _nextEmptyGridCleanup)
            return;

        _nextEmptyGridCleanup = curTime + TimeSpan.FromSeconds(_emptyGridCleanupInterval);
        CleanupEmptyGrids(curTime);
    }

    /// <summary>
    /// Periodically scans for and cleans up empty/nameless grids.
    /// </summary>
    /// <returns>The number of grids queued for deletion.</returns>
    private int CleanupEmptyGrids(TimeSpan curTime)
    {
        var gridsToDelete = new List<EntityUid>();

        // Track which grids we've seen this cycle to prune stale entries
        var seenGrids = new HashSet<EntityUid>();

        var query = EntityQueryEnumerator<MapGridComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var meta, out var xform))
        {
            seenGrids.Add(uid);

            // Skip if not old enough
            if (!_gridSpawnTimes.TryGetValue(uid, out var spawnTime))
            {
                // Grid existed before we started tracking, give it a timestamp now
                _gridSpawnTimes[uid] = curTime;
                continue;
            }

            var age = (curTime - spawnTime).TotalSeconds;
            if (age < _emptyGridMinAge)
                continue;

            // Check if this grid should be cleaned up
            if (ShouldCleanupEmptyGrid(uid, grid, meta))
            {
                gridsToDelete.Add(uid);
            }
        }

        // Prune stale entries from grids that no longer exist
        var staleGrids = _gridSpawnTimes.Keys.Where(uid => !seenGrids.Contains(uid)).ToList();
        foreach (var staleUid in staleGrids)
        {
            _gridSpawnTimes.Remove(staleUid);
        }

        // Delete the collected grids
        foreach (var gridUid in gridsToDelete)
        {
            Log.Info($"Deleting empty/nameless grid {ToPrettyString(gridUid)} during periodic cleanup");
            QueueDel(gridUid);
            _gridSpawnTimes.Remove(gridUid);
        }

        if (gridsToDelete.Count > 0)
        {
            Log.Info($"Periodic empty grid cleanup deleted {gridsToDelete.Count} grid(s)");
        }

        return gridsToDelete.Count;
    }

    /// <summary>
    /// Determines if a grid should be cleaned up during periodic empty grid cleanup.
    /// </summary>
    private bool ShouldCleanupEmptyGrid(EntityUid gridUid, MapGridComponent grid, MetaDataComponent meta)
    {
        // Count tiles
        var tileCount = _mapSystem.GetAllTiles(gridUid, grid).Count();

        // If grid has zero tiles, it's definitely a candidate for cleanup
        if (tileCount == 0)
        {
            // But still check for important entities (like players stuck in space)
            if (HasImportantEntities(gridUid))
                return false;

            return true;
        }

        // Check for dungeon atlas template grids - these are pre-loaded templates
        // that should be cleaned up if they're empty or have no tiles
        // They're supposed to be deleted on round start but sometimes persist
        if (HasComp<DungeonAtlasTemplateComponent>(gridUid))
        {
            // Dungeon templates with few tiles and no important entities should be cleaned up
            if (tileCount < _minimumTileCount && !HasImportantEntities(gridUid))
                return true;
        }

        // Check for blank/empty name combined with small size
        var name = meta.EntityName;
        var isNameless = string.IsNullOrWhiteSpace(name) || name == "grid" || name == "Grid";

        // If nameless and small, treat as cleanup candidate
        if (isNameless && tileCount < _minimumTileCount)
        {
            if (HasImportantEntities(gridUid))
                return false;

            return true;
        }

        return false;
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
        var tileCount = _mapSystem.GetAllTiles(gridUid, grid).Count();

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
        // Use TryGetComponent for safety - if the grid is being deleted or lacks a transform,
        // we treat it as having no important entities (safe to delete)
        if (!xformQuery.TryGetComponent(gridUid, out var xform))
            return false;

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

    /// <summary>
    /// Enables or disables periodic empty grid cleanup.
    /// </summary>
    public void SetEmptyGridCleanupEnabled(bool enabled)
    {
        _emptyGridCleanupEnabled = enabled;
        Log.Info($"Empty grid cleanup {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Forces an immediate empty grid cleanup check.
    /// </summary>
    /// <returns>The number of grids queued for deletion.</returns>
    public int ForceEmptyGridCleanup()
    {
        return CleanupEmptyGrids(_timing.CurTime);
    }
}
