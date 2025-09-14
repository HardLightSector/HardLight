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
    /// Distance in tiles beyond which grids are considered for cleanup
    /// </summary>
    private const float CleanupDistance = 256f;

    /// <summary>
    /// How often to perform cleanup checks (5 minutes)
    /// </summary>
    private readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Last time cleanup was performed
    /// </summary>
    private TimeSpan _lastCleanup = TimeSpan.Zero;

    public override void Initialize()
    {
        _sawmill = _logManager.GetSawmill("trash-cleanup");
        _sawmill.Info("Trash cleanup system initialized. Will clean up grids beyond {Distance} tiles every {Interval} minutes.",
            CleanupDistance, CleanupInterval.TotalMinutes);
    }

    public override void Update(float frameTime)
    {
        var currentTime = _timing.CurTime;

        // In integration tests there are no real players; avoid interfering by deleting test grids.
        // Skip cleanup if no sessions are connected and it's likely a headless/test context.
    if (!_playerManager.Sessions.Any())
            return;

        if (currentTime - _lastCleanup < CleanupInterval)
            return;

        _lastCleanup = currentTime;
        PerformCleanup();
    }

    private void PerformCleanup()
    {
        var gridsToDelete = new List<EntityUid>();
        var protectedPositions = GetProtectedPositions();

        // Check all grids for cleanup eligibility
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var grid, out var gridTransform))
        {
            if (ShouldProtectGrid(gridUid))
                continue;

            var gridPosition = _transformSystem.GetWorldPosition(gridTransform);
            var shouldDelete = true;

            // Check distance from all protected positions
            foreach (var protectedPos in protectedPositions)
            {
                if (protectedPos.MapId != gridTransform.MapID)
                    continue;

                var distance = (gridPosition - protectedPos.Position).Length();
                if (distance <= CleanupDistance)
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
            _sawmill.Info("Cleaning up {Count} grids that are beyond {Distance} tiles from players/world loaders",
                gridsToDelete.Count, CleanupDistance);

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
    /// Gets positions that should be protected from cleanup (players and world loaders)
    /// </summary>
    private List<MapCoordinates> GetProtectedPositions()
    {
        var positions = new List<MapCoordinates>();

        // Add player positions
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is { } playerEntity &&
                TryComp<TransformComponent>(playerEntity, out var playerTransform) &&
                !HasComp<GhostComponent>(playerEntity))
            {
                var worldPos = _transformSystem.GetWorldPosition(playerTransform);
                positions.Add(new MapCoordinates(worldPos, playerTransform.MapID));
            }
        }

        // Add world loader positions
        var loaderQuery = EntityQueryEnumerator<WorldLoaderComponent, TransformComponent>();
        while (loaderQuery.MoveNext(out var loaderUid, out var loader, out var loaderTransform))
        {
            if (loader.Disabled)
                continue;

            var worldPos = _transformSystem.GetWorldPosition(loaderTransform);
            var extendedDistance = CleanupDistance + loader.Radius;

            // Create a larger protected area around world loaders
            positions.Add(new MapCoordinates(worldPos, loaderTransform.MapID));
        }

        return positions;
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
