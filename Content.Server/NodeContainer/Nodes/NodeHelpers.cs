using System.Diagnostics.CodeAnalysis;
using Content.Shared.NodeContainer;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.NodeContainer.Nodes
{
    /// <summary>
    ///     Helper utilities for implementing <see cref="Node"/>.
    /// </summary>
    public static class NodeHelpers
    {
        public static IEnumerable<Node> GetNodesInTile(EntityQuery<NodeContainerComponent> nodeQuery, MapGridComponent grid, Vector2i coords)
        {
            var mapSystem = EntitySystem.Get<SharedMapSystem>();
            #pragma warning disable CS0618
            foreach (var entityUid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords))
            #pragma warning restore CS0618
            {
                if (!nodeQuery.TryGetComponent(entityUid, out var container))
                    continue;

                foreach (var node in container.Nodes.Values)
                {
                    yield return node;
                }
            }
        }

        public static IEnumerable<(Direction dir, Node node)> GetCardinalNeighborNodes(
            EntityQuery<NodeContainerComponent> nodeQuery,
            MapGridComponent grid,
            Vector2i coords,
            bool includeSameTile = true)
        {
            foreach (var (dir, entityUid) in GetCardinalNeighborCells(grid, coords, includeSameTile))
            {
                if (!nodeQuery.TryGetComponent(entityUid, out var container))
                    continue;

                foreach (var node in container.Nodes.Values)
                {
                    yield return (dir, node);
                }
            }
        }

        [SuppressMessage("ReSharper", "EnforceForeachStatementBraces")]
        public static IEnumerable<(Direction dir, EntityUid entity)> GetCardinalNeighborCells(
            MapGridComponent grid,
            Vector2i coords,
            bool includeSameTile = true)
        {
            var mapSystem = EntitySystem.Get<SharedMapSystem>();
            if (includeSameTile)
            {
                #pragma warning disable CS0618
                foreach (var uid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords))
                    yield return (Direction.Invalid, uid);
                #pragma warning restore CS0618
            }

            #pragma warning disable CS0618
            foreach (var uid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords + (0, 1)))
                yield return (Direction.North, uid);

            foreach (var uid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords + (0, -1)))
                yield return (Direction.South, uid);

            foreach (var uid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords + (1, 0)))
                yield return (Direction.East, uid);

            foreach (var uid in mapSystem.GetAnchoredEntities(grid.Owner, grid, coords + (-1, 0)))
                yield return (Direction.West, uid);
            #pragma warning restore CS0618
        }
    }
}
