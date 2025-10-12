using Content.Server._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.Shuttles.Save; // For SendShipSaveDataClientMessage
using Content.Server.Maps;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Access.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Power.Components;
using Content.Shared.VendingMachines;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
// using Content.Shared.Access.Components; // duplicate using removed
using System.Diagnostics.CodeAnalysis;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Numerics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.ContentPack;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Physics.Components;
using Robust.Shared.Containers;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Robust.Shared.Configuration;
using Content.Shared.HL.CCVar;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using Robust.Shared.Serialization;
using Content.Shared.Storage.Components;
using Robust.Shared.GameStates;
using Content.Shared.Wall; // WallMountComponent for preserving wall-mounted fixtures

// Suppress RA0004 for this file. There is no Task<Result> usage here, but the analyzer
// occasionally reports a false positive during Release/integration builds.
#pragma warning disable RA0004
// Suppress naming rule for _NF namespace prefix (modding convention)
#pragma warning disable IDE1006
namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// System for saving ships using the MapLoaderSystem infrastructure.
/// Saves ships as complete YAML files similar to savegrid command,
/// after cleaning them of problematic components and moving to exports folder.
/// </summary>
[SuppressMessage("Usage", "RA0004:Risk of deadlock from accessing Task<T>.Result", Justification = "No Task.Result used; false positive during Release/integration builds.")]
public sealed class ShipyardGridSaveSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly Content.Server.Shuttles.Save.ShipSerializationSystem _shipSerialization = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;

    private ISawmill _sawmill = default!;
    private MapLoaderSystem _mapLoader = default!;
    private SharedMapSystem _mapSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Initialize sawmill for logging
        _sawmill = Logger.GetSawmill("shipyard.gridsave");

        // Get the MapLoaderSystem reference
        _mapLoader = _entitySystemManager.GetEntitySystem<MapLoaderSystem>();
        _mapSystem = _entitySystemManager.GetEntitySystem<SharedMapSystem>();

        // Subscribe to shipyard console events
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSaveMessage>(OnSaveShipMessage);
    }

    private void OnSaveShipMessage(EntityUid consoleUid, ShipyardConsoleComponent component, ShipyardConsoleSaveMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            _sawmill.Warning("No ID card in shipyard console slot");
            return;
        }

        if (!_entityManager.TryGetComponent<ShuttleDeedComponent>(targetId, out var deed))
        {
            _sawmill.Warning("ID card does not have a shuttle deed");
            return;
        }

        if (deed.ShuttleUid == null || !_entityManager.TryGetEntity(deed.ShuttleUid.Value, out var shuttleUid))
        {
            _sawmill.Warning("Shuttle deed does not reference a valid shuttle");
            return;
        }

        if (!_entityManager.TryGetComponent<MapGridComponent>(shuttleUid.Value, out var gridComponent))
        {
            _sawmill.Warning("Shuttle entity is not a valid grid");
            return;
        }

        // Get player session
        if (!_playerManager.TryGetSessionByEntity(player, out var playerSession))
        {
            _sawmill.Warning("Could not get player session");
            return;
        }

        _sawmill.Info($"Starting ship save for {deed.ShuttleName ?? "Unknown_Ship"} owned by {playerSession.Name}");

        // Run save inline on the main thread to avoid off-thread ECS access.
        var success = TrySaveGridAsShip(shuttleUid.Value, deed.ShuttleName ?? "Unknown_Ship", playerSession.UserId.ToString(), playerSession);

        if (success)
        {
            // Clean up the deed after successful save
            _entityManager.RemoveComponent<ShuttleDeedComponent>(targetId);

            // Also remove any other shuttle deeds that reference this shuttle
            RemoveAllShuttleDeeds(shuttleUid.Value);

            // Transfer semantics: after saving, delete the live ship grid.
            // Use QueueDel to schedule deletion safely at end-of-frame to avoid PVS or in-frame references.
            QueueDel(shuttleUid.Value);
            _sawmill.Info($"Successfully saved ship {deed.ShuttleName}; queued deletion of grid {shuttleUid.Value}");
        }
        else
        {
            _sawmill.Error($"Failed to save ship {deed.ShuttleName}");
        }
    }

    /// <summary>
    /// Removes all ShuttleDeedComponents that reference the specified shuttle EntityUid
    /// </summary>
    private void RemoveAllShuttleDeeds(EntityUid shuttleUid)
    {
        var query = _entityManager.EntityQueryEnumerator<ShuttleDeedComponent>();
        var deedsToRemove = new List<EntityUid>();

        while (query.MoveNext(out var entityUid, out var deed))
        {
            if (deed.ShuttleUid != null && _entityManager.TryGetEntity(deed.ShuttleUid.Value, out var deedShuttleEntity) && deedShuttleEntity == shuttleUid)
            {
                deedsToRemove.Add(entityUid);
            }
        }

        foreach (var deedEntity in deedsToRemove)
        {
            _entityManager.RemoveComponent<ShuttleDeedComponent>(deedEntity);
            _sawmill.Info($"Removed shuttle deed from entity {deedEntity}");
        }
    }

    /// <summary>
    /// Saves a grid to YAML without mutating live game state. Uses ShipSerializationSystem to serialize in-place.
    /// This avoids moving the grid to temporary maps or deleting any entities, preventing PVS/map deletion issues.
    /// </summary>
    public bool TrySaveGridAsShip(EntityUid gridUid, string shipName, string playerUserId, ICommonSession playerSession)
    {
        if (!_entityManager.HasComponent<MapGridComponent>(gridUid))
        {
            _sawmill.Error($"Entity {gridUid} is not a valid grid");
            return false;
        }

        try
        {
            // Per user request: before purging / serializing, add SecretStashComponent to any entity contained
            // directly within a secret stash so that they are also considered preserved.
            TagStashContents(gridUid);
            // Purge transient entities (unanchored or inside containers) before serialization.
            // This mutates the live grid, but only removes objects explicitly deemed non-persistent by design.
            PurgeTransientEntities(gridUid);

            _sawmill.Info($"Serializing ship grid {gridUid} as '{shipName}' after transient purge using direct serialization");

            // 1) Serialize the grid and its children to a MappingDataNode (engine-standard format)
            var entities = new HashSet<EntityUid> { gridUid };
            // Prefer AutoInclude to pull in dependent entities; we'll sanitize nullspace and parents out below
            var opts = SerializationOptions.Default with
            {
                // Do NOT auto-include referenced entities (players/admin observers/etc.).
                // This prevents exceptions when encountering unserializable entities and keeps saves scoped to the grid.
                MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                ErrorOnOrphan = false,
                // Disable auto-include logging to avoid excessive log spam/lag during saves.
                LogAutoInclude = null
            };
            var (node, category) = _mapLoader.SerializeEntitiesRecursive(entities, opts);
            if (category != FileCategory.Grid)
            {
                _sawmill.Warning($"Expected FileCategory.Grid but got {category}; continuing with sanitation");
            }

            // 2) Sanitize the node to match blueprint conventions
            SanitizeShipSaveNode(node);

            // 3) Convert MappingDataNode to YAML text without touching disk
            var yaml = WriteYamlToString(node);

            // 4) Send to client for local saving
            var saveMessage = new SendShipSaveDataClientMessage(shipName, yaml);
            RaiseNetworkEvent(saveMessage, playerSession);
            _sawmill.Info($"Sent ship data '{shipName}' to client {playerSession.Name} for local saving");

            // Fire ShipSavedEvent for bookkeeping; DO NOT delete the grid or maps here.
            var gridSavedEvent = new ShipSavedEvent
            {
                GridUid = gridUid,
                ShipName = shipName,
                PlayerUserId = playerUserId,
                PlayerSession = playerSession
            };
            RaiseLocalEvent(gridSavedEvent);
            _sawmill.Info($"Fired ShipSavedEvent for '{shipName}'");

            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception during non-destructive ship save: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Adds <see cref="SecretStashComponent"/> to any entity currently hidden inside a SecretStash on the target grid.
    /// This satisfies the explicit requirement to "add secretstashcomponent to anything within the bluespace stash".
    /// NOTE: This will grant those items stash-like behavior (verbs, extra container). If this becomes undesirable,
    /// consider introducing a lightweight marker component instead and adjusting purge logic to check it.
    /// </summary>
    private void TagStashContents(EntityUid gridUid)
    {
        try
        {
            var tagged = 0;
            var stashQuery = _entityManager.EntityQueryEnumerator<SecretStashComponent, TransformComponent>();
            while (stashQuery.MoveNext(out var stashEnt, out var stashComp, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                var hidden = stashComp.ItemContainer?.ContainedEntity;
                if (hidden == null)
                    continue;
                // If already a stash, skip.
                if (_entityManager.HasComponent<SecretStashComponent>(hidden.Value))
                    continue;
                // Dynamically add the component. OnInit won't run automatically here for networked comps
                // when added at runtime; EnsureComp will construct and initialize it.
                _entityManager.EnsureComponent<SecretStashComponent>(hidden.Value);
                tagged++;
            }
            if (tagged > 0)
                _sawmill.Info($"TagStashContents: Added SecretStashComponent to {tagged} hidden item(s) on grid {gridUid}");
        }
        catch (Exception e)
        {
            _sawmill.Warning($"TagStashContents: Exception while tagging stash contents on grid {gridUid}: {e.Message}");
        }
    }

    /// <summary>
    /// Deletes entities on the grid that should not be persisted with the ship:
    ///  - Any entity whose Transform is not Anchored ("loose on the floor")
    ///  - Any entity that is currently inside any container (including nested)
    ///
    /// IMPORTANT: Anchored entities are never deleted, even if they appear inside containers due to edge cases.
    ///            Only unanchored entities are eligible for deletion. Contents of preserved bluespace stashes are also kept.
    /// Excludes the grid root itself.
    /// </summary>
    private void PurgeTransientEntities(EntityUid gridUid)
    {
        try
        {
            if (!_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
                return;
            var lookupSystem = _entitySystemManager.GetEntitySystem<EntityLookupSystem>();
            var looseDeletes = new List<EntityUid>();
            var containerContentDeletes = new List<EntityUid>();
            var processed = new HashSet<EntityUid>();

            // Pre-mark all stash roots and their direct hidden item contents as processed so they are never purged.
            // (User request was to make stash contents survive; instead of mutating items with SecretStashComponent we just exempt them here.)
            var stashQuery = _entityManager.EntityQueryEnumerator<SecretStashComponent, TransformComponent>();
            var preservedStashItemCount = 0;
            while (stashQuery.MoveNext(out var stashEnt, out var stashComp, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;
                processed.Add(stashEnt); // stash root
                var hidden = stashComp.ItemContainer?.ContainedEntity;
                if (hidden != null && _entityManager.EntityExists(hidden.Value))
                {
                    // Mark the hidden item as processed so fallback scans won't queue it for deletion.
                    processed.Add(hidden.Value);
                    preservedStashItemCount++;
                }
            }

            if (preservedStashItemCount > 0)
                _sawmill.Info($"PurgeTransientEntities: Preserving {preservedStashItemCount} secret stash item(s) on grid {gridUid}");

            _sawmill.Info($"PurgeTransientEntities: Scanning grid {gridUid} for transient entities (loose + contained)");

            // 1. Collect all entities spatially present on the grid (this won't include items inside containers)
            foreach (var ent in lookupSystem.GetEntitiesIntersecting(gridUid, grid.LocalAABB))
            {
                if (ent == gridUid)
                    continue;
                // Preserve any secret stash root or bluespace stash prototype entity itself
                if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
                    processed.Add(ent); // don't treat stash as loose
                if (!TryQueueLoose(ent, looseDeletes, processed))
                    continue;
            }

            // 2. Traverse container graphs on every anchored entity to collect ALL contained descendants
            foreach (var ent in lookupSystem.GetEntitiesIntersecting(gridUid, grid.LocalAABB))
            {
                if (ent == gridUid)
                    continue;
                if (!_entityManager.TryGetComponent<ContainerManagerComponent>(ent, out var manager))
                    continue;
                // If this entity is a stash or bluespace stash, preserve its contents entirely.
                if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
                    continue;
                foreach (var container in manager.Containers.Values)
                {
                    CollectContainerContentsRecursive(container.ContainedEntities, containerContentDeletes, processed);
                }
            }

            // Remove any duplicates between lists (if an entity was both loose + in container due to race, unlikely)
            if (containerContentDeletes.Count > 0)
            {
                var contentSet = new HashSet<EntityUid>(containerContentDeletes);
                looseDeletes.RemoveAll(e => contentSet.Contains(e));
            }

            var total = looseDeletes.Count + containerContentDeletes.Count;

            if (total == 0)
            {
                // Possibly lookup missed because of AABB mismatch or container-only population. Do a fallback exhaustive scan.
                var fallbackLoose = new List<EntityUid>();
                var fallbackContained = new List<EntityUid>();
                var fallbackProcessed = new HashSet<EntityUid>();

                // Exhaustive: iterate every entity with a Transform and check if its GridUid matches.
                var xformQuery = _entityManager.EntityQueryEnumerator<TransformComponent>();
                var inspected = 0;
                while (xformQuery.MoveNext(out var ent, out var xform))
                {
                    inspected++;
                    if (ent == gridUid)
                        continue;
                    if (xform.GridUid != gridUid)
                        continue;
                    if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
                    {
                        fallbackProcessed.Add(ent);
                        continue; // stash root preserved
                    }
                    TryQueueLoose(ent, fallbackLoose, fallbackProcessed);
                    if (_entityManager.TryGetComponent<ContainerManagerComponent>(ent, out var mgr))
                    {
                        if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
                            continue; // don't traverse preserved stash contents
                        foreach (var container in mgr.Containers.Values)
                            CollectContainerContentsRecursive(container.ContainedEntities, fallbackContained, fallbackProcessed);
                    }
                }

                // Remove duplicates
                if (fallbackContained.Count > 0)
                {
                    var contentSet2 = new HashSet<EntityUid>(fallbackContained);
                    fallbackLoose.RemoveAll(e => contentSet2.Contains(e));
                }

                var fallbackTotal = fallbackLoose.Count + fallbackContained.Count;
                if (fallbackTotal == 0)
                {
                    _sawmill.Info($"PurgeTransientEntities: No transient entities found on grid {gridUid} after fallback (inspected={inspected}, AABB={grid.LocalAABB})");
                    return;
                }

                _sawmill.Info($"PurgeTransientEntities: Primary scan empty; fallback found {fallbackTotal} (loose={fallbackLoose.Count}, contained={fallbackContained.Count}) on grid {gridUid}");
                DeleteEntityList(fallbackContained, "contained-fallback");
                DeleteEntityList(fallbackLoose, "loose-fallback");
                return;
            }

            _sawmill.Info($"PurgeTransientEntities: Deleting {total} entities (loose={looseDeletes.Count}, contained={containerContentDeletes.Count}) on grid {gridUid}");

            // Delete contained entities first (so container state is clean before possibly deleting loose objects referencing them)
            DeleteEntityList(containerContentDeletes, "contained");
            // Then delete loose ones
            DeleteEntityList(looseDeletes, "loose");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception during PurgeTransientEntities on grid {gridUid}: {ex}");
        }
    }

    private bool TryQueueLoose(EntityUid ent, List<EntityUid> list, HashSet<EntityUid> processed)
    {
        if (!_entityManager.EntityExists(ent))
            return false;
        if (!processed.Add(ent))
            return false; // already processed
        // Skip if terminating
        if (_entityManager.GetComponent<MetaDataComponent>(ent).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;
        if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
            return false; // preserve stash root outright
        if (_entityManager.HasComponent<MapGridComponent>(ent))
            return false; // never delete grid root or nested grids here
        // Preserve wall-mounted fixtures (buttons, levers, posters, etc.) regardless of anchored state
        if (_entityManager.HasComponent<WallMountComponent>(ent))
            return false;
        var anchored = false;
        if (_entityManager.TryGetComponent<TransformComponent>(ent, out var xform))
            anchored = xform.Anchored;
        var inContainer = _containerSystem.IsEntityInContainer(ent);
        // Per updated requirements: anchored entities must never be deleted under any circumstance.
        if (anchored)
            return false;
        if (inContainer)
        {
            // If this entity (at any ancestor depth) is ultimately inside a secret stash preserve it.
            if (IsInsideSecretStash(ent))
                return false;
        }
        // Only unanchored entities are eligible for deletion. If it's unanchored (loose) or unanchored-in-container, delete.
        if (!anchored || inContainer)
        {
            list.Add(ent);
            return true;
        }
        return false;
    }

    private void CollectContainerContentsRecursive(IReadOnlyList<EntityUid> contents, List<EntityUid> aggregate, HashSet<EntityUid> processed)
    {
        for (var i = 0; i < contents.Count; i++)
        {
            var ent = contents[i];
            if (!_entityManager.EntityExists(ent))
                continue;
            if (!processed.Add(ent))
                continue;
            // If the entity is a secret stash root, preserve it and do not descend or queue its contents.
            if (_entityManager.HasComponent<SecretStashComponent>(ent) || IsBluespaceStashPrototype(ent))
                continue;
            // If this entity (or an ancestor) is inside a secret stash we preserve it (do not queue) but still must not descend further since entire subtree should be kept.
            if (IsInsideSecretStash(ent))
                continue;
            // Preserve wall-mounted fixtures explicitly but still traverse their child containers.
            var isWallMount = _entityManager.HasComponent<WallMountComponent>(ent);
            // Preserve anchored entities even if they appear within containers; still traverse their child containers.
            var isAnchored = false;
            if (_entityManager.TryGetComponent<TransformComponent>(ent, out var xform))
                isAnchored = xform.Anchored;
            if (!isAnchored && !isWallMount)
            {
                aggregate.Add(ent);
            }
            if (_entityManager.TryGetComponent<ContainerManagerComponent>(ent, out var manager))
            {
                foreach (var container in manager.Containers.Values)
                {
                    CollectContainerContentsRecursive(container.ContainedEntities, aggregate, processed);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the given entity is contained (at any depth) within a <see cref="SecretStashComponent"/>.
    /// Walks up container parents until a non-contained entity is reached or a stash root is found.
    /// </summary>
    private bool IsInsideSecretStash(EntityUid ent)
    {
        // Fast path: immediately contained?
        if (!_containerSystem.IsEntityInContainer(ent))
            return false;
        // Walk up container chain.
        EntityUid current = ent;
        var safety = 0;
        while (safety++ < 64 && _containerSystem.TryGetContainingContainer(current, out var container))
        {
            var owner = container.Owner;
            if (!_entityManager.EntityExists(owner))
                return false;
            if (_entityManager.HasComponent<SecretStashComponent>(owner))
                return true; // Found stash root above.
            // Also treat bluespacestash prototype (storage-based) as a preservation root.
            if (IsBluespaceStashPrototype(owner))
                return true; // Found stash root above.
            current = owner;
        }
        return false;
    }

    /// <summary>
    /// Returns true if this entity's prototype id matches the explicit bluespace stash prototype we need to preserve.
    /// </summary>
    private bool IsBluespaceStashPrototype(EntityUid ent)
    {
        if (!_entityManager.TryGetComponent<MetaDataComponent>(ent, out var meta))
            return false;
        // Prototype id comparison (case-insensitive) to 'bluespacestash'
        return meta.EntityPrototype?.ID.Equals("bluespacestash", StringComparison.InvariantCultureIgnoreCase) == true;
    }

    private void DeleteEntityList(List<EntityUid> list, string category)
    {
        foreach (var ent in list)
        {
            try
            {
                // If it is still in a container, remove it cleanly first to clear flags
                if (_containerSystem.IsEntityInContainer(ent) && _entityManager.TryGetComponent<TransformComponent>(ent, out var _))
                {
                    // We need the container instance; brute force via manager (cheap for small counts)
                    if (_entityManager.TryGetComponent<ContainerManagerComponent>(ent, out var _))
                    {
                        // If the entity itself owns containers we don't care; removal is for when entity is inside one.
                    }
                }
                if (_entityManager.EntityExists(ent))
                    _entityManager.DeleteEntity(ent);
            }
            catch (Exception ex)
            {
                _sawmill.Warning($"Failed deleting {category} entity {ent}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Remove fields and components from the serialized YAML node to match blueprint output:
    /// - Clear nullspace
    /// - Remove mapInit/paused from entities
    /// - Remove Transform.rot entries
    /// - Remove SpreaderGrid update accumulator
    /// - Remove components: Joint, StationMember, NavMap, ShuttleDeed, IFF, LinkedLifecycleGridParent
    /// </summary>
    private void SanitizeShipSaveNode(MappingDataNode root)
    {
        // Ensure nullspace is empty
        try
        {
            root["nullspace"] = new SequenceDataNode();
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Failed to clear nullspace: {e.Message}");
        }

        if (!root.TryGet("entities", out SequenceDataNode? protoSeq) || protoSeq == null)
            return;

        var filteredTypes = new HashSet<string>
        {
            "Joint",
            "StationMember",
            "NavMap",
            "ShuttleDeed",
            "IFF",
            "LinkedLifecycleGridParent",
        };

        // Prototype-level exclusions for obvious non-ship entities.
        // If we encounter these, we drop them entirely from the export.
        var filteredPrototypes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // admin / ghost observers, spectators, etc.
            "AdminObserver",
            "AdminObserverDummy",
            "Ghost",
            "GhostRoleMob",
        };

        foreach (var protoNode in protoSeq)
        {
            if (protoNode is not MappingDataNode protoMap)
                continue;

            if (!protoMap.TryGet("entities", out SequenceDataNode? entitiesSeq) || entitiesSeq == null)
                continue;

            for (var i = 0; i < entitiesSeq.Count; i++)
            {
                if (entitiesSeq[i] is not MappingDataNode entMap)
                    continue;

                // Remove map initialization flags
                entMap.Remove("mapInit");
                entMap.Remove("paused");

                // Optional: Drop entities that are clearly unrelated by prototype id.
                // Each proto group node contains a "proto" key with the prototype id string.
                if (protoMap.TryGet("proto", out ValueDataNode? protoIdNode) && protoIdNode != null)
                {
                    var protoId = protoIdNode.Value;
                    if (filteredPrototypes.Contains(protoId))
                    {
                        // Remove this entity entirely
                        entitiesSeq.RemoveAt(i);
                        i--;
                        continue;
                    }
                }

                // Components cleanup
                if (!entMap.TryGet("components", out SequenceDataNode? comps) || comps == null)
                {
                    // If there are no components left, this entity is empty and can be removed
                    entitiesSeq.RemoveAt(i);
                    i--;
                    continue;
                }

                // Determine if this entity is the grid root (has MapGrid component)
                var hasMapGrid = false;
                var compsNotNull = comps!; // Assert non-null for analyzer; guarded above.
                foreach (var c in compsNotNull)
                {
                    if (c is MappingDataNode cm && cm.TryGet("type", out ValueDataNode? t) && t != null && t.Value == "MapGrid")
                    {
                        hasMapGrid = true;
                        break;
                    }
                }

                var newComps = new SequenceDataNode();
                foreach (var compNode in compsNotNull)
                {
                    if (compNode is not MappingDataNode compMap)
                        continue;

                    if (!compMap.TryGet("type", out ValueDataNode? typeNode) || typeNode == null)
                    {
                        newComps.Add(compMap);
                        continue;
                    }

                    var typeName = typeNode.Value;

                    // Filter out undesired component types entirely
                    if (filteredTypes.Contains(typeName))
                        continue;

                    // Transform: remove rotation on the grid root to match blueprint expectations
                    if (typeName == "Transform" && hasMapGrid)
                    {
                        compMap.Remove("rot");
                    }

                    // Gravity: strip runtime 'enabled' flag to match blueprint outputs
                    if (typeName == "Gravity")
                    {
                        compMap.Remove("enabled");
                        compMap.Remove("Enabled");
                    }

                    // SpreaderGrid: strip accumulator fields
                    if (typeName == "SpreaderGrid")
                    {
                        compMap.Remove("updateAccumulator");
                        compMap.Remove("UpdateAccumulator");
                    }

                    // VendingMachine: strip runtime inventory & timers to match blueprint expectations
                    if (typeName == "VendingMachine")
                    {
                        compMap.Remove("Inventory");
                        compMap.Remove("EmaggedInventory");
                        compMap.Remove("ContrabandInventory");
                        compMap.Remove("Contraband");
                        compMap.Remove("EjectEnd");
                        compMap.Remove("DenyEnd");
                        compMap.Remove("DispenseOnHitEnd");
                        compMap.Remove("NextEmpEject");
                        compMap.Remove("EjectRandomCounter");
                    }

                    newComps.Add(compMap);
                }

                if (newComps.Count > 0)
                {
                    entMap["components"] = newComps;
                }
                else
                {
                    // No components left; remove the entire entity
                    entitiesSeq.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    private string WriteYamlToString(MappingDataNode node)
    {
        // Based on MapLoaderSystem.Write but to a string instead of file
        var document = new YamlDocument(node.ToYaml());
        using var writer = new StringWriter();
        var stream = new YamlStream { document };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
        return writer.ToString();
    }

    #region 5-Step Ship Save Process

    /// <summary>
    /// STEP 1: Create a blank map and teleport the ship to it for saving
    /// </summary>
    private async Task<MapId?> Step1_CreateBlankMapAndTeleportShip(EntityUid gridUid, string shipName, ICommonSession playerSession)
    {
        MapId tempMapId = default;
        try
        {
            _sawmill.Info("Step 1: Creating blank map and teleporting ship");

            // Create a temporary blank map for saving
            tempMapId = _mapManager.CreateMap();
            _sawmill.Info($"Created temporary map {tempMapId}");

            // Step 2: Move the grid to the temporary map and clean it
            var tempGridUid = await MoveAndCleanGrid(gridUid, tempMapId);
            if (tempGridUid == null)
            {
                _sawmill.Error("Failed to move and clean grid");
                return null;
            }

            _sawmill.Info($"Successfully moved and cleaned grid to {tempGridUid}");

            // Step 3: Save the grid using MapLoaderSystem to a temporary file
            var fileName = $"{shipName}.yml";
            var tempFilePath = new ResPath("/") / "UserData" / fileName;
            _sawmill.Info($"Attempting to save grid as {fileName}");

            bool success = _mapLoader.TrySaveGrid(tempGridUid.Value, tempFilePath);

            if (success)
            {
                _sawmill.Info($"Successfully saved grid to {fileName}");

                // Step 4: Read the YAML file and send to client
                try
                {
                    using var fileStream = _resourceManager.UserData.OpenRead(tempFilePath);
                    using var reader = new StreamReader(fileStream);
                    var yamlContent = await reader.ReadToEndAsync();

                    // Send the YAML data to the client for local saving
                    var saveMessage = new SendShipSaveDataClientMessage(shipName, yamlContent);
                    RaiseNetworkEvent(saveMessage, playerSession);

                    _sawmill.Info($"Sent ship data '{shipName}' to client {playerSession.Name} for local saving");

                    // Clean up the temporary server file with retry logic
                    await TryDeleteFileWithRetry(tempFilePath);
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to read/send YAML file: {ex}");
                    success = false;
                }
            }
            else
            {
                _sawmill.Error($"Failed to save grid to {fileName}");
            }

            return success ? tempMapId : null;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Exception during ship save: {ex}");
            return null;
        }
        finally
        {
            // Step 6: Clean up temporary resources with proper timing
            if (tempMapId != default)
            {
                // Give all systems significant time to finish processing the map deletion
                await Task.Delay(500);

                try
                {
                    _mapManager.DeleteMap(tempMapId);
                    _sawmill.Info($"Cleaned up temporary map {tempMapId}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to clean up temporary map {tempMapId}: {ex}");
                }
            }

            // Delete the original grid after all processing is complete
            if (_entityManager.EntityExists(gridUid))
            {
                // Additional delay to ensure all systems finish processing entity changes
                await Task.Delay(300);

                try
                {
                    _entityManager.DeleteEntity(gridUid);
                    _sawmill.Info($"Deleted original grid entity {gridUid}");
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to delete original grid entity {gridUid}: {ex}");
                }
            }
        }
    }

    /// <summary>
    /// STEP 2: Empty the contents of any container on the grid, then clean the grid of problematic components
    /// and delete freefloating entities that are not anchored or connected to the grid
    /// </summary>
    private async Task<bool> Step2_EmptyContainersAndCleanGrid(EntityUid gridUid)
    {
        try
        {
            _sawmill.Info("Step 2: Emptying containers and cleaning grid");

            var allEntities = new HashSet<EntityUid>();

            // Get all entities on the grid
            if (_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            {
                var gridBounds = grid.LocalAABB;
                var lookupSystem = _entitySystemManager.GetEntitySystem<EntityLookupSystem>();
                foreach (var entity in lookupSystem.GetEntitiesIntersecting(gridUid, gridBounds))
                {
                    if (entity != gridUid) // Don't include the grid itself
                        allEntities.Add(entity);
                }
            }

            _sawmill.Info($"Found {allEntities.Count} entities to process");

            var entitiesRemoved = 0;
            var containersEmptied = 0;

            // First pass: Empty all containers
            foreach (var entity in allEntities.ToList())
            {
                if (!_entityManager.EntityExists(entity))
                    continue;

                // Empty any containers by removing their contents first (to clear InContainer flags client-side), then delete
                if (_entityManager.TryGetComponent<ContainerManagerComponent>(entity, out var containerManager))
                {
                    foreach (var container in containerManager.Containers.Values)
                    {
                        var containedEntities = container.ContainedEntities.ToList();
                        foreach (var containedEntity in containedEntities)
                        {
                            try
                            {
                                // Properly remove from the container to ensure MetaDataFlags.InContainer is cleared everywhere
                                _containerSystem.Remove(containedEntity, container, force: true);
                                _entityManager.DeleteEntity(containedEntity);
                                entitiesRemoved++;
                            }
                            catch (Exception ex)
                            {
                                _sawmill.Warning($"Failed to delete contained entity {containedEntity}: {ex}");
                            }
                        }
                        if (containedEntities.Count > 0)
                        {
                            containersEmptied++;
                            _sawmill.Info($"Emptied container with {containedEntities.Count} items");
                        }
                    }
                }
            }

            // Second pass: Delete freefloating entities (not anchored or connected to grid)
            var freefloatingEntities = new List<EntityUid>();
            foreach (var entity in allEntities)
            {
                if (!_entityManager.EntityExists(entity))
                    continue;

                // Skip structural grid components
                if (_entityManager.HasComponent<MapGridComponent>(entity))
                    continue;

                // Check if entity is anchored or in a container
                if (_entityManager.TryGetComponent<TransformComponent>(entity, out var transform))
                {
                    // If not anchored and not in a container, mark for deletion
                    if (!transform.Anchored && !_containerSystem.IsEntityInContainer(entity))
                    {
                        freefloatingEntities.Add(entity);
                    }
                }
            }

            // Delete freefloating entities
            foreach (var entity in freefloatingEntities)
            {
                try
                {
                    if (_entityManager.EntityExists(entity))
                    {
                        _entityManager.DeleteEntity(entity);
                        entitiesRemoved++;
                    }
                }
                catch (Exception ex)
                {
                    _sawmill.Warning($"Failed to delete freefloating entity {entity}: {ex}");
                }
            }

            _sawmill.Info($"Step 2 complete: Emptied {containersEmptied} containers, removed {entitiesRemoved} entities");
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Step 2 failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// STEP 3: Delete any vending machines or remaining structures that would pose a problem to ship saving
    /// </summary>
    private async Task<bool> Step3_DeleteProblematicStructures(EntityUid gridUid)
    {
        try
        {
            _sawmill.Info("Step 3: Deleting problematic structures");

            var allEntities = new HashSet<EntityUid>();

            // Get all remaining entities on the grid
            if (_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            {
                var gridBounds = grid.LocalAABB;
                var lookupSystem = _entitySystemManager.GetEntitySystem<EntityLookupSystem>();
                foreach (var entity in lookupSystem.GetEntitiesIntersecting(gridUid, gridBounds))
                {
                    if (entity != gridUid) // Don't include the grid itself
                        allEntities.Add(entity);
                }
            }

            var structuresRemoved = 0;
            var componentsRemoved = 0;

            foreach (var entity in allEntities.ToList())
            {
                if (!_entityManager.EntityExists(entity))
                    continue;

                // Delete vending machines completely
                if (_entityManager.HasComponent<VendingMachineComponent>(entity))
                {
                    _sawmill.Info($"Removing vending machine entity {entity}");
                    _entityManager.DeleteEntity(entity);
                    structuresRemoved++;
                    continue;
                }

                // Remove problematic components from remaining entities

                // Note: Removed PhysicsComponent deletion that was causing collision issues in loaded ships
                // PhysicsComponent and FixturesComponent are needed for proper collision detection

                // Remove atmospheric components that hold runtime state

                // Reset power components to clean state
            }

            _sawmill.Info($"Step 3 complete: Removed {structuresRemoved} problematic structures, cleaned {componentsRemoved} components");
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Step 3 failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// STEP 4: Save the grid
    /// </summary>
    private async Task<bool> Step4_SaveGrid(EntityUid gridUid, string shipName, ICommonSession playerSession)
    {
        try
        {
            _sawmill.Info($"Step 4: Saving grid as '{shipName}'");

            // Save the grid using MapLoaderSystem to a temporary file
            var fileName = $"{shipName}.yml";
            var tempFilePath = new ResPath("/") / "UserData" / fileName;

            bool success = _mapLoader.TrySaveGrid(gridUid, tempFilePath);

            if (success)
            {
                _sawmill.Info($"Successfully saved grid to {fileName}");

                // Read the YAML file and send to client
                try
                {
                    using var fileStream = _resourceManager.UserData.OpenRead(tempFilePath);
                    using var reader = new StreamReader(fileStream);
                    var yamlContent = await reader.ReadToEndAsync();

                    // Send the YAML data to the client for local saving
                    var saveMessage = new SendShipSaveDataClientMessage(shipName, yamlContent);
                    RaiseNetworkEvent(saveMessage, playerSession);

                    _sawmill.Info($"Sent ship data '{shipName}' to client {playerSession.Name} for local saving");

                    // Clean up the temporary server file
                    await TryDeleteFileWithRetry(tempFilePath);
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to read/send YAML file: {ex}");
                    success = false;
                }
            }
            else
            {
                _sawmill.Error($"Failed to save grid to {fileName}");
            }

            return success;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Step 4 failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// STEP 5: Throw event to say grid was saved, remove shuttle deed from player's ID, and update console
    /// </summary>
    private async Task<bool> Step5_PostSaveCleanupAndEvents(EntityUid originalGridUid, string shipName, string playerUserId, ICommonSession playerSession)
    {
        try
        {
            _sawmill.Info("Step 5: Post-save cleanup and events");

            // Fire grid saved event
            var gridSavedEvent = new ShipSavedEvent
            {
                GridUid = originalGridUid,
                ShipName = shipName,
                PlayerUserId = playerUserId,
                PlayerSession = playerSession
            };
            RaiseLocalEvent(gridSavedEvent);
            _sawmill.Info($"Fired ShipSavedEvent for '{shipName}'");

            // Deed removal is handled where the save is initiated (console slot entity) after success

            // Delete the original grid entity now that save is complete
            if (_entityManager.EntityExists(originalGridUid))
            {
                await Task.Delay(100); // Brief delay to ensure all events are processed
                _entityManager.DeleteEntity(originalGridUid);
                _sawmill.Info($"Deleted original grid entity {originalGridUid}");
            }

            _sawmill.Info("Step 5 complete: Events fired, deed removed, grid deleted");
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Step 5 failed: {ex}");
            return false;
        }
    }

    #endregion

    /// <summary>
    /// Legacy method - replaced by 5-step process above</summary>
    private async Task<EntityUid?> MoveAndCleanGrid(EntityUid originalGridUid, MapId targetMapId)
    {
        try
        {
            // Move the grid to the temporary map and normalize its rotation
            var gridTransform = _entityManager.GetComponent<TransformComponent>(originalGridUid);
            _transformSystem.SetCoordinates(originalGridUid, new EntityCoordinates(_mapManager.GetMapEntityId(targetMapId), Vector2.Zero));

            // Normalize grid rotation to 0 degrees
            _transformSystem.SetLocalRotation(originalGridUid, Angle.Zero);

            _sawmill.Info($"Moved grid {originalGridUid} to temporary map {targetMapId} and normalized rotation");

            // Clean the grid of problematic components
            CleanGridForSaving(originalGridUid);

            return originalGridUid;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to move and clean grid: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Removes problematic components from a grid before saving.
    /// This includes session-specific data, vending machines, runtime state, etc.
    /// Uses a two-phase approach: first delete problematic entities, then clean remaining entities.
    /// </summary>
    public void CleanGridForSaving(EntityUid gridUid)
    {
        _sawmill.Info($"Starting grid cleanup for {gridUid}");

        var allEntities = new HashSet<EntityUid>();

        // Get all entities on the grid
        if (_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
        {
            var gridBounds = grid.LocalAABB;
            var lookupSystem = _entitySystemManager.GetEntitySystem<EntityLookupSystem>();
            foreach (var entity in lookupSystem.GetEntitiesIntersecting(gridUid, gridBounds))
            {
                if (entity != gridUid) // Don't include the grid itself
                    allEntities.Add(entity);
            }
        }

        _sawmill.Info($"Found {allEntities.Count} entities to clean on grid");

        var entitiesRemoved = 0;
        var componentsRemoved = 0;

        // PHASE 1: Do not delete entities to preserve physics counts
        // We'll clean by removing components instead (e.g., VendingMachineComponent)
        _sawmill.Info("Phase 1: Skipping entity deletions to preserve physics components");
        _sawmill.Info($"Phase 1 complete: deleted {entitiesRemoved} entities");

        // PHASE 2: Clean components from remaining entities
        // Re-gather remaining entities to avoid processing deleted ones
        _sawmill.Info("Phase 2: Cleaning components from remaining entities");

        var remainingEntities = new HashSet<EntityUid>();

        if (_entityManager.TryGetComponent<MapGridComponent>(gridUid, out grid))
        {
            var gridBounds = grid.LocalAABB;
            var lookupSystem = _entitySystemManager.GetEntitySystem<EntityLookupSystem>();
            foreach (var entity in lookupSystem.GetEntitiesIntersecting(gridUid, gridBounds))
            {
                if (entity != gridUid) // Don't include the grid itself
                    remainingEntities.Add(entity);
            }
        }

        _sawmill.Info($"Found {remainingEntities.Count} remaining entities to clean components from");

        foreach (var entity in remainingEntities)
        {
            try
            {
                // Check if entity still exists before processing
                if (!_entityManager.EntityExists(entity))
                    continue;

                // Remove session-specific components that shouldn't be saved
                if (_entityManager.RemoveComponent<ActorComponent>(entity))
                    componentsRemoved++;
                if (_entityManager.RemoveComponent<EyeComponent>(entity))
                    componentsRemoved++;

                // Remove vending machine behavior but keep the entity to preserve physics
                if (_entityManager.RemoveComponent<VendingMachineComponent>(entity))
                    componentsRemoved++;

                // Note: Removed PhysicsComponent deletion that was causing collision issues in loaded ships
                // PhysicsComponent and FixturesComponent are needed for proper collision detection

                // Reset power components to clean state through the proper system
                if (_entityManager.TryGetComponent<BatteryComponent>(entity, out var battery))
                {
                    // Use the battery system instead of direct access
                    if (_entitySystemManager.TryGetEntitySystem<BatterySystem>(out var batterySystem))
                    {
                        batterySystem.SetCharge(entity, battery.MaxCharge);
                    }
                }

                // Remove problematic atmospheric state
                if (_entityManager.RemoveComponent<AtmosDeviceComponent>(entity))
                    componentsRemoved++;

                // Remove any other problematic components
                // Note: We're being conservative here - removing things that commonly cause issues
            }
            catch (Exception ex)
            {
                _sawmill.Warning($"Error cleaning entity {entity}: {ex}");
            }
        }

        _sawmill.Info($"Grid cleanup complete: deleted {entitiesRemoved} entities, removed {componentsRemoved} components from {remainingEntities.Count} remaining entities");
    }

    /// <summary>
    /// Writes YAML data to a temporary file in UserData for loading
    /// </summary>
    public async Task<bool> WriteYamlToUserData(string fileName, string yamlData)
    {
        try
        {
            var userDataPath = _resourceManager.UserData;
            var resPath = new ResPath(fileName);

            await using var stream = userDataPath.OpenWrite(resPath);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(yamlData);

            _sawmill.Info($"Temporary YAML file written: {resPath}");
            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to write temporary YAML file {fileName}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to delete a file with retry logic to handle file access conflicts
    /// </summary>
    private async Task TryDeleteFileWithRetry(ResPath filePath, int maxRetries = 3, int delayMs = 100)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                _resourceManager.UserData.Delete(filePath);
                _sawmill.Info($"Successfully deleted temporary server file {filePath}");
                return;
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                _sawmill.Warning($"File deletion attempt {attempt + 1} failed for {filePath}: {ex.Message}. Retrying in {delayMs}ms...");
                await Task.Delay(delayMs);
                delayMs *= 2; // Exponential backoff
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to delete temporary file {filePath} on attempt {attempt + 1}: {ex}");
                if (attempt == maxRetries - 1)
                {
                    _sawmill.Error($"Giving up on deleting {filePath} after {maxRetries} attempts");
                }
                break;
            }
        }
    }

}
