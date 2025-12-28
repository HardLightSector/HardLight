using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.Markdown.Sequence;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Content.Shared.Shuttles.Save;
using Robust.Shared.Network;
using Robust.Shared.Maths;
using System;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Robust.Shared.Log;
using Robust.Server.GameObjects;
using Content.Server.Atmos.Components;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Containers;
using Robust.Server.Physics;
using Content.Shared.Atmos;
using Robust.Shared.Timing;
using Robust.Shared.Console;
using Content.Shared.Decals;
using Content.Server.Decals;
using static Content.Shared.Decals.DecalGridComponent;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;
using Robust.Shared.Configuration;
using Content.Shared._NF.CCVar;
using Robust.Shared.Player;
using Robust.Server.Player;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Robust.Shared.Serialization.Markdown;
using Content.Shared.VendingMachines;
using Robust.Shared.EntitySerialization.Systems; // Added for MapLoaderSystem
using Robust.Shared.EntitySerialization;
using Content.Shared.Access.Components; // AccessReaderComponent for access retention
using Robust.Shared.Map.Events; // For BeforeEntityReadEvent

namespace Content.Server.Shuttles.Save
{
    public sealed class ShipSerializationSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ISerializationManager _serializationManager = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
        [Dependency] private readonly MapSystem _map = default!;
        [Dependency] private readonly IConsoleHost _consoleHost = default!;
        [Dependency] private readonly DecalSystem _decalSystem = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;
        [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!;
        [Dependency] private readonly IGameTiming _gameManager = default!;
        [Dependency] private readonly MapLoaderSystem _mapLoader = default!; // For refactored serializer path
        [Dependency] private readonly IDependencyCollection _dependencyCollection = default!; // Use same dependency collection as MapLoaderSystem
        // Note: For EntityDeserializer we use IoCManager.Instance directly to avoid extra injected fields.

        private ISawmill _sawmill = default!;

        private ISerializer _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private IDeserializer _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Alternate deserializer for legacy saves that used underscored field names (format_version, original_grid_id, grid_id, etc.)
        private IDeserializer _deserializerUnderscore = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        public override void Initialize()
        {
            base.Initialize();
            _sawmill = Logger.GetSawmill("ship-serialization");
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // Process any pending asynchronous ship load jobs in small batches per tick
            if (_shipLoadJobs.Count == 0)
                return;

            // Read CVars once per tick
            var enableAsync = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadAsync);
            if (!enableAsync)
                return;

            var batchNonContained = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadBatchNonContained);
            var batchContained = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadBatchContained);
            var timeBudgetMs = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadTimeBudgetMs);
            var logProgress = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadLogProgress);

            var now = _gameManager.CurTime;

            // Copy to avoid modification during iteration
            for (var i = _shipLoadJobs.Count - 1; i >= 0; i--)
            {
                var job = _shipLoadJobs[i];
                if (job.Complete || !_entityManager.EntityExists(job.GridOwner))
                {
                    _shipLoadJobs.RemoveAt(i);
                    continue;
                }

                var start = _gameManager.CurTime;
                var processed = 0;

                // Phase 1: non-contained entities
                if (job.NonContained.Count > 0)
                {
                    var toProcess = Math.Max(1, batchNonContained);
                    while (toProcess-- > 0 && job.NonContained.Count > 0)
                    {
                        var entityData = job.NonContained.Dequeue();
                        try
                        {
                            var coordinates = new EntityCoordinates(job.GridOwner, entityData.Position);
                            var newEntity = SpawnEntityWithComponents(entityData, coordinates, job.ClearDefaultsForContainers);
                            if (newEntity != null)
                            {
                                job.IdMap[entityData.EntityId] = newEntity.Value;
                                job.SpawnedNonContained++;
                            }
                            else
                            {
                                job.FailedNonContained++;
                            }
                        }
                        catch
                        {
                            job.FailedNonContained++;
                        }

                        processed++;
                        if ((_gameManager.CurTime - start).TotalMilliseconds >= timeBudgetMs)
                            break;
                    }
                }

                // Phase 2: contained entities (after phase 1 completes)
                if (job.NonContained.Count == 0 && job.Contained.Count > 0 && (_gameManager.CurTime - start).TotalMilliseconds < timeBudgetMs)
                {
                    var toProcess = Math.Max(1, batchContained);
                    while (toProcess-- > 0 && job.Contained.Count > 0)
                    {
                        var entityData = job.Contained.Dequeue();
                        try
                        {
                            var tempCoordinates = new EntityCoordinates(job.GridOwner, Vector2.Zero);
                            var containedEntity = SpawnEntityWithComponents(entityData, tempCoordinates, job.ClearDefaultsForContainers);

                            if (containedEntity != null)
                            {
                                job.IdMap[entityData.EntityId] = containedEntity.Value;

                                if (!string.IsNullOrEmpty(entityData.ParentContainerEntity) &&
                                    !string.IsNullOrEmpty(entityData.ContainerSlot) &&
                                    job.IdMap.TryGetValue(entityData.ParentContainerEntity, out var parentContainer))
                                {
                                    if (InsertIntoContainer(containedEntity.Value, parentContainer, entityData.ContainerSlot))
                                        job.SpawnedContained++;
                                    else
                                    {
                                        _entityManager.DeleteEntity(containedEntity.Value);
                                        job.IdMap.Remove(entityData.EntityId);
                                        job.FailedContained++;
                                    }
                                }
                                else
                                {
                                    _entityManager.DeleteEntity(containedEntity.Value);
                                    job.IdMap.Remove(entityData.EntityId);
                                    job.FailedContained++;
                                }
                            }
                            else
                            {
                                job.FailedContained++;
                            }
                        }
                        catch
                        {
                            job.FailedContained++;
                        }

                        processed++;
                        if ((_gameManager.CurTime - start).TotalMilliseconds >= timeBudgetMs)
                            break;
                    }
                }

                // If all entity work is done, optionally restore decals now
                if (job.NonContained.Count == 0 && job.Contained.Count == 0 && !job.Complete)
                {
                    if (job.DecalsPending && job.DecalChunkCollection != null)
                    {
                        var decalsRestored = 0;
                        foreach (var (chunkPos, chunk) in job.DecalChunkCollection.ChunkCollection)
                        {
                            foreach (var (decalId, decal) in chunk.Decals)
                            {
                                var decalCoords = new EntityCoordinates(job.GridOwner, decal.Coordinates);
                                if (_decalSystem.TryAddDecal(decal.Id, decalCoords, out _, decal.Color, decal.Angle, decal.ZIndex, decal.Cleanable))
                                    decalsRestored++;
                            }
                        }
                        if (logProgress)
                            _sawmill.Info($"[ShipLoad] Restored {decalsRestored} decals to grid {job.GridOwner}");
                    }

                    job.Complete = true;
                    if (logProgress)
                    {
                        _sawmill.Info($"[ShipLoad] Completed async ship load on grid {job.GridOwner}: non-contained {job.SpawnedNonContained}/{job.SpawnedNonContained + job.FailedNonContained}, contained {job.SpawnedContained}/{job.SpawnedContained + job.FailedContained}");
                    }
                }
                else if (logProgress && processed > 0)
                {
                    _sawmill.Debug($"[ShipLoad] Progress grid {job.GridOwner}: remaining(non-contained={job.NonContained.Count}, contained={job.Contained.Count})");
                }
            }
        }

        // Async ship load job tracking
        private sealed class ShipLoadJob
        {
            public EntityUid GridOwner;
            public Queue<EntityData> NonContained = new();
            public Queue<EntityData> Contained = new();
            public Dictionary<string, EntityUid> IdMap = new();
            public bool DecalsPending;
            public DecalGridChunkCollection? DecalChunkCollection;
            public int SpawnedNonContained;
            public int FailedNonContained;
            public int SpawnedContained;
            public int FailedContained;
            public bool Complete;
            // If true, when spawning container owners we clear their default contents
            // so we can reinsert saved contents. For legacy saves (no container data),
            // this must be false to preserve prototype defaults like electronics.
            public bool ClearDefaultsForContainers = true;
        }

        private readonly List<ShipLoadJob> _shipLoadJobs = new();

        public ShipGridData SerializeShip(EntityUid gridId, NetUserId playerId, string shipName)
        {
            // Feature flag: allow fallback to legacy path (currently the same method acts as legacy until refactor complete)
            var useLegacy = _configManager.GetCVar(Content.Shared.CCVar.CCVars.ShipyardUseLegacySerializer);
            if (!useLegacy)
            {
                return SerializeShipRefactored(gridId, playerId, shipName);
            }

            // Verbose flag for legacy path debug output
            var verbose = _configManager.GetCVar(Content.Shared.CCVar.CCVars.ShipyardSaveVerbose);
            var excludeVending = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ExcludeVendingInShipSave);

            if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var grid))
            {
                throw new ArgumentException($"Grid with ID {gridId} not found.");
            }

            // Validate grid isn't being deleted
            if (!_entityManager.EntityExists(gridId) || _entityManager.IsQueuedForDeletion(gridId))
            {
                throw new ArgumentException($"Grid with ID {gridId} is being deleted or doesn't exist.");
            }

            // Get grid transform to normalize rotation during save
            var gridTransform = _entityManager.GetComponent<TransformComponent>(gridId);
            var originalGridRotation = gridTransform.LocalRotation;

            try
            {
                // Temporarily set grid rotation to 0° for serialization
                if (originalGridRotation != Angle.Zero)
                {
                    // Downgraded from Info to Debug to avoid log spam during saves
                    if (verbose)
                        _sawmill.Debug($"Normalizing grid rotation from {originalGridRotation.Degrees:F2}° to 0° for save");
                    _transform.SetLocalRotation(gridId, Angle.Zero, gridTransform);
                }

                var shipGridData = new ShipGridData
                {
                    Metadata = new ShipMetadata
                    {
                        OriginalGridId = gridId.ToString(),
                        PlayerId = playerId.ToString(),
                        ShipName = shipName,
                        Timestamp = DateTime.UtcNow
                    }
                };

                var gridData = new GridData
                {
                    GridId = gridId.ToString()
                };

                // Proper tile serialization
                var tiles = _map.GetAllTiles(gridId, grid);
                foreach (var tile in tiles)
                {
                    var tileDef = _tileDefManager[tile.Tile.TypeId];
                    if (tileDef.ID == "Space") // Skip space tiles
                        continue;

                    gridData.Tiles.Add(new TileData
                    {
                        X = tile.GridIndices.X,
                        Y = tile.GridIndices.Y,
                        TileType = tileDef.ID
                    });
                }

                // Tiles serialized


                // Skip atmosphere serialization (TileAtmosphere non-serializable); fixgridatmos handles on load
                if (_entityManager.TryGetComponent<DecalGridComponent>(gridId, out var decalComponent))
                {
                    try
                    {
                        var decalNode = _serializationManager.WriteValue(decalComponent.ChunkCollection, notNullableOverride: true);
                        var decalYaml = _serializer.Serialize(decalNode);
                        gridData.DecalData = decalYaml;
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Error($"Failed to serialize decal data: {ex.Message}");
                    }
                }

                // Optimized entity serialization - uses faster grid child enumeration
                var serializedEntities = new HashSet<EntityUid>();

                // Use grid's child enumerator instead of global entity query for better performance
                var childEnumerator = gridTransform.ChildEnumerator;

                while (childEnumerator.MoveNext(out var childUid))
                {
                    // Validate entity exists and isn't deleted
                    if (!_entityManager.EntityExists(childUid) || _entityManager.IsQueuedForDeletion(childUid))
                        continue;

                    if (!_entityManager.TryGetComponent<TransformComponent>(childUid, out var childTransform))
                        continue;

                    // Anchored-only: skip loose/unanchored entities on the floor
                    if (!childTransform.Anchored)
                        continue;

                    var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(childUid);
                    var proto = meta?.EntityPrototype?.ID ?? string.Empty;

                    // Skip entities with empty or invalid prototypes
                    if (string.IsNullOrEmpty(proto))
                        continue;

                    // Serialize all entities with normalized grid rotation (0°)
                    // Skip vending machines to avoid lag (delete them during save) when enabled
                    if (excludeVending && _entityManager.HasComponent<VendingMachineComponent>(childUid))
                    {
                        // High-frequency per-entity log downgraded from Info to Debug
                        if (verbose)
                            _sawmill.Debug($"Skipping vending machine {proto} during serialization");
                        continue; // Skip vending machines entirely (effectively deletes them from save)
                    }

                    var entityData = SerializeEntity(childUid, childTransform, proto, gridId);
                    if (entityData != null)
                    {
                        gridData.Entities.Add(entityData);
                        serializedEntities.Add(childUid);
                    }
                }

                // Also serialize entities that are contained but might not be in the grid query
                SerializeContainedEntities(gridId, gridData, serializedEntities);

                // Validate container relationships before finalizing
                ValidateContainerRelationships(gridData);

                shipGridData.Grids.Add(gridData);

                // Check for overlapping entities at same coordinates AFTER serialization
                var positionGroups = gridData.Entities.GroupBy(e => new { e.Position.X, e.Position.Y }).Where(g => g.Count() > 1);
                if (positionGroups.Any())
                {
                    _sawmill.Warning($"Found {positionGroups.Count()} positions with overlapping entities during serialization:");
                    foreach (var group in positionGroups)
                    {
                        _sawmill.Warning($"  Position ({group.Key.X}, {group.Key.Y}): {string.Join(", ", group.Select(e => e.Prototype))}");
                    }
                }

                // Consolidated summary log (previously just a generic success line)
                _sawmill.Info($"Ship serialized successfully: {gridData.Entities.Count} entities, {gridData.Tiles.Count} tiles, decals={(gridData.DecalData != null)}");

                return shipGridData;
            }
            finally
            {
                // Always restore original grid rotation, even if serialization fails
                if (originalGridRotation != Angle.Zero)
                {
                    try
                    {
                        _transform.SetLocalRotation(gridId, originalGridRotation, gridTransform);
                        // Downgraded from Info to Debug to reduce routine save noise
                        if (verbose)
                            _sawmill.Debug($"Restored grid rotation to {originalGridRotation.Degrees:F2}°");
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Error($"Failed to restore grid rotation: {ex.Message}");
                    }
                }
            }
        }

        private ShipGridData SerializeShipRefactored(EntityUid gridId, NetUserId playerId, string shipName)
        {
            if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var grid))
                throw new ArgumentException($"Grid with ID {gridId} not found.");

            if (!_entityManager.EntityExists(gridId) || _entityManager.IsQueuedForDeletion(gridId))
                throw new ArgumentException($"Grid with ID {gridId} is being deleted or doesn't exist.");

            // CVar flags
            var verbose = _configManager.GetCVar(Content.Shared.CCVar.CCVars.ShipyardSaveVerbose);
            var excludeVending = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ExcludeVendingInShipSave);
            var progressInterval = _configManager.GetCVar(Content.Shared.CCVar.CCVars.ShipyardSaveProgressInterval);
            if (progressInterval < 0) progressInterval = 0;

            var gridTransform = _entityManager.GetComponent<TransformComponent>(gridId);
            var originalGridRotation = gridTransform.LocalRotation; // Preserve rotation; don't zero it out here.
            var skippedVending = 0;

            // Temporary filter hook: veto vending machines before serializer touches them.
            EntitySerializer.IsSerializableDelegate? veto = (Entity<MetaDataComponent> ent, ref bool serializable) =>
            {
                if (excludeVending && _entityManager.HasComponent<VendingMachineComponent>(ent))
                {
                    skippedVending++;
                    serializable = false;
                }
                // serializable remains true by default for other entities
            };

            MappingDataNode? entityDataNode = null;
            FileCategory category = FileCategory.Unknown;

            try
            {
                // Unlike legacy path we no longer forcibly normalize rotation here; we retain original so docking can align.
                if (verbose)
                    _sawmill.Debug($"[Refactored] Preserving grid rotation {originalGridRotation.Degrees:F2}° during serialization");

                // Attach filter
                _mapLoader.OnIsSerializable += veto;
                (entityDataNode, category) = _mapLoader.SerializeEntitiesRecursive(new HashSet<EntityUid> { gridId });
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Refactored serialization failed: {ex.Message}");
                throw;
            }
            finally
            {
                _mapLoader.OnIsSerializable -= veto;
                // No rotation mutation occurred; nothing to restore.
            }

            if (category != FileCategory.Grid || entityDataNode == null)
                throw new InvalidOperationException("Serializer did not return a grid category node");

            // Build ShipGridData wrapper
            var shipGridData = new ShipGridData
            {
                Metadata = new ShipMetadata
                {
                    OriginalGridId = gridId.ToString(),
                    PlayerId = playerId.ToString(),
                    ShipName = shipName,
                    Timestamp = DateTime.UtcNow,
                    OriginalGridRotation = (float)Math.Round(originalGridRotation.Theta, 3)
                }
            };

            var gridData = new GridData { GridId = gridId.ToString() };

            // Tiles (retain existing tile extraction logic for compatibility)
            var tiles = _map.GetAllTiles(gridId, grid);
            foreach (var tile in tiles)
            {
                var tileDef = _tileDefManager[tile.Tile.TypeId];
                if (tileDef.ID == "Space")
                    continue;
                gridData.Tiles.Add(new TileData
                {
                    X = tile.GridIndices.X,
                    Y = tile.GridIndices.Y,
                    TileType = tileDef.ID
                });
            }

            // Decals (unchanged)
            if (_entityManager.TryGetComponent<DecalGridComponent>(gridId, out var decalComponent))
            {
                try
                {
                    var decalNode = _serializationManager.WriteValue(decalComponent.ChunkCollection, notNullableOverride: true);
                    gridData.DecalData = _serializer.Serialize(decalNode);
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to serialize decal data (refactored): {ex.Message}");
                }
            }

            // Extract entities from engine recursive serializer output.
            // Format (MapFormat v6+) groups entities by prototype:
            // entities:
            // - proto: "PrototypeId"
            //   entities:
            //     - uid: 1
            //       components: [ { type: Transform, pos: X,Y, rot: <float> rad, parent: <uid|invalid> }, ... ]
            // We only need prototype, position, rotation. Component state intentionally omitted.
            var parsedEntityCount = 0;
            // Refactored path: enumerate live grid children to capture full component and container data,
            // while preserving original grid rotation. Apply anchored-only filtering and recurse into containers.
            var serializedEntities = new HashSet<EntityUid>();
            var childEnumerator = gridTransform.ChildEnumerator;
            while (childEnumerator.MoveNext(out var childUid))
            {
                if (!_entityManager.EntityExists(childUid) || _entityManager.IsQueuedForDeletion(childUid))
                    continue;
                if (!_entityManager.TryGetComponent<TransformComponent>(childUid, out var childTransform))
                    continue;
                // Anchored-only root entities
                if (!childTransform.Anchored)
                    continue;

                var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(childUid);
                var proto = meta?.EntityPrototype?.ID ?? string.Empty;
                if (string.IsNullOrEmpty(proto))
                    continue;
                if (excludeVending && _entityManager.HasComponent<VendingMachineComponent>(childUid))
                {
                    skippedVending++;
                    continue;
                }

                var entityData = SerializeEntity(childUid, childTransform, proto, gridId);
                if (entityData != null)
                {
                    gridData.Entities.Add(entityData);
                    serializedEntities.Add(childUid);
                }
            }

            // Include contained entities recursively
            SerializeContainedEntities(gridId, gridData, serializedEntities);

            // Validate container relationships
            ValidateContainerRelationships(gridData);

            shipGridData.Grids.Add(gridData);
            _sawmill.Info($"[Refactored] Ship serialized: {gridData.Entities.Count} entities, {gridData.Tiles.Count} tiles, decals={(gridData.DecalData != null)}, skippedVend={skippedVending}");
            return shipGridData;
        }

        public string SerializeShipGridDataToYaml(ShipGridData data)
        {
            return _serializer.Serialize(data);
        }

        public ShipGridData DeserializeShipGridDataFromYaml(string yamlString, Guid loadingPlayerId)
        {
            return DeserializeShipGridDataFromYaml(yamlString, loadingPlayerId, out _);
        }

        public ShipGridData DeserializeShipGridDataFromYaml(string yamlString, Guid loadingPlayerId, out bool wasLegacyConverted)
        {
            _sawmill.Info($"Deserializing ship YAML for player {loadingPlayerId}");
            wasLegacyConverted = false;
            ShipGridData data;
            try
            {
                // Preprocess legacy root key 'meta' -> 'metadata' if present at top level.
                // We only rewrite if we find 'meta:' before any non-whitespace / comment content for safety.
                var span = yamlString.AsSpan();
                // Quick heuristic: look for a line starting with 'meta:' (ignoring leading spaces) and also containing 'grids:' later.
                // Avoid false positives if 'metadata:' already exists.
                var hasMetadata = yamlString.Contains("metadata:");
                // Detect 'meta:' even if it's the very first line (no leading newline) or later.
                var hasLegacyMetaToken = !hasMetadata && (yamlString.StartsWith("meta:") || yamlString.Contains("\nmeta:"));
                if (hasLegacyMetaToken)
                {
                    // Replace only the first occurrence of a standalone 'meta:' at line start.
                    var lines = yamlString.Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("meta:") && !trimmed.StartsWith("metadata:"))
                        {
                            var leading = lines[i].Substring(0, lines[i].Length - trimmed.Length);
                            lines[i] = leading + trimmed.Replace("meta:", "metadata:");
                            wasLegacyConverted = true;
                            break;
                        }
                        // Stop searching if we reach another root key that would precede metadata logically.
                        if (trimmed.StartsWith("grids:"))
                            break;
                    }
                    if (wasLegacyConverted)
                        yamlString = string.Join('\n', lines);
                }

                // Additional legacy: some early saves used 'format:' instead of 'format_version:' inside metadata.
                // Perform a simple line-based replacement only within the metadata block if present.
                if (yamlString.Contains("format:") && !yamlString.Contains("format_version:"))
                {
                    // Very naive block scan: find line with 'metadata:' then replace first 'format:' encountered before 'grids:' root.
                    var lines = yamlString.Split('\n');
                    var inMetadata = false;
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var raw = lines[i];
                        var trimmed = raw.TrimStart();
                        if (!inMetadata)
                        {
                            if (trimmed.StartsWith("metadata:"))
                                inMetadata = true;
                        }
                        else
                        {
                            // End metadata block heuristically if we hit a root-level key (no indent) like 'grids:'
                            if (!char.IsWhiteSpace(raw, 0) && trimmed.StartsWith("grids:"))
                                break;
                            if (trimmed.StartsWith("format:") && !trimmed.StartsWith("format_version:"))
                            {
                                var leading = raw.Substring(0, raw.Length - trimmed.Length);
                                lines[i] = leading + trimmed.Replace("format:", "format_version:");
                                wasLegacyConverted = true;
                                break;
                            }
                        }
                    }
                    if (wasLegacyConverted)
                        yamlString = string.Join('\n', lines);
                }

                // Heuristic detection: legacy underscore style (old Robust serialization) vs new camelCase style (YamlDotNet default in this system)
                // If we see common underscored keys we attempt the underscore deserializer first.
                var looksUnderscore = yamlString.Contains("format_version:") ||
                                      yamlString.Contains("original_grid_id:") ||
                                      yamlString.Contains("grid_id:") ||
                                      yamlString.Contains("tile_type:") ||
                                      yamlString.Contains("entity_id:");

                Exception? firstEx = null;
                if (looksUnderscore)
                {
                    try
                    {
                        data = _deserializerUnderscore.Deserialize<ShipGridData>(yamlString);
                        _sawmill.Debug("[ShipLoad] Parsed YAML using underscore naming convention.");
                        return data;
                    }
                    catch (Exception exUnderscore)
                    {
                        firstEx = exUnderscore;
                        _sawmill.Debug($"[ShipLoad] Underscore deserializer failed ({exUnderscore.Message}); attempting camelCase fallback.");
                    }
                }

                // Fallback / primary path: camelCase
                try
                {
                    data = _deserializer.Deserialize<ShipGridData>(yamlString);
                }
                catch (Exception exCamel)
                {
                    // If we already tried underscore and it failed, surface original + camel errors for context.
                    if (firstEx != null)
                    {
                        _sawmill.Error($"YAML deserialization failed with both underscore ({firstEx.Message}) and camelCase ({exCamel.Message}) attempts.");
                    }
                    throw; // Original catch below will log again in unified format.
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"YAML deserialization failed: {ex.Message}");
                throw;
            }

            // Player ID check removed - anyone can load any ship file

            return data;
        }

        /// <summary>
        /// Fast-path loader for standard MapGrid YAML (engine-native). Returns the new grid UID or null on failure.
        /// </summary>
        public EntityUid? TryLoadStandardGridYaml(string yamlData, MapId map, System.Numerics.Vector2 offset)
        {
            try
            {
                using var textReader = new System.IO.StringReader(yamlData);
                var documents = DataNodeParser.ParseYamlStream(textReader).ToArray();
                if (documents.Length != 1)
                {
                    _sawmill.Error("Grid YAML should contain exactly one document.");
                    return null;
                }
                var data = (MappingDataNode)documents[0].Root;
                var opts = new MapLoadOptions
                {
                    MergeMap = map,
                    Offset = offset,
                    Rotation = Angle.Zero,
                    DeserializationOptions = DeserializationOptions.Default,
                    ExpectedCategory = FileCategory.Grid
                };

                var ev = new BeforeEntityReadEvent();
                RaiseLocalEvent(ev);
                opts.DeserializationOptions.AssignMapIds = opts.ForceMapId == null;
                if (opts.MergeMap is { } mergeTarget && !_map.MapExists(mergeTarget))
                    throw new Exception($"Target map {mergeTarget} does not exist");

                // Use injected dependency collection (mirrors MapLoaderSystem) instead of IoCManager.Instance to avoid missing registrations during map load.
                var deps = _dependencyCollection;
                if (deps == null)
                {
                    _sawmill.Error("Dependency collection was unexpectedly null during ship grid load.");
                    return null;
                }
                // Ensure MapSystem (server implementation of SharedMapSystem) is initialized; some early calls may occur
                // before normal system initialization ordering if invoked very early in round startup or test harness.
                if (!EntityManager.EntitySysManager.TryGetEntitySystem(typeof(MapSystem), out _))
                {
                    try
                    {
                        // Force creation - this mirrors how systems are normally lazily constructed.
                        EntityManager.EntitySysManager.GetEntitySystem<MapSystem>();
                        _sawmill.Debug("[ShipLoad] Lazily initialized MapSystem prior to EntityDeserializer creation.");
                    }
                    catch (Exception initEx)
                    {
                        _sawmill.Debug($"[ShipLoad] Failed to initialize MapSystem early: {initEx.Message}");
                    }
                }

                EntityDeserializer deserializer;
                var triedMapInit = false;
                while (true)
                {
                    try
                    {
                        deserializer = new EntityDeserializer(
                            deps!,
                            data,
                            opts.DeserializationOptions,
                            ev.RenamedPrototypes,
                            ev.DeletedPrototypes);
                        break; // success
                    }
                    catch (Robust.Shared.IoC.Exceptions.UnregisteredDependencyException ude)
                    {
                        // Specifically handle missing SharedMapSystem / MapSystem once.
                        if (!triedMapInit && ude.Message.Contains("SharedMapSystem"))
                        {
                            triedMapInit = true;
                            _sawmill.Debug("[ShipLoad] Retrying deserializer after attempting MapSystem init due to missing SharedMapSystem.");
                            try
                            {
                                EntityManager.EntitySysManager.GetEntitySystem<MapSystem>();
                            }
                            catch (Exception retryEx)
                            {
                                _sawmill.Debug($"[ShipLoad] MapSystem retry init failed: {retryEx.Message}");
                                return null;
                            }
                            continue; // retry loop
                        }
                        _sawmill.Debug($"Standard grid path aborted due to unregistered dependency: {ude.Message}");
                        return null;
                    }
                }

                if (!deserializer.TryProcessData())
                {
                    _sawmill.Error("Failed to process grid YAML data");
                    return null;
                }

                deserializer.CreateEntities();
                if (opts.ExpectedCategory is { } exp && exp != deserializer.Result.Category)
                {
                    _sawmill.Error($"YAML does not contain expected category {exp}");
                    _mapLoader.Delete(deserializer.Result);
                    return null;
                }

                var merged = new HashSet<EntityUid>();
                if (opts.MergeMap is { } targetId)
                {
                    // Get the map entity (MapSystem provides this)
                    if (!_map.TryGetMap(targetId, out var targetMapEnt))
                        throw new Exception($"Target map {targetId} does not exist (TryGetMap failed)");
                    deserializer.Result.Category = FileCategory.Unknown;
                    var rotation = opts.Rotation;
                    var matrix = Matrix3Helpers.CreateTransform(opts.Offset, rotation);
                    var target = new Entity<TransformComponent>(targetMapEnt!.Value, Transform(targetMapEnt.Value)); // targetMapEnt guaranteed non-null here

                    HashSet<EntityUid> maps = new();
                    HashSet<EntityUid> logged = new();
                    foreach (var uid in deserializer.Result.Entities)
                    {
                        var xform = Transform(uid);
                        var parent = xform.ParentUid;
                        if (parent == EntityUid.Invalid)
                            continue;
                        if (!HasComp<MapComponent>(parent))
                            continue;
                        if (HasComp<MapGridComponent>(parent) && logged.Add(parent))
                        {
                            _sawmill.Error("[ShipLoad] Standard YAML: merging a grid-map onto another map is not supported");
                            continue;
                        }
                        maps.Add(parent);
                        MergeEntityForStandardLoad(merged, uid, target, matrix, rotation);
                    }

                    deserializer.ToDelete.UnionWith(maps);
                    deserializer.Result.Maps.RemoveWhere(x => maps.Contains(x.Owner));

                    foreach (var orphan in deserializer.Result.Orphans)
                    {
                        MergeEntityForStandardLoad(merged, orphan, target, matrix, rotation);
                    }
                    deserializer.Result.Orphans.Clear();
                }
                else
                {
                    // Apply transform matrix to entities parented to map roots (non-merge path)
                    if (opts.Offset != System.Numerics.Vector2.Zero || opts.Rotation != Angle.Zero)
                    {
                        var matrix = Matrix3Helpers.CreateTransform(opts.Offset, opts.Rotation);
                        foreach (var uid in deserializer.Result.Entities)
                        {
                            var xform = Transform(uid);
                            var parent = xform.ParentUid; // EntityUid (non-nullable)
                            if (!parent.IsValid())
                                continue; // No parent

                            // Only adjust if parent is a map root (map entity without grid component)
                            if (!HasComp<MapComponent>(parent) || HasComp<MapGridComponent>(parent))
                                continue;

                            var rot = xform.LocalRotation + opts.Rotation;
                            var pos = System.Numerics.Vector2.Transform(xform.LocalPosition, matrix);
                            _transform.SetLocalPositionRotation(uid, pos, rot, xform);
                        }
                    }
                }

                deserializer.StartEntities();

                // Reconciliation: ensure all entities that reference the produced grid are parented properly
                if (deserializer.Result.Grids.Count == 1)
                {
                    var gridEnt = deserializer.Result.Grids.Single().Owner;
                    ReconcileChildParenting(gridEnt);
                    return gridEnt;
                }

                _mapLoader.Delete(deserializer.Result);
                return null;
            }
            catch (Exception ex)
            {
                _sawmill.Error($"TryLoadStandardGridYaml failed: {ex}");
                return null;
            }
        }

        private void MergeEntityForStandardLoad(HashSet<EntityUid> merged, EntityUid uid, Entity<TransformComponent> target, in Matrix3x2 matrix, Angle rotation)
        {
            merged.Add(uid);
            var xform = Transform(uid);
            var angle = xform.LocalRotation + rotation;
            var pos = System.Numerics.Vector2.Transform(xform.LocalPosition, matrix);
            var coords = new EntityCoordinates(target.Owner, pos);
            _transform.SetCoordinates((uid, xform, MetaData(uid)), coords, rotation: angle, newParent: target.Comp);
        }

        private void ReconcileChildParenting(EntityUid gridUid)
        {
            try
            {
                if (!TryComp<MapGridComponent>(gridUid, out _))
                    return;
                var fixedCount = 0;
                foreach (var uid in EntityManager.GetEntities())
                {
                    if (uid == gridUid) continue;
                    if (!TryComp<TransformComponent>(uid, out var xform)) continue;
                    if (xform.GridUid == gridUid && xform.ParentUid != gridUid)
                    {
                        // Reparent to grid root to ensure motion / FTL docking carries them.
                        var coords = new EntityCoordinates(gridUid, xform.LocalPosition);
                        _transform.SetCoordinates((uid, xform, MetaData(uid)), coords, newParent: Transform(gridUid));
                        fixedCount++;
                    }
                }
                if (fixedCount > 0)
                    _sawmill.Info($"[ShipLoad] Reconciled {fixedCount} entities to parent grid {gridUid}");
            }
            catch (Exception ex)
            {
                _sawmill.Warning($"[ShipLoad] ReconcileChildParenting exception: {ex.Message}");
            }
        }

        public EntityUid ReconstructShipOnMap(ShipGridData shipGridData, MapId targetMap, System.Numerics.Vector2 offset)
        {
            _sawmill.Info($"Reconstructing ship: {shipGridData.Grids.Count} grids, {shipGridData.Grids[0].Entities.Count} entities");
            if (shipGridData.Grids.Count == 0)
            {
                throw new ArgumentException("No grid data to reconstruct.");
            }

            var primaryGridData = shipGridData.Grids[0];
            // Primary grid entities
            // Primary grid tiles

            var newGrid = _mapManager.CreateGridEntity(targetMap);
            // Ensure the reconstructed grid participates in physics so docking can create an actual joint.
            // Without a PhysicsComponent on the grid, DockingSystem.Dock will set DockedWith but skip joint creation.
            // Ensure a PhysicsComponent exists so DockingSystem can create a weld joint between grids.
            _entityManager.EnsureComponent<Robust.Shared.Physics.Components.PhysicsComponent>(newGrid);
            // Created new grid

            // Note: Grid splitting prevention would require internal access
            // TODO: Investigate alternative approaches to prevent grid splitting

            // Move grid to the specified offset position
            var gridXform = Transform(newGrid);
            gridXform.WorldPosition = offset;
            // Apply original saved rotation (if any) before spawning entities/tiles so local positions remain valid.
            var savedRot = Angle.Zero;
            if (shipGridData.Metadata != null && Math.Abs(shipGridData.Metadata.OriginalGridRotation) > 0.0001f)
            {
                savedRot = new Angle(shipGridData.Metadata.OriginalGridRotation);
                gridXform.LocalRotation = savedRot;
                _sawmill.Info($"Applied saved grid rotation {savedRot.Degrees:F2}° to reconstructed ship prior to docking");
            }

            // Reconstruct tiles in connectivity order to prevent grid splitting
            var tilesToPlace = new List<(Vector2i coords, Tile tile)>();
            foreach (var tileData in primaryGridData.Tiles)
            {
                if (string.IsNullOrEmpty(tileData.TileType) || tileData.TileType == "Space")
                    continue;

                try
                {
                    var tileDef = _tileDefManager[tileData.TileType];
                    var tile = new Tile(tileDef.TileId);
                    var tileCoords = new Vector2i(tileData.X, tileData.Y);
                    tilesToPlace.Add((tileCoords, tile));
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to prepare tile {tileData.TileType} at ({tileData.X}, {tileData.Y}): {ex.Message}");
                }
            }

            // Sort tiles by connectivity using flood-fill to prevent grid splitting
            if (tilesToPlace.Any())
            {
                tilesToPlace = SortTilesForConnectivity(tilesToPlace);
            }

            // Place tiles maintaining connectivity
            foreach (var (coords, tile) in tilesToPlace)
            {
                _map.SetTile(newGrid.Owner, newGrid, coords, tile);
            }
            // Placed tiles

            // Apply fixgridatmos-style atmosphere to all loaded ships
            // Applying atmosphere
            ApplyFixGridAtmosphereToGrid(newGrid.Owner);

            // Restore decal data (optional via CVar)
            var loadDecals = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadDecals);
            if (loadDecals && !string.IsNullOrEmpty(primaryGridData.DecalData))
            {
                try
                {
                    var decalChunkCollection = _deserializer.Deserialize<DecalGridChunkCollection>(primaryGridData.DecalData);
                    // Ensure the grid has a DecalGridComponent
                    _entityManager.EnsureComponent<DecalGridComponent>(newGrid.Owner);

                    // If async loading enabled, defer decals to the end of entity spawn phases to reduce spikes
                    if (_configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadAsync))
                    {
                        _shipLoadJobs.Add(new ShipLoadJob
                        {
                            GridOwner = newGrid.Owner,
                            NonContained = new Queue<EntityData>(), // filled later below
                            Contained = new Queue<EntityData>(),
                            IdMap = new Dictionary<string, EntityUid>(),
                            DecalsPending = true,
                            DecalChunkCollection = decalChunkCollection
                        });
                        // We'll merge entity queues into this job below.
                    }
                    else
                    {
                        var decalsRestored = 0;
                        var decalsFailed = 0;
                        foreach (var (chunkPos, chunk) in decalChunkCollection.ChunkCollection)
                        {
                            foreach (var (decalId, decal) in chunk.Decals)
                            {
                                var decalCoords = new EntityCoordinates(newGrid.Owner, decal.Coordinates);
                                if (_decalSystem.TryAddDecal(decal.Id, decalCoords, out _, decal.Color, decal.Angle, decal.ZIndex, decal.Cleanable))
                                    decalsRestored++;
                                else
                                    decalsFailed++;
                            }
                        }
                        _sawmill.Info($"Restored {decalsRestored} decals from {decalChunkCollection.ChunkCollection.Count} chunks");
                        if (decalsFailed > 0)
                            _sawmill.Warning($"Failed to restore {decalsFailed} decals");
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to restore decal data: {ex.Message}");
                }
            }
            else
            {
                // No decal data found
            }

            // Two-phase entity reconstruction to handle containers properly
            // Starting two-phase entity reconstruction

            var entityIdMapping = new Dictionary<string, EntityUid>();
            var spawnedEntities = new List<(EntityUid entity, string prototype, Vector2 position)>();

            // Check if this is a legacy save without container data
            var hasContainerData = primaryGridData.Entities.Any(e => e.IsContainer || e.IsContained);
            if (!hasContainerData)
            {
                var enableAsync = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadAsync);
                _sawmill.Info("Legacy save detected - no container data found");
                if (enableAsync)
                {
                    var job = new ShipLoadJob { GridOwner = newGrid.Owner, ClearDefaultsForContainers = false };
                    foreach (var entity in primaryGridData.Entities)
                    {
                        if (string.IsNullOrEmpty(entity.Prototype))
                            continue;
                        job.NonContained.Enqueue(entity);
                    }
                    // If there was a decal job created above for this grid, merge into it instead
                    var existing = _shipLoadJobs.Find(j => j.GridOwner == newGrid.Owner);
                    if (existing != null)
                    {
                        while (job.NonContained.Count > 0)
                            existing.NonContained.Enqueue(job.NonContained.Dequeue());
                    }
                    else
                    {
                        _shipLoadJobs.Add(job);
                    }
                    _sawmill.Info($"[ShipLoad] Queued async legacy ship load on grid {newGrid.Owner} with {job.NonContained.Count} entities");
                    return newGrid.Owner;
                }
                else
                {
                    // In legacy mode we must not clear container defaults (electronics/boards)
                    ReconstructEntitiesLegacyMode(primaryGridData, newGrid, entityIdMapping, clearDefaults: false);
                    return newGrid.Owner;
                }
            }

            // Phase 1: Spawn all non-contained entities (containers, infrastructure, furniture)
            // Pre-filter entities into separate lists in a single pass
            var nonContainedEntities = new List<EntityData>();
            var containedEntitiesList = new List<EntityData>();

            foreach (var entity in primaryGridData.Entities)
            {
                if (string.IsNullOrEmpty(entity.Prototype))
                    continue;

                if (entity.IsContained)
                    containedEntitiesList.Add(entity);
                else
                    nonContainedEntities.Add(entity);
            }

            var asyncEnabled = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ShipLoadAsync);
            if (asyncEnabled)
            {
                // Queue async job for phased entity spawning
                var job = _shipLoadJobs.Find(j => j.GridOwner == newGrid.Owner);
                if (job == null)
                {
                    job = new ShipLoadJob { GridOwner = newGrid.Owner, ClearDefaultsForContainers = true };
                    _shipLoadJobs.Add(job);
                }
                foreach (var e in nonContainedEntities)
                {
                    job.NonContained.Enqueue(e);
                }
            }
            else
            {
                foreach (var entityData in nonContainedEntities)
                {
                    try
                    {
                        var coordinates = new EntityCoordinates(newGrid.Owner, entityData.Position);
                        // In standard sync load we clear defaults so saved container contents can be re-inserted
                        var newEntity = SpawnEntityWithComponents(entityData, coordinates, clearDefaultsForContainers: true);
                        if (newEntity != null)
                        {
                            entityIdMapping[entityData.EntityId] = newEntity.Value;
                            spawnedEntities.Add((newEntity.Value, entityData.Prototype, entityData.Position));
                        }
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Error($"Failed to spawn entity {entityData.Prototype}: {ex.Message}");
                    }
                }
            }

            // Phase 1 complete

            // Phase 2: Spawn contained entities and insert them into containers
            // Phase 2: Spawning contained entities
            var containedSpawned = 0;
            var containedFailed = 0;

            if (asyncEnabled)
            {
                var job = _shipLoadJobs.Find(j => j.GridOwner == newGrid.Owner)!;
                foreach (var e in containedEntitiesList)
                    job.Contained.Enqueue(e);
            }
            else
            {
                foreach (var entityData in containedEntitiesList)
                {
                    try
                    {
                        var tempCoordinates = new EntityCoordinates(newGrid.Owner, Vector2.Zero);
                        // In standard sync load we clear defaults for containers; legacy path handles false separately
                        var containedEntity = SpawnEntityWithComponents(entityData, tempCoordinates, clearDefaultsForContainers: true);
                        if (containedEntity != null)
                        {
                            entityIdMapping[entityData.EntityId] = containedEntity.Value;
                            if (!string.IsNullOrEmpty(entityData.ParentContainerEntity) &&
                                !string.IsNullOrEmpty(entityData.ContainerSlot) &&
                                entityIdMapping.TryGetValue(entityData.ParentContainerEntity, out var parentContainer))
                            {
                                if (InsertIntoContainer(containedEntity.Value, parentContainer, entityData.ContainerSlot))
                                    containedSpawned++;
                                else
                                {
                                    _entityManager.DeleteEntity(containedEntity.Value);
                                    entityIdMapping.Remove(entityData.EntityId);
                                    containedFailed++;
                                }
                            }
                            else
                            {
                                _entityManager.DeleteEntity(containedEntity.Value);
                                entityIdMapping.Remove(entityData.EntityId);
                                containedFailed++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Error($"Phase 2: Failed to spawn contained entity {entityData.Prototype}: {ex.Message}");
                        containedFailed++;
                    }
                }
            }

            // Phase 2 complete (sync path). For async we already returned after queuing.

            if (!asyncEnabled)
            {
                if (containedFailed > 0)
                    _sawmill.Warning($"{containedFailed} contained entities could not be properly placed");
                return newGrid.Owner;
            }

            return newGrid.Owner;
        }

        public EntityUid ReconstructShip(ShipGridData shipGridData)
        {
            _sawmill.Info($"Reconstructing ship with {shipGridData.Grids.Count} grids");
            if (shipGridData.Grids.Count == 0)
            {
                throw new ArgumentException("No grid data to reconstruct.");
            }

            var primaryGridData = shipGridData.Grids[0];
            // Primary grid entities

            // Note: Grid splitting prevention would require internal access
            // TODO: Investigate alternative approaches to prevent grid splitting

            // Create a new map for the ship instead of using MapId.Nullspace
            _map.CreateMap(out var mapId);
            _sawmill.Info($"Created new map {mapId}");

            // Create grid entity and get its MapGridComponent without using the obsolete Component.Owner
            var newGridEntity = _mapManager.CreateGridEntity(mapId);
            var newGrid = _entityManager.GetComponent<MapGridComponent>(newGridEntity);
            // Ensure the new grid has physics so any subsequent docking attaches a weld joint properly.
            _entityManager.EnsureComponent<Robust.Shared.Physics.Components.PhysicsComponent>(newGridEntity);
            _sawmill.Info($"Created new grid {newGridEntity} on map {mapId}");

            // Reconstruct tiles in connectivity order to prevent grid splitting
            var tilesToPlace = new List<(Vector2i coords, Tile tile)>();
            foreach (var tileData in primaryGridData.Tiles)
            {
                if (string.IsNullOrEmpty(tileData.TileType) || tileData.TileType == "Space")
                    continue;

                try
                {
                    var tileDef = _tileDefManager[tileData.TileType];
                    var tile = new Tile(tileDef.TileId);
                    var tileCoords = new Vector2i(tileData.X, tileData.Y);
                    tilesToPlace.Add((tileCoords, tile));
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to prepare tile {tileData.TileType} at ({tileData.X}, {tileData.Y}): {ex.Message}");
                }
            }

            // Sort tiles by connectivity using flood-fill to prevent grid splitting
            if (tilesToPlace.Any())
            {
                tilesToPlace = SortTilesForConnectivity(tilesToPlace);
            }

            // Place tiles maintaining connectivity
                foreach (var (coords, tile) in tilesToPlace)
                {
                    _map.SetTile(newGridEntity, newGrid, coords, tile);
                }
            // Placed tiles

            // Apply fixgridatmos-style atmosphere to all loaded ships
            // Applying atmosphere
            ApplyFixGridAtmosphereToGrid(newGridEntity);

            // Restore decal data using proper DecalSystem API
            if (!string.IsNullOrEmpty(primaryGridData.DecalData))
            {
                try
                {
                    var decalChunkCollection = _deserializer.Deserialize<DecalGridChunkCollection>(primaryGridData.DecalData);
                    var decalsRestored = 0;
                    var decalsFailed = 0;

                    // Ensure the grid has a DecalGridComponent
                    _entityManager.EnsureComponent<DecalGridComponent>(newGridEntity);

                    foreach (var (chunkPos, chunk) in decalChunkCollection.ChunkCollection)
                    {
                        foreach (var (decalId, decal) in chunk.Decals)
                        {
                            // Convert the decal coordinates to EntityCoordinates on the new grid
                                var decalCoords = new EntityCoordinates(newGridEntity, decal.Coordinates);

                            // Use the DecalSystem to properly add the decal
                            if (_decalSystem.TryAddDecal(decal.Id, decalCoords, out _, decal.Color, decal.Angle, decal.ZIndex, decal.Cleanable))
                            {
                                decalsRestored++;
                            }
                            else
                            {
                                decalsFailed++;
                                _sawmill.Warning($"Failed to restore decal {decal.Id} at {decal.Coordinates}");
                            }
                        }
                    }

                    _sawmill.Info($"Restored {decalsRestored} decals from {decalChunkCollection.ChunkCollection.Count} chunks");
                    if (decalsFailed > 0)
                    {
                        _sawmill.Warning($"Failed to restore {decalsFailed} decals");
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to restore decal data: {ex.Message}");
                }
            }
            else
            {
                // No decal data found
            }

            // Reconstruct entities with component restoration
            foreach (var entityData in primaryGridData.Entities)
            {
                // Skip entities with empty or null prototypes
                if (string.IsNullOrEmpty(entityData.Prototype))
                {
                    _sawmill.Debug($"Skipping entity with empty prototype at {entityData.Position}");
                    continue;
                }

                try
                {
                    var coordinates = new EntityCoordinates(newGridEntity, entityData.Position);
                    var newEntity = _entityManager.SpawnEntity(entityData.Prototype, coordinates);

                    // Apply rotation if it exists
                    if (Math.Abs(entityData.Rotation) > 0.001f)
                    {
                        var transform = _entityManager.GetComponent<TransformComponent>(newEntity);
                        transform.LocalRotation = new Angle(entityData.Rotation);
                    }


                    _sawmill.Debug($"Spawned entity {newEntity} ({entityData.Prototype}) at {entityData.Position}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to spawn entity {entityData.Prototype}: {ex.Message}");
                    throw;
                }
            }

            return newGridEntity;
        }

        private bool IsEntityContained(EntityUid entityUid, EntityUid gridId)
        {
            // Check if this entity is contained within another entity (not directly on the grid)
            if (_entityManager.TryGetComponent<TransformComponent>(entityUid, out var transformComp))
            {
                var parent = transformComp.ParentUid;

                // Direct grid children should always be included (pipes, cables, fixtures, etc.)
                if (parent == gridId)
                {
                    return false;
                }

                while (parent.IsValid() && parent != gridId)
                {
                    // Get the entity prototype to make smarter containment decisions
                    var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(parent);
                    var parentProto = meta?.EntityPrototype?.ID ?? string.Empty;

                    // Allow infrastructure entities even if they have complex hierarchies
                    if (parentProto.Contains("Pipe") || parentProto.Contains("Cable") ||
                        parentProto.Contains("Conduit") || parentProto.Contains("Atmos") ||
                        parentProto.Contains("Wire") || parentProto.Contains("Junction"))
                    {
                        return false;
                    }

                    // If we find a parent that has a ContainerManagerComponent, this entity is contained
                    // BUT exclude certain infrastructure containers that should be serialized
                    if (_entityManager.HasComponent<ContainerManagerComponent>(parent))
                    {
                        // Allow entities in certain "infrastructure" containers
                        if (parentProto.Contains("Pipe") || parentProto.Contains("Machine") ||
                            parentProto.Contains("Console") || parentProto.Contains("Computer"))
                        {
                            return false;
                        }
                        return true;
                    }

                    // Move up the hierarchy
                    if (_entityManager.TryGetComponent<TransformComponent>(parent, out var parentTransform))
                        parent = parentTransform.ParentUid;
                    else
                        break;
                }
            }
            return false;
        }

        private void ApplyFixGridAtmosphereToGrid(EntityUid gridUid)
        {
            // Execute fixgridatmos console command after a short delay to allow atmosphere system to initialize
            Timer.Spawn(TimeSpan.FromMilliseconds(100), () =>
            {
                if (!_entityManager.EntityExists(gridUid))
                {
                    _sawmill.Error($"Grid {gridUid} no longer exists for atmosphere application");
                    return;
                }

                var netEntity = _entityManager.GetNetEntity(gridUid);
                var commandArgs = $"fixgridatmos {netEntity}";
                _sawmill.Info($"Running fixgridatmos command: {commandArgs}");

                try
                {
                    _consoleHost.ExecuteCommand(null, commandArgs);
                    _sawmill.Info($"Successfully executed fixgridatmos for grid {gridUid}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to execute fixgridatmos command: {ex.Message}");
                }
            });
        }

        public string GetConvertedLegacyShipYaml(ShipGridData shipData, string playerName, string originalYamlString)
        {
            try
            {
                _sawmill.Info($"Generating converted YAML for legacy ship file for player {playerName}");

                // Serialize the updated ship data
                var convertedYamlString = SerializeShipGridDataToYaml(shipData);

                _sawmill.Info($"Ship '{shipData.Metadata.ShipName}' converted successfully");

                return convertedYamlString;
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to generate converted ship YAML for player {playerName}: {ex.Message}");
                return string.Empty;
            }
        }

        private List<ComponentData> SerializeEntityComponents(EntityUid entityUid)
        {
            var componentDataList = new List<ComponentData>();

            try
            {
                // Get all components on the entity
                var metaData = _entityManager.GetComponent<MetaDataComponent>(entityUid);
                foreach (var component in _entityManager.GetComponents(entityUid))
                {
                    // Skip certain components that shouldn't be serialized
                    var componentType = component.GetType();
                    if (ShouldSkipComponent(componentType))
                        continue;

                    try
                    {
                        var componentData = SerializeComponent(component);
                        if (componentData != null)
                            componentDataList.Add(componentData);
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Warning($"Failed to serialize component {componentType.Name} on entity {entityUid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to serialize components for entity {entityUid}: {ex.Message}");
            }

            return componentDataList;
        }

        private ComponentData? SerializeComponent(IComponent component)
        {
            try
            {
                var componentType = component.GetType();
                var typeName = componentType.Name;

                // Filter out problematic component types that shouldn't be serialized
                if (IsProblematicComponent(componentType))
                {
                    // Skipping problematic component
                    return null;
                }

                // Special handling for solution components to ensure chemical preservation
                if (component is SolutionContainerManagerComponent solutionManager)
                {
                    return SerializeSolutionComponent(solutionManager);
                }

                // Skip paper components - causes loading lag
                if (component is Content.Shared.Paper.PaperComponent)
                {
                    return null;
                }

                // Use RobustToolbox's serialization system to serialize the component
                var node = _serializationManager.WriteValue(componentType, component, notNullableOverride: true);
                var yamlData = _serializer.Serialize(node);

                var componentData = new ComponentData
                {
                    Type = typeName,
                    YamlData = yamlData,
                    NetId = 0 // NetID not available in this context
                };

                // Log important component preservation
                if (IsImportantComponent(componentType))
                {
                    // Preserved important component
                }

                return componentData;
            }
            catch (Exception ex)
            {
                var componentType = component.GetType();

                // Don't log warnings for expected problematic components
                if (IsProblematicComponent(componentType))
                {
                    // Reduce noise - only log at debug level for expected failures
                    // Expected serialization failure
                }
                else if (IsImportantComponent(componentType))
                {
                    // Only warn for important components that fail
                    _sawmill.Warning($"Failed to serialize important component {componentType.Name}: {ex.Message}");
                }
                else
                {
                    // Less important components - just debug
                    // Failed to serialize component
                }
                return null;
            }
        }

        private bool IsProblematicComponent(Type componentType)
        {
            var typeName = componentType.Name;

            // Network/client-side components that cause serialization issues
            if (typeName.Contains("Network") || typeName.Contains("Client") || typeName.Contains("Ui"))
                return true;

            // Timing/temporary components that shouldn't be preserved
            if (typeName.Contains("Timer") || typeName.Contains("Temporary") || typeName.Contains("Transient"))
                return true;

            // Event/notification components
            if (typeName.Contains("Event") || typeName.Contains("Alert") || typeName.Contains("Notification"))
                return true;

            // Runtime/generated components
            if (typeName.Contains("Runtime") || typeName.Contains("Generated") || typeName.Contains("Dynamic"))
                return true;

            // Known problematic component types from logs
            var problematicTypes = new[]
            {
                // Client / mind / player / UI heavy
                "ActionsComponent", "ItemSlotsComponent", "InventoryComponent", "SlotManagerComponent",
                "HandsComponent", "BodyComponent", "PlayerInputMoverComponent", "GhostComponent",
                "MindComponent", "MovementSpeedModifierComponent", "InputMoverComponent",
                "ActorComponent", "StatusEffectsComponent", "BloodstreamComponent",
                // Physics fixtures get rebuilt
                "FixtureComponent",
                // Low-value visuals or radio UI-only bits
                "RadioComponent", "InteractionOutlineComponent",
                // Scan-only
                "SolutionScannerComponent",
                // Safety: still skip vending machines entirely if flagged elsewhere
                "VendingMachineComponent"
            };

            return problematicTypes.Contains(typeName);
        }

        private ComponentData? SerializeSolutionComponent(SolutionContainerManagerComponent solutionManager)
        {
            try
            {
                // Create a simplified representation of the solution data for better preservation
                var solutionData = new Dictionary<string, object>();

                foreach (var (solutionName, solution) in solutionManager.Solutions ?? new Dictionary<string, Solution>())
                {
                    var solutionInfo = new Dictionary<string, object>
                    {
                        ["Volume"] = solution.Volume,
                        ["MaxVolume"] = solution.MaxVolume,
                        ["Temperature"] = solution.Temperature,
                        ["Reagents"] = solution.Contents?.ToDictionary(
                            reagent => reagent.Reagent.Prototype,
                            reagent => (object)reagent.Quantity
                        ) ?? new Dictionary<string, object>()
                    };
                    solutionData[solutionName] = solutionInfo;
                }

                var componentData = new ComponentData
                {
                    Type = "SolutionContainerManagerComponent",
                    Properties = solutionData,
                    NetId = 0 // NetID not available in this context
                };

                // Preserved solution component
                return componentData;
            }
            catch (Exception ex)
            {
                _sawmill.Warning($"Failed to serialize solution component: {ex.Message}");
                return null;
            }
        }


        private static readonly Dictionary<Type, bool> ComponentSkipCache = new();

        private bool ShouldSkipComponent(Type componentType)
        {
            if (ComponentSkipCache.TryGetValue(componentType, out var cached))
                return cached;

            var typeName = componentType.Name;

            var shouldSkip = false;

            // Skip transform components (position handled separately)
            if (typeName == "TransformComponent")
                shouldSkip = true;

            // Skip metadata components (handled separately)
            else if (typeName == "MetaDataComponent")
                shouldSkip = true;

            // Skip physics components (usually regenerated)
            else if (typeName.Contains("Physics"))
                shouldSkip = true;

            // Skip appearance/visual components (usually regenerated)
            else if (typeName.Contains("Appearance") || typeName.Contains("Sprite"))
                shouldSkip = true;

            // Skip network/client-side components
            else if (typeName.Contains("Eye") || typeName.Contains("Input") || typeName.Contains("UserInterface"))
                shouldSkip = true;

            ComponentSkipCache[componentType] = shouldSkip;
            return shouldSkip;
        }

        private bool IsImportantComponent(Type componentType)
        {
            var typeName = componentType.Name;

            // Chemical/solution components - high priority for preservation
            if (typeName.Contains("Solution") || typeName.Contains("Chemical"))
                return true;

            // Book and text components (paper no longer preserved)
            if (typeName.Contains("Book") || typeName.Contains("SignComponent"))
                return true;

            // Storage and container components
            if (typeName.Contains("Storage") || typeName.Contains("Container"))
                return true;

            // Seed and plant components (GMO preservation)
            if (typeName.Contains("Seed") || typeName.Contains("Plant") || typeName.Contains("Produce"))
                return true;

            // Stack and quantity components
            if (typeName.Contains("Stack") || typeName.Contains("Quantity"))
                return true;

            // Power and battery components
            if (typeName.Contains("Battery") || typeName.Contains("PowerCell"))
                return true;

            // Generator and fuel components (PACMAN, AME, etc.)
            if (typeName.Contains("Generator") || typeName.Contains("Fuel") || typeName.Contains("AME") ||
                typeName.Contains("PACMAN") || typeName.Contains("Reactor") || typeName.Contains("Engine"))
                return true;

            // Power/energy storage and distribution
            if (typeName.Contains("Power") || typeName.Contains("Energy") || typeName.Contains("Charge"))
                return true;

            // Machine state components
            if (typeName.Contains("Machine") || typeName.Contains("Processor") || typeName.Contains("Fabricator"))
                return true;

            // Atmospheric components (for atmospheric engines, scrubbers, etc.)
            if (typeName.Contains("Atmospheric") || typeName.Contains("Gas") || typeName.Contains("Atmos"))
                return true;

            // IFF and ship identification components
            if (typeName.Contains("IFF") || typeName.Contains("Identification") || typeName.Contains("Identity"))
                return true;

            // Access control: doors, airlocks, consoles using AccessReader must retain configuration.
            if (typeName.Contains("AccessReader"))
                return true;

            return false;
        }

        private void RestoreEntityComponents(EntityUid entityUid, List<ComponentData> componentDataList)
        {
            var restored = 0;
            var failed = 0;
            var skipped = 0;

            foreach (var componentData in componentDataList)
            {
                try
                {
                    var componentTypes = _entityManager.ComponentFactory.GetAllRefTypes()
                        .Select(idx => _entityManager.ComponentFactory.GetRegistration(idx).Type)
                        .Where(t => t.Name == componentData.Type || t.Name.EndsWith($".{componentData.Type}"))
                        .FirstOrDefault();

                    if (componentTypes != null && IsProblematicComponent(componentTypes))
                    {
                        skipped++;
                        continue;
                    }

                    RestoreComponent(entityUid, componentData);
                    restored++;
                }
                catch (Exception ex)
                {
                    failed++;
                    // Reduce noise - only warn for important components
                    var componentTypes = _entityManager.ComponentFactory.GetAllRefTypes()
                        .Select(idx => _entityManager.ComponentFactory.GetRegistration(idx).Type)
                        .Where(t => t.Name == componentData.Type || t.Name.EndsWith($".{componentData.Type}"))
                        .FirstOrDefault();

                    if (componentTypes != null && (IsImportantComponent(componentTypes) && !IsProblematicComponent(componentTypes)))
                    {
                        _sawmill.Warning($"Failed to restore important component {componentData.Type} on entity {entityUid}: {ex.Message}");
                    }
                    else
                    {
                        // Failed to restore component
                    }
                }
            }

            if (restored > 0 || failed > 0 || skipped > 0)
            {
                // Entity component restoration completed
                if (failed > 10) // Only warn if excessive failures
                {
                    _sawmill.Warning($"Entity {entityUid} had {failed} component restoration failures - entity may be incomplete");
                }
            }
        }

        private void RestoreComponent(EntityUid entityUid, ComponentData componentData)
        {
            try
            {
                // Special handling for solution components
                if (componentData.Type == "SolutionContainerManagerComponent")
                {
                    if (componentData.Properties.Any())
                    {
                        RestoreSolutionComponent(entityUid, componentData);
                    }
                    else
                    {
                        // Solution component has no data - skipping
                    }
                    return;
                }

                // Skip paper components - no longer preserved to reduce loading lag
                if (componentData.Type == "PaperComponent")
                {
                    return;
                }

                if (string.IsNullOrEmpty(componentData.YamlData))
                    return;

                // Filter out components that shouldn't be restored
                var componentTypes = _entityManager.ComponentFactory.GetAllRefTypes()
                    .Select(idx => _entityManager.ComponentFactory.GetRegistration(idx).Type)
                    .Where(t => t.Name == componentData.Type || t.Name.EndsWith($".{componentData.Type}"))
                    .ToList();

                if (!componentTypes.Any())
                {
                    // Component type not found - version mismatch
                    return;
                }

                var componentType = componentTypes.First();

                // Skip problematic components during restoration too
                if (IsProblematicComponent(componentType))
                {
                    // Skipping problematic component
                    return;
                }

                // Deserialize the component data
                var node = _deserializer.Deserialize<DataNode>(componentData.YamlData);

                // Ensure the entity has this component
                if (!_entityManager.HasComponent(entityUid, componentType))
                {
                    try
                    {
                        var newComponent = (Component)Activator.CreateInstance(componentType)!;
                        _entityManager.AddComponent(entityUid, newComponent);
                    }
                    catch (Exception ex)
                    {
                        // Failed to create component - continuing
                        return;
                    }
                }

                // Get the existing component and populate it with saved data
                var existingComponent = _entityManager.GetComponent(entityUid, componentType);

                try
                {
                    object? temp = existingComponent;
                    _serializationManager.CopyTo(node, ref temp);
                    // Component restored
                }
                catch (Exception ex)
                {
                    // Only warn for important components
                    if (IsImportantComponent(componentType) && !IsProblematicComponent(componentType))
                    {
                        _sawmill.Warning($"Failed to populate important component {componentData.Type} data: {ex.Message}");
                    }
                    else
                    {
                        // Failed to populate component data
                    }
                    // Continue execution - partial restoration is better than none
                }
            }
            catch (Exception ex)
            {
                // Failed to restore component - continuing
                // Don't throw - continue with other components
            }
        }

        private void RestoreSolutionComponent(EntityUid entityUid, ComponentData componentData)
        {
            try
            {
                if (!_entityManager.TryGetComponent<SolutionContainerManagerComponent>(entityUid, out var solutionManager))
                {
                    _sawmill.Warning($"Entity {entityUid} does not have SolutionContainerManagerComponent to restore");
                    return;
                }

                var restoredSolutions = 0;
                foreach (var (solutionName, solutionDataObj) in componentData.Properties)
                {
                    if (solutionDataObj is not Dictionary<string, object> solutionInfo)
                        continue;

                    try
                    {
                        // Get or create the solution
                        if (solutionManager.Solutions?.TryGetValue(solutionName, out var solution) != true || solution == null)
                        {
                            _sawmill.Warning($"Solution '{solutionName}' not found on entity {entityUid}");
                            continue;
                        }

                        // Clear existing contents
                        solution.RemoveAllSolution();

                        // Restore solution properties
                        if (solutionInfo.TryGetValue("Temperature", out var tempObj) && tempObj is double temperature)
                        {
                            solution.Temperature = (float)temperature;
                        }

                        // Restore reagents
                        if (solutionInfo.TryGetValue("Reagents", out var reagentsObj) &&
                            reagentsObj is Dictionary<string, object> reagents)
                        {
                            foreach (var (reagentId, quantityObj) in reagents)
                            {
                                if (quantityObj is double quantity && quantity > 0)
                                {
                                    // Add the reagent back to the solution
                                    solution?.AddReagent(reagentId, (float)quantity);
                                }
                            }
                        }

                        restoredSolutions++;
                        var reagentCount = solutionInfo.ContainsKey("Reagents") &&
                                          solutionInfo["Reagents"] is Dictionary<string, object> reagentDict ?
                                          reagentDict.Count : 0;
                        _sawmill.Debug($"Restored solution '{solutionName}' with {reagentCount} reagents");
                    }
                    catch (Exception ex)
                    {
                        _sawmill.Warning($"Failed to restore solution '{solutionName}': {ex.Message}");
                    }
                }

                if (restoredSolutions > 0)
                {
                    // Restored chemical solutions
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to restore solution component on entity {entityUid}: {ex.Message}");
            }
        }


        private (string? parentContainerEntity, string? containerSlot) GetContainerInfo(EntityUid entityUid)
        {
            try
            {
                if (!_entityManager.TryGetComponent<TransformComponent>(entityUid, out var transform))
                    return (null, null);

                var parent = transform.ParentUid;
                if (!parent.IsValid())
                    return (null, null);

                // Check if the parent has a container manager
                if (!_entityManager.TryGetComponent<ContainerManagerComponent>(parent, out var containerManager))
                    return (null, null);

                // Find which container this entity is in
                foreach (var container in containerManager.Containers.Values)
                {
                    if (container.Contains(entityUid))
                    {
                        return (parent.ToString(), container.ID);
                    }
                }

                return (null, null);
            }
            catch (Exception ex)
            {
                _sawmill.Warning($"Failed to get container info for entity {entityUid}: {ex.Message}");
                return (null, null);
            }
        }

        private bool HasContainers(EntityUid entityUid)
        {
            return _entityManager.HasComponent<ContainerManagerComponent>(entityUid);
        }

        private EntityData? SerializeEntity(EntityUid uid, TransformComponent transform, string prototype, EntityUid gridId)
        {
            try
            {
                // Validate entity still exists and isn't being deleted
                if (!_entityManager.EntityExists(uid) || _entityManager.IsQueuedForDeletion(uid))
                {
                    _sawmill.Warning($"Attempted to serialize deleted/invalid entity {uid}");
                    return null;
                }

                // Additional validation for entity state
                var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(uid);
                if (meta == null || meta.EntityLifeStage >= EntityLifeStage.Terminating)
                {
                    _sawmill.Warning($"Attempted to serialize terminating entity {uid}");
                    return null;
                }

                // Get container relationship information
                var (parentContainer, containerSlot) = GetContainerInfo(uid);
                var isContained = parentContainer != null;
                var isContainer = HasContainers(uid);

                // Serialize component states
                var components = SerializeEntityComponents(uid);

                // Use entity's current position and rotation (grid is already normalized to 0°)
                var position = transform.LocalPosition;
                var rotation = transform.LocalRotation;

                var entityData = new EntityData
                {
                    EntityId = uid.ToString(),
                    Prototype = prototype,
                    Position = new Vector2((float)Math.Round(position.X, 3), (float)Math.Round(position.Y, 3)),
                    Rotation = (float)Math.Round(rotation.Theta, 3),
                    Components = components,
                    ParentContainerEntity = parentContainer,
                    ContainerSlot = containerSlot,
                    IsContainer = isContainer,
                    IsContained = isContained
                };

                return entityData;
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to serialize entity {uid}: {ex.Message}");
                return null;
            }
        }

        private void SerializeContainedEntities(EntityUid gridId, GridData gridData, HashSet<EntityUid> alreadySerialized)
        {
            var verbose = _configManager.GetCVar(Content.Shared.CCVar.CCVars.ShipyardSaveVerbose);
            var excludeVending = _configManager.GetCVar(Content.Shared.HL.CCVar.HLCCVars.ExcludeVendingInShipSave);
            // Find all entities that might be contained within grid entities but not directly on the grid
            var containersToCheck = new Queue<EntityUid>();

            // Start with all container entities on the grid
            foreach (var entityData in gridData.Entities.Where(e => e.IsContainer))
            {
                if (EntityUid.TryParse(entityData.EntityId, out var containerUid))
                {
                    containersToCheck.Enqueue(containerUid);
                }
            }

            // Process containers recursively
            while (containersToCheck.Count > 0)
            {
                var containerUid = containersToCheck.Dequeue();

                // Validate container still exists
                if (!_entityManager.EntityExists(containerUid) || _entityManager.IsQueuedForDeletion(containerUid))
                    continue;

                if (!_entityManager.TryGetComponent<ContainerManagerComponent>(containerUid, out var containerManager))
                    continue;

                foreach (var container in containerManager.Containers.Values)
                {
                    foreach (var containedEntity in container.ContainedEntities)
                    {
                        if (alreadySerialized.Contains(containedEntity))
                            continue;

                        // Validate contained entity exists
                        if (!_entityManager.EntityExists(containedEntity) || _entityManager.IsQueuedForDeletion(containedEntity))
                            continue;

                        try
                        {
                            var transform = _entityManager.GetComponent<TransformComponent>(containedEntity);
                            var meta = _entityManager.GetComponentOrNull<MetaDataComponent>(containedEntity);
                            var proto = meta?.EntityPrototype?.ID ?? string.Empty;

                            // Skip entities with invalid prototypes
                            if (string.IsNullOrEmpty(proto))
                                continue;

                            // Skip vending machines in containers to avoid lag
                            if (excludeVending && _entityManager.HasComponent<VendingMachineComponent>(containedEntity))
                            {
                                // Downgraded from Info to Debug
                                if (verbose)
                                    _sawmill.Debug($"Skipping contained vending machine {proto} during serialization");
                                continue; // Skip contained vending machines
                            }

                            var entityData = SerializeEntity(containedEntity, transform, proto, gridId);
                            if (entityData != null)
                            {
                                gridData.Entities.Add(entityData);
                                alreadySerialized.Add(containedEntity);
                                if (verbose)
                                    _sawmill.Debug($"Serialized contained entity {containedEntity} ({proto}) in container {containerUid}");

                                // If this contained entity is also a container, check its contents
                                if (entityData.IsContainer)
                                {
                                    containersToCheck.Enqueue(containedEntity);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _sawmill.Warning($"Failed to serialize contained entity {containedEntity}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private EntityUid? SpawnEntityWithComponents(EntityData entityData, EntityCoordinates coordinates, bool clearDefaultsForContainers = true)
        {
            try
            {
                // Spawn the basic entity
                var newEntity = _entityManager.SpawnEntity(entityData.Prototype, coordinates);

                // Apply rotation if it exists
                if (Math.Abs(entityData.Rotation) > 0.001f)
                {
                    var transform = _entityManager.GetComponent<TransformComponent>(newEntity);
                    transform.LocalRotation = new Angle(entityData.Rotation);
                }

                // Clear any default container contents to prevent duplicates
                // This ensures saved containers don't get refilled with prototype defaults
                if (clearDefaultsForContainers && entityData.IsContainer && _entityManager.TryGetComponent<ContainerManagerComponent>(newEntity, out var containerManager))
                {
                    // If this entity uses an AccessReader that pulls requirements from a specific container,
                    // don't clear that container or we may wipe its configured access provider board.
                    AccessReaderComponent? accessReaderOnOwner = null;
                    _entityManager.TryGetComponent(newEntity, out accessReaderOnOwner);

                    foreach (var container in containerManager.Containers.Values)
                    {
                        if (accessReaderOnOwner != null && accessReaderOnOwner.ContainerAccessProvider == container.ID)
                            continue;
                        // Do not clear powered light bulb containers; ships would spawn dark.
                        if (container.ID == Content.Server.Light.EntitySystems.PoweredLightSystem.LightBulbContainer)
                            continue;
                        // Clear default spawned items - we'll restore saved contents later
                        var defaultItems = container.ContainedEntities.ToList();
                        foreach (var defaultItem in defaultItems)
                        {
                            _containerSystem.Remove(defaultItem, container);
                            _entityManager.DeleteEntity(defaultItem);
                        }
                    }
                    //_sawmill.Debug($"Cleared default contents from container {newEntity} - will restore saved contents");
                }

                // Restore component states
                if (entityData.Components.Any())
                {
                    RestoreEntityComponents(newEntity, entityData.Components);
                }

                return newEntity;
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to spawn entity with components {entityData.Prototype}: {ex.Message}");
                return null;
            }
        }

        private bool InsertIntoContainer(EntityUid entityToInsert, EntityUid containerEntity, string containerSlot)
        {
            try
            {
                if (!_entityManager.TryGetComponent<ContainerManagerComponent>(containerEntity, out var containerManager))
                {
                    _sawmill.Warning($"Container entity {containerEntity} does not have ContainerManagerComponent");
                    return false;
                }

                if (!containerManager.TryGetContainer(containerSlot, out var container))
                {
                    _sawmill.Warning($"Container slot '{containerSlot}' not found on entity {containerEntity}");
                    return false;
                }

                // Use the container system to properly insert the entity
                if (_containerSystem.Insert(entityToInsert, container))
                {
                    //_sawmill.Debug($"Successfully inserted entity {entityToInsert} into container {containerEntity} slot '{containerSlot}'");
                    return true;
                }
                else
                {
                    //_sawmill.Warning($"Failed to insert entity {entityToInsert} into container {containerEntity} slot '{containerSlot}' - container may be full");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Error inserting entity {entityToInsert} into container {containerEntity}: {ex.Message}");
                return false;
            }
        }

        private List<(Vector2i coords, Tile tile)> SortTilesForConnectivity(List<(Vector2i coords, Tile tile)> tilesToPlace)
        {
            if (!tilesToPlace.Any()) return tilesToPlace;

            var result = new List<(Vector2i coords, Tile tile)>();
            var remaining = new HashSet<Vector2i>(tilesToPlace.Select(t => t.coords));
            var tileDict = tilesToPlace.ToDictionary(t => t.coords, t => t.tile);

            // Start with any tile (preferably near center)
            var startCoord = tilesToPlace.OrderBy(t => t.coords.X * t.coords.X + t.coords.Y * t.coords.Y).First().coords;
            var queue = new Queue<Vector2i>();
            queue.Enqueue(startCoord);
            remaining.Remove(startCoord);

            // Flood-fill from start position
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add((current, tileDict[current]));

                // Check adjacent positions (4-directional)
                var adjacent = new[]
                {
                    new Vector2i(current.X + 1, current.Y),
                    new Vector2i(current.X - 1, current.Y),
                    new Vector2i(current.X, current.Y + 1),
                    new Vector2i(current.X, current.Y - 1)
                };

                foreach (var adj in adjacent)
                {
                    if (remaining.Contains(adj))
                    {
                        queue.Enqueue(adj);
                        remaining.Remove(adj);
                    }
                }
            }

            // Add any remaining disconnected tiles (these will create separate grids)
            foreach (var remainingCoord in remaining)
            {
                result.Add((remainingCoord, tileDict[remainingCoord]));
                _sawmill.Warning($"GRID SPLIT: Disconnected tile at {remainingCoord} will create separate grid");
            }

            return result;
        }

        private System.Numerics.Vector2 FindNearbyPosition(EntityUid gridEntity, System.Numerics.Vector2 originalPosition)
        {
            // Try to find a nearby unoccupied position
            var searchPositions = new[]
            {
                originalPosition,
                originalPosition + new Vector2(1, 0),
                originalPosition + new Vector2(-1, 0),
                originalPosition + new Vector2(0, 1),
                originalPosition + new Vector2(0, -1),
                originalPosition + new Vector2(1, 1),
                originalPosition + new Vector2(-1, -1),
                originalPosition + new Vector2(1, -1),
                originalPosition + new Vector2(-1, 1)
            };

            foreach (var testPos in searchPositions)
            {
                var coords = new EntityCoordinates(gridEntity, testPos);

                // Check if position is occupied (basic check)
                var lookup = _entityManager.System<EntityLookupSystem>();
                var mapCoords = coords.ToMap(_entityManager, _transform);
                var entitiesAtPos = lookup.GetEntitiesIntersecting(mapCoords.MapId, new Box2(testPos - Vector2.One * 0.1f, testPos + Vector2.One * 0.1f));
                if (!entitiesAtPos.Any())
                {
                    return testPos;
                }
            }

            // If all nearby positions are occupied, just use the original position
            return originalPosition;
        }

        private void ValidateContainerRelationships(GridData gridData)
        {
            try
            {
                var containerEntities = gridData.Entities.Where(e => e.IsContainer).ToList();
                var containedEntities = gridData.Entities.Where(e => e.IsContained).ToList();
                var entityIds = gridData.Entities.Select(e => e.EntityId).ToHashSet();

                // Validating container relationships

                var orphanedEntities = 0;
                var invalidContainers = 0;

                foreach (var containedEntity in containedEntities)
                {
                    // Check if parent container exists
                    if (string.IsNullOrEmpty(containedEntity.ParentContainerEntity))
                    {
                        _sawmill.Warning($"Contained entity {containedEntity.EntityId} has no parent container specified");
                        orphanedEntities++;
                        continue;
                    }

                    if (!entityIds.Contains(containedEntity.ParentContainerEntity))
                    {
                        _sawmill.Warning($"Contained entity {containedEntity.EntityId} references non-existent parent container {containedEntity.ParentContainerEntity}");
                        orphanedEntities++;
                        continue;
                    }

                    // Check if parent is actually marked as a container
                    var parentEntity = gridData.Entities.FirstOrDefault(e => e.EntityId == containedEntity.ParentContainerEntity);
                    if (parentEntity != null && !parentEntity.IsContainer)
                    {
                        _sawmill.Warning($"Entity {containedEntity.EntityId} is contained in {containedEntity.ParentContainerEntity}, but parent is not marked as container");
                        invalidContainers++;
                    }

                    // Check if container slot is specified
                    if (string.IsNullOrEmpty(containedEntity.ContainerSlot))
                    {
                        _sawmill.Warning($"Contained entity {containedEntity.EntityId} has no container slot specified");
                    }
                }

                if (orphanedEntities > 0 || invalidContainers > 0)
                {
                    _sawmill.Warning($"Container validation found issues: {orphanedEntities} orphaned entities, {invalidContainers} invalid containers");
                }
                else
                {
                    // Container relationship validation passed
                }
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to validate container relationships: {ex.Message}");
            }
        }

        private void ReconstructEntitiesLegacyMode(GridData gridData, MapGridComponent newGrid, Dictionary<string, EntityUid> entityIdMapping, bool clearDefaults = false)
        {
            _sawmill.Info("Using legacy reconstruction mode for backward compatibility");

            var spawnedCount = 0;
            var failedCount = 0;

            foreach (var entityData in gridData.Entities)
            {
                if (string.IsNullOrEmpty(entityData.Prototype))
                {
                    _sawmill.Debug($"Skipping entity with empty prototype at {entityData.Position}");
                    continue;
                }

                try
                {
                    var coordinates = new EntityCoordinates(newGrid.Owner, entityData.Position);
                    var newEntity = SpawnEntityWithComponents(entityData, coordinates, clearDefaultsForContainers: clearDefaults);

                    if (newEntity != null)
                    {
                        entityIdMapping[entityData.EntityId] = newEntity.Value;
                        spawnedCount++;
                        _sawmill.Debug($"Legacy: Spawned entity {newEntity} ({entityData.Prototype}) at {entityData.Position}");
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Legacy: Failed to spawn entity {entityData.Prototype} at {entityData.Position}: {ex.Message}");
                    failedCount++;
                }
            }

            // Legacy reconstruction complete
        }

    }
}
