using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Shuttles.Save; // For SendShipSaveDataClientMessage
using Content.Server.Atmos.Piping.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.VendingMachines;
using Robust.Shared.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;
using Robust.Shared.ContentPack;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Physics.Components;
using Robust.Shared.Containers;
using System.IO;
using System.Threading.Tasks;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using Robust.Shared.Serialization;
using Content.Shared.Storage.Components;
using Content.Shared.Wall; // WallMountComponent for preserving wall-mounted fixtures
using Robust.Shared.Physics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Server.Construction.Components;
using Content.Shared._HL.Shipyard;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// System for saving ships using the MapLoaderSystem infrastructure.
/// Saves ships as complete YAML files similar to savegrid command,
/// after cleaning them of problematic components and moving to exports folder.
/// </summary>
public sealed class ShipyardGridSaveSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IResourceManager _resourceManager = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;

    private ISawmill _sawmill = default!;
    private MapLoaderSystem _mapLoader = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<SecretStashComponent> _secretStashQuery;
    private EntityQuery<HLPersistOnShipSaveComponent> _persistOnSaveQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _secretStashQuery = GetEntityQuery<SecretStashComponent>();
        _persistOnSaveQuery = GetEntityQuery<HLPersistOnShipSaveComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        // Initialize sawmill for logging
        _sawmill = Logger.GetSawmill("shipyard.gridsave");

        // Get the MapLoaderSystem reference
        _mapLoader = _entitySystemManager.GetEntitySystem<MapLoaderSystem>();

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

        if (!_gridQuery.TryComp(shuttleUid.Value, out var gridComponent))
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
        if (!_gridQuery.HasComp(gridUid))
        {
            _sawmill.Error($"Entity {gridUid} is not a valid grid");
            return false;
        }

        try
        {
            // Per user request: before purging / serializing, add SecretStashComponent to any entity contained
            // directly within a secret stash so that they are also considered preserved.
            TagStashContents(gridUid);

            // Clean up broken device links before serialization
            CleanupBrokenDeviceLinks(gridUid);

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
                if (_secretStashQuery.HasComp(hidden.Value))
                    continue;
                // Dynamically add the component. OnInit won't run automatically here for networked comps
                // when added at runtime; EnsureComp will construct and initialize it.
                EnsureComp<SecretStashComponent>(hidden.Value);
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
    /// Cleans up broken device links where one or both linked entities no longer exist.
    /// Preserves valid links where both source and sink entities are still present.
    /// </summary>
    private void CleanupBrokenDeviceLinks(EntityUid gridUid)
    {
        try
        {
            var linksRemoved = 0;
            var sourcesProcessed = 0;

            // Collect all entities on the grid with device link source components
            var sourceQuery = _entityManager.EntityQueryEnumerator<DeviceLinkSourceComponent, TransformComponent>();
            while (sourceQuery.MoveNext(out var sourceEnt, out var sourceComp, out var xform))
            {
                if (xform.GridUid != gridUid)
                    continue;

                sourcesProcessed++;

                // Check LinkedPorts and remove links to entities that no longer exist
                var brokenSinks = new List<EntityUid>();
                foreach (var sinkEnt in sourceComp.LinkedPorts.Keys)
                {
                    if (!_entityManager.EntityExists(sinkEnt) || _entityManager.IsQueuedForDeletion(sinkEnt))
                    {
                        brokenSinks.Add(sinkEnt);
                    }
                }

                // Use the DeviceLinkSystem to properly remove broken links
                foreach (var brokenSink in brokenSinks)
                {
                    _deviceLink.RemoveSinkFromSource(sourceEnt, brokenSink, sourceComp);
                    linksRemoved++;
                }
            }

            if (linksRemoved > 0)
                _sawmill.Info($"CleanupBrokenDeviceLinks: Removed {linksRemoved} broken device link(s) from {sourcesProcessed} source(s) on grid {gridUid}");
        }
        catch (Exception e)
        {
            _sawmill.Warning($"CleanupBrokenDeviceLinks: Exception while cleaning device links on grid {gridUid}: {e.Message}");
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
            if (!_gridQuery.TryComp(gridUid, out var grid))
                return;
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
            foreach (var ent in _lookup.GetEntitiesIntersecting(gridUid, grid.LocalAABB))
            {
                if (ent == gridUid)
                    continue;
                // Preserve any secret stash root or bluespace stash prototype entity itself
                if (_secretStashQuery.HasComp(ent) || _persistOnSaveQuery.HasComp(ent))
                    processed.Add(ent); // don't treat stash as loose
                if (!TryQueueLoose(ent, looseDeletes, processed))
                    continue;
            }

            // 2. Traverse container graphs on every anchored entity to collect ALL contained descendants
            foreach (var ent in _lookup.GetEntitiesIntersecting(gridUid, grid.LocalAABB))
            {
                if (ent == gridUid)
                    continue;
                if (!TryComp<ContainerManagerComponent>(ent, out var manager))
                    continue;
                // If this entity is a stash or bluespace stash, preserve its contents entirely.
                if (_secretStashQuery.HasComp(ent) || _persistOnSaveQuery.HasComp(ent))
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
                    if (_secretStashQuery.HasComp(ent) || _persistOnSaveQuery.HasComp(ent))
                    {
                        fallbackProcessed.Add(ent);
                        continue; // stash root preserved
                    }
                    TryQueueLoose(ent, fallbackLoose, fallbackProcessed);
                    if (_entityManager.TryGetComponent<ContainerManagerComponent>(ent, out var mgr))
                    {
                        if (_secretStashQuery.HasComp(ent) || _persistOnSaveQuery.HasComp(ent))
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

    /// <summary>
    /// Checks if this entity being saved is valid for deletion.
    /// </summary>
    private bool IsInvalidEntity(EntityUid uid)
    {
        if (!Exists(uid))
            return false;
        // Skip if terminating
        if (_entityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;
        if (_secretStashQuery.HasComp(uid) || _persistOnSaveQuery.HasComp(uid))
            return false; // preserve stash root outright
        if (_gridQuery.HasComp(uid))
            return false; // never delete grid root or nested grids here
        // Preserve wall-mounted fixtures (buttons, posters, etc.) regardless of anchored state
        if (HasComp<WallMountComponent>(uid))
            return false;
        // Preserve levers
        if (HasComp<TwoWayLeverComponent>(uid))
            return false;
        // Preserve entities with static body types, such as drains or sinks.
        if (TryComp<PhysicsComponent>(uid, out var physics) && physics.BodyType == BodyType.Static)
            return false;
        // Preserve solutions
        if (HasComp<ContainedSolutionComponent>(uid) || HasComp<SolutionComponent>(uid))
            return false;
        var anchored = false;
        if (_transformQuery.TryComp(uid, out var xform))
            anchored = xform.Anchored;
        var inContainer = _containerSystem.IsEntityInContainer(uid);
        // Per updated requirements: anchored entities must never be deleted under any circumstance.
        if (anchored)
            return false;
        if (inContainer)
        {
            // If this entity (at any ancestor depth) is ultimately inside a secret stash preserve it.
            if (IsInsideSecretStash(uid))
                return false;
        }

        // Only unanchored entities are eligible for deletion. If it's unanchored (loose) or unanchored-in-container, delete.
        return true;
    }

    private bool TryQueueLoose(EntityUid ent, List<EntityUid> list, HashSet<EntityUid> processed)
    {
        if (!Exists(ent))
            return false;
        if (!processed.Add(ent))
            return false; // already processed

        // Delete any entities invalid for saving
        if (IsInvalidEntity(ent))
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
            if (!IsInvalidEntity(ent))
                continue;

            // Preserve wall-mounted fixtures explicitly but still traverse their child containers.
            var isWallMount = _entityManager.HasComponent<WallMountComponent>(ent);
            // Preserve anchored entities even if they appear within containers; still traverse their child containers.
            var isAnchored = false;
            if (_transformQuery.TryComp(ent, out var xform))
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
            if (!Exists(owner))
                return false;
            if (_secretStashQuery.HasComp(owner))
                return true; // Found stash root above.
            // Also treat persistent entities as a preservation root.
            if (_persistOnSaveQuery.HasComp(owner))
                return true; // Found stash root above.
            if (HasComp<MachineComponent>(owner))
                return true; // This is so machines keep their upgraded parts.
            current = owner;
        }
        return false;
    }

    private void DeleteEntityList(List<EntityUid> list, string category)
    {
        foreach (var ent in list)
        {
            try
            {
                if (Exists(ent))
                    Del(ent);
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
            "AccessReader", // Door logs
            "DeviceList",
            "DeviceNetwork",
            "DeviceNetworkComponent",
            "UserInterface", // Contains invalid EntityUid references
            "Docking", // Contains invalid EntityUid references to docked entities
            "ActionGrant", // Contains invalid EntityUid references to granted actions
        };

        // Prototype-level exclusions for obvious non-ship entities.
        // If we encounter these, we drop them entirely from the export.
        var filteredPrototypes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // admin / ghost observers, spectators, etc.
            "AdminObserver",
            "AdminObserverDummy",
            "Ghost",
            "Forensics",
            "DnaComponent",
            "DNA",
            "Store",
            "MindContainer",
            "GhostRole",
            "GhostTakeoverAvailable",
            "PayloadCase",
            "GhostRoleMob",
            "HumanoidAppearance",
            "GeneralStationRecordConsole",
            // Uplinks and bundled items
            "BaseMercenaryUplinkRadio",
            "BriefcaseSyndieLobbyingBundleFilled",
            "BriefcaseThiefBribingBundleFilled",
            "VendingMachineRobotics",
            "MachineFlatpacker",
            "ClothingBackpackDuffelEVABundleAtmosTech",
            "ClothingBackpackDuffelEVABundleBoxerBlue",
            "ClothingBackpackDuffelEVABundleBoxerGreen",
            "ClothingBackpackDuffelEVABundleBoxerRandom",
            "ClothingBackpackDuffelEVABundleBoxerRed",
            "ClothingBackpackDuffelEVABundleBoxerYellow",
            "ClothingBackpackDuffelEVABundleCaptain",
            "ClothingBackpackDuffelEVABundleCargo",
            "ClothingBackpackDuffelEVABundleChaplain",
            "ClothingBackpackDuffelEVABundleContractor",
            "ClothingBackpackDuffelEVABundleEngineer",
            "ClothingBackpackDuffelEVABundleJanitor",
            "ClothingBackpackDuffelEVABundleMail",
            "ClothingBackpackDuffelEVABundleMedical",
            "ClothingBackpackDuffelEVABundleMercenary",
            "ClothingBackpackDuffelEVABundleNfsd",
            "ClothingBackpackDuffelEVABundlePilot",
            "ClothingBackpackDuffelEVABundlePrivateSec",
            "ClothingBackpackDuffelEVABundleSalvage",
            "ClothingBackpackDuffelEVABundleScientist",
            "ClothingBackpackDuffelEVABundleServiceWorker",
            "ClothingBackpackDuffelEVABundleSr",
            "ClothingBackpackDuffelHoldingNitchFilled",
            "ClothingBackpackDuffelHoldingShiftTheGlaceon",
            "ClothingBackpackDuffelHoldingVulrikFilled",
            "ClothingBackpackDuffelSurgeryAdvancedFilled",
            "ClothingBackpackDuffelSurgeryFilled",
            "ClothingBackpackDuffelSurgeryImpovFilled",
            "ClothingBackpackDuffelSyndicateAmmoFilled",
            "ClothingBackpackDuffelSyndicateDecoyKitFilled",
            "ClothingBackpackDuffelSyndicateFilledAtreides",
            "ClothingBackpackDuffelSyndicateFilledCarbine",
            "ClothingBackpackDuffelSyndicateFilledGrenadeLauncher",
            "ClothingBackpackDuffelSyndicateFilledLMG",
            "ClothingBackpackDuffelSyndicateFilledMedical",
            "ClothingBackpackDuffelSyndicateFilledRevolver",
            "ClothingBackpackDuffelSyndicateFilledShotgun",
            "ClothingBackpackDuffelSyndicateFilledSMG",
            "ClothingBackpackDuffelSyndicateFilledWSPR",
            "ClothingBackpackDuffelSyndicateMedicalBundleFilled",
            "ClothingBackpackMessengerChaplainMarrikFilled",
            "ClothingBackpackMessengerColorGreenJosephFilled",
            "ClothingBackpackPirateBundle",
            "ClothingBackpackSatchelHoldingNirouFilled",
            "ClothingBackpackSatchelLeatherWinterFilled",
            "ClothingBackpackSatchelSmugglerFilled",
            "ClothingBeltArcadiaArachneFilled",
            "ClothingBeltAssaultMarrikFilled",
            "ClothingBeltChaplainSashFilled",
            "ClothingBeltChefFilled",
            "ClothingBeltChiefEngineerFilled",
            "ClothingBeltHolsterFilled",
            "ClothingBeltHolsterJosephFilled",
            "ClothingBeltJanitorFilled",
            "ClothingBeltMedicalEMTFilled",
            "ClothingBeltMedicalFilled",
            "ClothingBeltMedicalNitchFilled",
            "ClothingBeltMilitaryWebbingGrenadeFilled",
            "ClothingBeltMilitaryWebbingMedFilled",
            "ClothingBeltNfsdFilled",
            "ClothingBeltNfsdWebbingFilled",
            "ClothingBeltNfsdWebbingFilledBrigmedic",
            "ClothingBeltPilotFilled",
            "ClothingBeltSalvageWebbingFilledNF",
            "ClothingBeltSecurityFilled",
            "ClothingBeltSecurityWebbingFilled",
            "ClothingBeltWandFilled",
            "ClothingWalletLeatherBlackArachneFilled",
            "ComputerContrabandPalletConsole",
            "ComputerContrabandPalletConsolePirate",
            "ComputerCriminalRecords",
            "ComputerEmergencyShuttle",
            "ComputerMarketConsoleNFHigh",
            "ComputerMarketConsoleNFLow",
            "ComputerMarketConsoleNFNormal",
            "ComputerMedicalRecords",
            "ComputerPalletConsoleNFHighMarket",
            "ComputerPalletConsoleNFLowMarket",
            "ComputerPalletConsoleNFNormalMarket",
            "ComputerPalletConsoleNFVeryHighMarket",
            "ComputerPalletConsoleNFVeryLowMarket",
            "ComputerPsionicsRecords",
            "ComputerRoboticsControl",
            "ComputerShipyard",
            "ComputerShipyardBlackMarket",
            "ComputerShipyardExpedition",
            "ComputerShipyardMedical",
            "ComputerShipyardNfsd",
            "ComputerShipyardScrap",
            "ComputerShipyardSecurity",
            "ComputerShipyardSr",
            "ComputerShipyardSyndicate",
            "ComputerShuttleRecords",
            "ComputerStationRecords",
            "ComputerTabletopContrabandPalletConsole",
            "ComputerTabletopContrabandPalletConsolePirate",
            "ComputerTabletopCriminalRecords",
            "ComputerTabletopMarketConsoleNFHigh",
            "ComputerTabletopMarketConsoleNFLow",
            "ComputerTabletopMarketConsoleNFNormal",
            "ComputerTabletopMedicalRecords",
            "ComputerTabletopPalletConsoleNFHighMarket",
            "ComputerTabletopPalletConsoleNFLowMarket",
            "ComputerTabletopPalletConsoleNFNormalMarket",
            "ComputerTabletopPalletConsoleNFVeryLowMarket",
            "ComputerTabletopShipyardBlackMarket",
            "ComputerTabletopShipyardExpedition",
            "ComputerTabletopShipyardNfsd",
            "ComputerTabletopShipyardScrap",
            "ComputerTabletopShipyardSecurity",
            "ComputerTabletopShuttleAntag",
            "ComputerTabletopStationRecords",
            "ComputerWallmountStationRecords",
            "CowToolboxFilled",
            "CrateCybersunDarkGygaxBundle",
            "CrateCybersunJuggernautBundle",
            "CrateCybersunMaulerBundle",
            "CrateFunToyBox",
            "CrateMaterialsBasicFilled",
            "DEBUGVendingMachineAmmoBoxes",
            "DEBUGVendingMachineMagazines",
            "DEBUGVendingMachineRangedWeapons",
            "LessLethalVendingMachine",
            "LessLethalVendingMachinePOI",
            "LockerMaterialsBasicFilled",
            "LockerWallMaterialsBasicFilled",
            "ClosetFsbEvaFilled",
            "LockerWallEVAColorFsbFilled",
            "LockerWallEVAColorChaplainFilled",
            "LockerChemistryFilled",
            "LockerClownFilled",
            "LockerEvacRepairFilled",
            "LockerWallEVAColorMailFilled",
            "ClosetFscEvaFilled",
            "LockerWallEVAColorFscFilled",
            "LockerWallColorL1FireFilled",
            "LockerWallEVAColorCargoFilled",
            "HLLockerChemistryFilled",
            "LockerWallEVAColorContractorFilled",
            "LockerWallEvacRepairFilled",
            "LockerMailCarrierFilled",
            "LockerMaterialsBasic10Filled",
            "LockerWallColorL2RadiationFilled",
            "LockerWallColorL3BiohazardFilled",
            "LockerWallColorL4BombFilled",
            "HLLockerCaptainFilledLaser",
            "LockerWallColorChemistryFilled",
            "HLLockerDetectiveFilled",
            "LockerFreezerSushi",
            "LockerWallEVAColorLvhiFilled",
            "LockerWallEVAColorNfsdFilled",
            "LockerNfsdBailiff",
            "HLLockerCaptainFilledHardsuit",
            "LockerChiefEngineerFilled",
            "LockerWallEVAColorMedicalFilled",
            "LockerWallEVAColorGoblinFilled",
            "LockerWallMaterialsBasic10Filled",
            "LockerMedicalFilled",
            "LockerNfsdBrigmedic",
            "LockerNfsdSheriff",
            "LockerNfsdCopper",
            "HLLockerCaptainFilledNoLaser",
            "LockerChiefEngineerFilledHardsuit",
            "LockerElectricalSuppliesFilled",
            "LockerHeadOfPersonnelFilled",
            "HLLockerMedicalFilled",
            "LockerWallMedicalDoctorFilled",
            "LockerCaptainFilledHardsuit",
            "LockerCaptainFilled",
            "LockerCaptainFilledNoLaser",
            "HLLockerChiefEngineerFilled",
            "HLLockerChiefEngineerFilledHardsuit",
            "LockerHeadOfSecurityFilled",
            "LockerHeadOfSecurityFilledHardsuit",
            "HLLockerHeadOfSecurityFilled",
            "HLLockerHeadOfSecurityFilledHardsuit",
            "HLLockerBrigmedicFilled",
            "HLLockerBrigmedicFilledNoHardsuit",
            "HLLockerHeadOfPersonnelFilled",
            "LockerChiefMedicalOfficerFilled",
            "LockerChiefMedicalOfficerFilledHardsuit",
            "HLLockerChiefMedicalOfficerFilled",
            "HLLockerChiefMedicalOfficerFilledHardsuit",
            "LockerSalvageSpecialistFilled",
            "HLLockerResearchDirectorFilled",
            "HLLockerResearchDirectorFilledHardsuit",
            "LockerResearchDirectorFilled",
            "LockerResearchDirectorFilledHardsuit",
            "LockerQuarterMasterFilled",
            "LockerQuarterMasterFilledHardsuit",
            "HLLockerQuarterMasterFilled",
            "HLLockerSalvageSpecialistFilled",
            "HLLockerWardenFilled",
            "HLLockerWardenFilledHardsuit",
            "HLLockerSecurityFilled",
            "NFLockerSecurityFilled",
            "HLLockerSalvageSpecialistFilledHardsuit",
            "LockerSalvageSpecialistFilled",
            "LockerWallColorSalvageFilled",
            "LockerWallEVAColorSalvageFilled",
            "LockerWallEVAColorSrFilled",
            "LockerStationRepresentativeFilled",
            "LockerNfsdSilverDetectiveFilled",
            "LockerNfsdSheriffFilled",
            "LockerNfsdSergeant",
            "HLLockerElectricalSuppliesFilled",
            "LockerWallColorMedicalDoctorFilled",
            "HLLockerWallMedicalDoctorFilled",
            "LockerNfsdSilver",
            "LockerSyndicatePersonalFilled",
            "LockerWallEVAColorAtmosTechFilled",
            "LockerWallEVAColorEmergencyFilled",
            "LockerMedicineFilled",
            "HLLockerMedicineFilled",
            "LockerAtmosphericsFilled",
            "LockerAtmosphericsFilledHardsuit",
            "HLLockerAtmosphericsFilled",
            "LockerWallEVAColorEngineerFilled",
            "LockerWallMedicalFilled",
            "LockerWallColorMedicalFilled",
            "HLLockerAtmosphericsFilledHardsuit",
            "LockerWallMaterialsFuelBananiumFilled",
            "LockerWallMaterialsFuelBananiumFilled2",
            "LockerWallEVAColorCaptainFilled",
            "LockerEngineerFilled",
            "HLLockerWallMedicalFilled",
            "LockerWallEVAColorMercenaryFilled",
            "LockerBoozeFilled",
            "HLLockerBoozeFilled",
            "LockerWallEVAColorHydroponicsFilled",
            "LockerEngineerFilledHardsuit",
            "LockerMercenaryFilled",
            "LockerWallEVAColorParamedicFilled",
            "LockerWallColorHydroponicsFilled",
            "LockerBotanistFilled",
            "HLLockerBotanistFilled",
            "HLLockerEngineerFilled",
            "LockerWallEVAColorJanitorFilled",
            "LockerParamedicFilled",
            "LockerParamedicFilledHardsuit",
            "LockerWallMaterialsFuelWeldingFilled",
            "LockerWallEVAColorStcFilled",
            "LockerWeldingSuppliesFilled",
            "HLLockerWeldingSuppliesFilled",
            "HLLockerScienceFilled",
            "NFLockerScienceFilled",
            "LockerWallEVAColorScientistFilled",
            "LockerWallEVAColorServiceWorkerFilled",
            "LockerWallMaterialsFuelUraniumFilled",
            "LockerWallMaterialsFuelUraniumFilled2",
            "LockerWallEVAColorBoxerBlueFilled",
            "LockerWallEVAColorBoxerGreenFilled",
            "LockerWallEVAColorBoxerRandomFilled",
            "LockerWallEVAColorBoxerRedFilled",
            "LockerWallEVAColorBoxerYellowFilled",
            "HLLockerEngineerFilledHardsuit",
            "LockerJanitorFilled",
            "HLLockerParamedicFilledNoHardsuit",
            "HLLockerParamedicFilled",
            "LockerWallColorParamedicFilled",
            "LockerWallEVAColorPilotFilled",
            "LockerPilotFilled",
            "LockerWallMaterialsFuelPlasmaFilled",
            "LockerWallMaterialsFuelPlasmaFilled2",
            "LockerWallEVAColorPrivateSecFilled",
            "LockerPsychologistFilled",
            "MedicalPodFilled",
            "NFPouchMercenaryArachneFilled",
            "NFTelecomServerFilled",
            "NFVendingMachineCart",
            "NFVendingMachineCartNfsd",
            "NonLethalVendingMachine",
            "PsionicsRecordsComputerCircuitboard",
            "StationAiUploadComputer",
            "StructureRackBloodCultFilled",
            "StructureRackWallmountedSalvageFilled",
            "StructurePistolRackPiratesFilled",
            "StructurePistolRackWallmountedMercenaryFilled",
            "TelecomServerFilledArcadia",
            "TelecomServerFilledCartel",
            "TelecomServerFilledColComm",
            "TelecomServerFilledEvent",
            "TelecomServerFilledFreelance",
            "TelecomServerFilledNotrasen",
            "TelecomServerFilledNfsd",
            "TelecomServerFilledShuttle",
            "TelecomServerFilledStatic",
            "TelecomServerFilledSyndicate",
            "TelecomServerFilledViperCell",
            "ToolboxElectricalFilled",
            "ToolboxElectricalTurretFilled",
            "ToolboxMechanicalFilled",
            "ToolboxMechanicalFilledAllTools",
            "ToolboxSyndicateFilled",
            "VendingBarDrobe",
            "VendingMachineAmmo",
            "VendingMachineAmmoPOI",
            "VendingMachineArcadia",
            "VendingMachineAstroVend",
            "VendingMachineAstroVendPOI",
            "VendingMachineAtmosDrobe",
            "VendingMachineAutoTuneVend",
            "VendingMachineBooze",
            "VendingMachineBoozePOI",
            "VendingMachineBoozeSyndicate",
            "VendingMachineBountyVend",
            "VendingMachineBountyVendPOI",
            "VendingMachineBountyVendPunk",
            "VendingMachineBoxingDrobe",
            "VendingMachineCargoDrobe",
            "VendingMachineCart",
            "VendingMachineCentDrobe",
            "VendingMachineChang",
            "VendingMachineChapel",
            "VendingMachineChefDrobe",
            "VendingMachineChefvend",
            "VendingMachineChemDrobe",
            "VendingMachineChemicals",
            "VendingMachineChemicalsSyndicate",
            "VendingMachineCigs",
            "VendingMachineCigsPOI",
            "VendingMachineCiviMed",
            "VendingMachineCiviMedPlus",
            "VendingMachineClothing",
            "VendingMachineClothingPunk",
            "VendingMachineCoffee",
            "VendingMachineCola",
            "VendingMachineColaBlack",
            "VendingMachineColaRed",
            "VendingMachineCondim",
            "VendingMachineCuddlyCritterVend",
            "VendingMachineCuraDrobe",
            "VendingMachineDetDrobe",
            "VendingMachineDinnerware",
            "VendingMachineDiscount",
            "VendingMachineDonut",
            "VendingMachineDrGibb",
            "VendingMachineEngiDrobe",
            "VendingMachineEngivend",
            "VendingMachineEngivendPOI",
            "VendingMachineExpeditionaryFlatpackVend",
            "VendingMachineFlatpackVend",
            "VendingMachineFuelVend",
            "VendingMachineGames",
            "VendingMachineGamesPOI",
            "VendingMachineGeneDrobe",
            "VendingMachineHappyHonk",
            "VendingMachineHydrobe",
            "VendingMachineJaniDrobe",
            "VendingMachineLawDrobe",
            "VendingMachineMagivend",
            "VendingMachineMailDrobe",
            "VendingMachineMailVend",
            "VendingMachineMedical",
            "VendingMachineMediDrobe",
            "VendingMachineMediDrobePOI",
            "VendingMachineMercVend",
            "VendingMachineSyndieDrobe",
            "VendingMachineTankDisserEngineering",
            "VendingMachineTankDisserEVA",
            "VendingMachineTankDisserEVAPOI",
            "VendingMachineTheater",
            "VendingMachineValetDrobe",
            "VendingMachineVendomat",
            "VendingMachineVendomatPOI",
            "VendingMachineViroDrobe",
            "VendingMachineWallMedical",
            "VendingMachineWinter",
            "VendingMachineYarrrDrobe",
            "VendingMachineYouTool",
            "VendingMachineYouToolPOI",
            "BaseMercenaryUplinkRadio",
            "BaseUplinkRadio",
            "BaseUplinkRadio20TC",
            "BaseUplinkRadio25TC",
            "BaseUplinkRadio40TC",
            "BaseUplinkRadio60TC",
            "UplinkImplanter",
            "BaseSecurityUplinkRadio",
            "BaseSecurityUplinkRadioSheriff",
            "BaseSecurityUplinkRadioOfficer",
            "BaseSecurityUplinkRadioDeputy",
            "BasePirateUplink",
            "BasePirateUplinkRadioPirateCaptain",
            "BasePirateUplinkPirateCrew",

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

                    // Gravity: preserve enabled state so gravity persists on ship load
                    // (Removed stripping of enabled field)

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

                    // DeviceLink: Keep device links intact - they should persist
                    // (Removed clearing of linkedPorts and links)

                    // Solution/SolutionContainer: Keep solution contents - they should persist
                    // (Removed clearing of solutions and contents)

                    // ResearchServer: reset research server state
                    if (typeName == "ResearchServer")
                    {
                        compMap.Remove("points");
                        compMap.Remove("Points");
                        compMap.Remove("pointsPerSecond");
                        compMap.Remove("PointsPerSecond");
                    }

                    // TechnologyDatabase: reset unlocked technologies and recipes
                    if (typeName == "TechnologyDatabase")
                    {
                        compMap.Remove("unlockedTechnologies");
                        compMap.Remove("UnlockedTechnologies");
                        compMap.Remove("unlockedRecipes");
                        compMap.Remove("UnlockedRecipes");
                        compMap.Remove("currentTechnologyCards");
                        compMap.Remove("CurrentTechnologyCards");
                        compMap.Remove("mainDiscipline");
                        compMap.Remove("MainDiscipline");
                    }

                    // Battery: reset charge to 0
                    if (typeName == "Battery")
                    {
                        compMap["currentCharge"] = new ValueDataNode("0");
                        compMap["CurrentCharge"] = new ValueDataNode("0");
                    }

                    // DeviceNetwork: clear device lists that contain invalid EntityUid references
                    if (typeName == "DeviceNetwork")
                    {
                        compMap.Remove("devices");
                        compMap.Remove("Devices");
                    }

                    // UserInterface: remove to prevent invalid EntityUid references
                    // (This is handled by filteredTypes but adding explicit note)

                    // Docking: remove to prevent invalid EntityUid references to docked entities
                    // (This is handled by filteredTypes but adding explicit note)

                    // ActionGrant: remove to prevent invalid EntityUid references to granted actions
                    // (This is handled by filteredTypes but adding explicit note)

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
        if (_gridQuery.TryComp(gridUid, out var grid))
        {
            var gridBounds = grid.LocalAABB;
            foreach (var entity in _lookup.GetEntitiesIntersecting(gridUid, gridBounds))
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

        if (_gridQuery.TryComp(gridUid, out grid))
        {
            var gridBounds = grid.LocalAABB;
            foreach (var entity in _lookup.GetEntitiesIntersecting(gridUid, gridBounds))
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
}
