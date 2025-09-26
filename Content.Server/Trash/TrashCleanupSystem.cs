using Content.Server.Station.Components;
using System.Linq;
using Content.Server.Worldgen.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Trash;

/// <summary>
/// System that periodically cleans up grids that are far away from players and world loaders.
/// This helps prevent the accumulation of abandoned grids that can impact server performance.
/// </summary>
public sealed class TrashCleanupSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Base distance in tiles beyond which grids are considered for cleanup relative to player positions.
    /// </summary>
    private const float PlayerCleanupDistance = 256f;

    /// <summary>
    /// Extra padding added to a world loader's own load radius to form a protection zone.
    /// This creates hysteresis so we don't immediately delete freshly spawned worldgen (e.g. asteroids)
    /// right as the loader skims the boundary.
    /// </summary>
    // Base hysteresis padding added to a world loader's load radius. Replaced by dynamic logic below but
    // kept as a minimum additive fallback so config tweaks can still lean on a constant if desired.
    private const float BaseLoaderProtectionPadding = 64f; // smaller base; dynamic scaling adds more.

    /// <summary>
    /// Minimum protection radius around world loaders. Ensures even tiny loader radii still protect a wider area.
    /// </summary>
    private const float MinLoaderProtectionRadius = PlayerCleanupDistance; // could be larger if desired

    /// <summary>
    /// How often to perform cleanup checks (5 minutes)
    /// </summary>
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Last time cleanup was performed
    /// </summary>
    private TimeSpan _lastCleanup = TimeSpan.Zero;

    public override void Initialize()
    {
        _sawmill = _logManager.GetSawmill("trash-cleanup");
        _sawmill.Info("Trash cleanup system initialized. Will clean up grids beyond player distance {PlayerDistance} tiles (base loader padding {LoaderPadding}) every {Interval} minutes.",
            PlayerCleanupDistance, BaseLoaderProtectionPadding, _cleanupInterval.TotalMinutes);
    }

    public override void Update(float frameTime)
    {
        var currentTime = _timing.CurTime;

        // In integration tests there are no real players; avoid interfering by deleting test grids.
        // Skip cleanup if no sessions are connected and it's likely a headless/test context.
        if (!_playerManager.Sessions.Any())
            return;

        if (currentTime - _lastCleanup < _cleanupInterval)
            return;

        _lastCleanup = currentTime;
        PerformCleanup();
    }

    private void PerformCleanup()
    {
        var gridsToDelete = new List<EntityUid>();
        var protectedZones = GetProtectedZones();

        // Check all grids for cleanup eligibility
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var grid, out var gridTransform))
        {
            if (ShouldProtectGrid(gridUid))
                continue;

            var gridPosition = _transformSystem.GetWorldPosition(gridTransform);
            var shouldDelete = true;

            // Check distance from all protected zones (players & loaders with individual radii)
            foreach (var zone in protectedZones)
            {
                if (zone.Position.MapId != gridTransform.MapID)
                    continue;

                var delta = gridPosition - zone.Position.Position;
                // LengthSquared() avoids sqrt each comparison
                if (delta.LengthSquared() <= zone.RadiusSquared)
                {
                    shouldDelete = false;
                    break;
                }
            }

            if (shouldDelete)
            {
                gridsToDelete.Add(gridUid);
            }
        }

        // Delete eligible grids
        if (gridsToDelete.Count > 0)
        {
            _sawmill.Info("Cleaning up {Count} grids that are outside all protection zones (player radius {PlayerDist}, base loader padding {LoaderPad}).",
                gridsToDelete.Count, PlayerCleanupDistance, BaseLoaderProtectionPadding);

            foreach (var gridUid in gridsToDelete)
            {
                var gridName = MetaData(gridUid).EntityName;
                _sawmill.Debug("Deleting grid: {GridName} ({GridUid})", gridName, gridUid);
                EntityManager.DeleteEntity(gridUid);
            }
        }
        else
        {
            _sawmill.Debug("No grids found for cleanup");
        }
    }

    /// <summary>
    /// Holds a protection zone (center + squared radius for fast comparisons).
    /// </summary>
    private readonly record struct ProtectionZone(MapCoordinates Position, float Radius, float RadiusSquared);

    /// <summary>
    /// Builds dynamic protection zones for both players and world loaders.
    /// Players get a fixed radius (PlayerCleanupDistance); loaders get their load radius plus padding.
    /// </summary>
    private List<ProtectionZone> GetProtectedZones()
    {
        var zones = new List<ProtectionZone>(_playerManager.Sessions.Count() + 8);

        // Player zones
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } playerEntity)
                continue;
            if (!TryComp<TransformComponent>(playerEntity, out var xform))
                continue;
            if (HasComp<GhostComponent>(playerEntity))
                continue;

            var worldPos = _transformSystem.GetWorldPosition(xform);
            var coords = new MapCoordinates(worldPos, xform.MapID);
            zones.Add(new ProtectionZone(coords, PlayerCleanupDistance, PlayerCleanupDistance * PlayerCleanupDistance));
        }

        // Loader zones
        var loaderQuery = EntityQueryEnumerator<WorldLoaderComponent, TransformComponent>();
        while (loaderQuery.MoveNext(out var loaderUid, out var loader, out var loaderTransform))
        {
            if (loader.Disabled)
                continue;

            var worldPos = _transformSystem.GetWorldPosition(loaderTransform);
            var coords = new MapCoordinates(worldPos, loaderTransform.MapID);
            // Protection radius: strictly greater than loader.Radius to create a hysteresis ring
            // where entities can spawn but won't immediately be considered for trash cleanup.
            var requested = loader.Radius + BaseLoaderProtectionPadding;

            // Safety: if padding got set to 0 or negative in future tweaks, still enforce at least +1 tile.
            if (requested <= loader.Radius)
                requested = loader.Radius + 1f;

            // Dynamic scaling: ensure protection extends at least one full world chunk beyond load radius,
            // and also at least 25% of the loader radius. This makes the ring size scale with how much the
            // loader is expected to pull in.
            // WorldGen.ChunkSize is large (e.g. 192); choose max(chunkSize, 0.25 * radius, BaseLoaderProtectionPadding).
            // MathF.Max has only 2-arg overloads; compute ternary max manually.
            var dynamicPad = MathF.Max(Worldgen.WorldGen.ChunkSize, loader.Radius * 0.25f);
            if (dynamicPad < BaseLoaderProtectionPadding)
                dynamicPad = BaseLoaderProtectionPadding;
            requested = loader.Radius + dynamicPad;
            var protectionRadius = MathF.Max(MinLoaderProtectionRadius, requested);
            zones.Add(new ProtectionZone(coords, protectionRadius, protectionRadius * protectionRadius));
        }

        return zones;
    }

    /// <summary>
    /// Determines if a grid should be protected from cleanup
    /// </summary>
    private bool ShouldProtectGrid(EntityUid gridUid)
    {
        // Protect station grids
        if (HasComp<StationMemberComponent>(gridUid))
            return true;

        // Protect grids with important components that indicate they shouldn't be deleted
        if (HasComp<WorldControllerComponent>(gridUid))
            return true;

        // Check if grid has any players on it
        var playerQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (playerQuery.MoveNext(out var actorUid, out var actor, out var actorTransform))
        {
            if (actorTransform.GridUid == gridUid && !HasComp<GhostComponent>(actorUid))
            {
                return true;
            }
        }

        return false;
    }
}
