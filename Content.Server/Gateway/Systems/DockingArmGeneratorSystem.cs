using System.Linq;
using System.Numerics;
using Content.Server.Gateway.Components;
using Content.Server.Parallax;
using Content.Shared.CCVar;
using Content.Shared.Dataset;
using Content.Shared.Gateway;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Gateway.Systems;

/// <summary>
/// Generates docking arm destinations that spawn specific grid structures.
/// When a destination is selected, spawns the grid from a YAML file onto the current map.
/// </summary>
public sealed class DockingArmGeneratorSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GatewaySystem _gateway = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // Optional name dataset - will fall back to default names if not found
    private const string DockingArmNames = "names_borer";

    private static readonly string[] DefaultDockingArmNames = new[]
    {
        "Docking Arm Alpha",
        "Docking Arm Beta",
        "Docking Arm Gamma",
        "Docking Arm Delta",
        "Docking Arm Epsilon",
        "Docking Arm Zeta",
        "Docking Arm Eta",
        "Docking Arm Theta",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DockingArmGeneratorComponent, MapInitEvent>(OnGeneratorMapInit);
        SubscribeLocalEvent<DockingArmGeneratorComponent, ComponentShutdown>(OnGeneratorShutdown);
        SubscribeLocalEvent<DockingArmDestinationComponent, AttemptGatewayOpenEvent>(OnDockingArmAttemptOpen);
        SubscribeLocalEvent<DockingArmDestinationComponent, GatewayOpenEvent>(OnDockingArmOpen);
    }

    private void OnGeneratorShutdown(EntityUid uid, DockingArmGeneratorComponent component, ComponentShutdown args)
    {
        foreach (var genUid in component.Generated)
        {
            if (Deleted(genUid))
                continue;

            QueueDel(genUid);
        }
    }

    private void OnGeneratorMapInit(EntityUid uid, DockingArmGeneratorComponent generator, MapInitEvent args)
    {
        if (!_cfgManager.GetCVar(CCVars.GatewayGeneratorEnabled))
            return;

        // Create a single persistent "Spawn New Dock" option
        GeneratePersistentSpawnButton(uid, generator);
    }

    private void GeneratePersistentSpawnButton(EntityUid uid, DockingArmGeneratorComponent? generator = null)
    {
        if (!Resolve(uid, ref generator))
            return;

        if (generator.DockingArmGrids.Count == 0)
        {
            Log.Warning($"DockingArmGeneratorComponent on {ToPrettyString(uid)} has no docking arm grids configured!");
            return;
        }

        // Create a single persistent spawn button entity
        var destUid = Spawn(null, MapCoordinates.Nullspace);
        _metadata.SetEntityName(destUid, "Spawn New Dock");

        var genDest = EnsureComp<DockingArmDestinationComponent>(destUid);
        genDest.Generator = uid;
        genDest.Name = "New Dock";
        genDest.GridPath = string.Empty; // Will be selected randomly on spawn
        genDest.Locked = false;

        // Add a gateway component so it shows up in the gateway list
        var gatewayComp = EnsureComp<GatewayComponent>(destUid);
        _gateway.SetDestinationName(destUid, FormattedMessage.FromMarkupOrThrow($"[color=#6495ED]Spawn New Dock[/color]"), gatewayComp);
        _gateway.SetEnabled(destUid, true, gatewayComp);

        generator.Generated.Add(destUid);
    }

    private string GenerateDockingArmName()
    {
        // Try to use the prototype if it exists
        if (_protoManager.TryIndex<LocalizedDatasetPrototype>(DockingArmNames, out var dataset))
        {
            return Loc.GetString(_random.Pick(dataset.Values));
        }

        // Fallback to default names
        return _random.Pick(DefaultDockingArmNames);
    }

    private void OnDockingArmAttemptOpen(Entity<DockingArmDestinationComponent> ent, ref AttemptGatewayOpenEvent args)
    {
        Log.Info($"OnDockingArmAttemptOpen called for {ToPrettyString(ent)} - Locked: {ent.Comp.Locked}, Cancelled: {args.Cancelled}");

        if (args.Cancelled)
        {
            Log.Info($"Cancelling attempt - previously cancelled");
            return;
        }

        // If there's no generator (manual destination), check if locked
        if (ent.Comp.Generator == null)
        {
            if (ent.Comp.Locked)
            {
                Log.Info($"Cancelling - manual destination is locked");
                args.Cancelled = true;
            }
            return;
        }

        // No cooldown check - allow spawning anytime
        Log.Info($"Allowing spawn - no cooldown restrictions");
    }

    private void OnDockingArmOpen(Entity<DockingArmDestinationComponent> ent, ref GatewayOpenEvent args)
    {
        Log.Info($"OnDockingArmOpen called for {ToPrettyString(ent)} - Source Gateway: {ToPrettyString(args.MapUid)}");
        
        // Check if we've hit the maximum dock limit
        if (ent.Comp.Generator != null && TryComp(ent.Comp.Generator, out DockingArmGeneratorComponent? genComp))
        {
            // Clean up deleted docks from the list
            genComp.SpawnedDocks.RemoveAll(dock => !Exists(dock));
            
            if (genComp.SpawnedDocks.Count >= genComp.MaxDocks)
            {
                Log.Warning($"Cannot spawn dock - maximum limit of {genComp.MaxDocks} docks reached");
                if (args.MapUid != null)
                    _popup.PopupEntity(Loc.GetString("gateway-docking-arm-max-limit"), args.MapUid);
                return;
            }
        }
        
        // Select a random grid path if not specified
        string gridPath = ent.Comp.GridPath;
        if (string.IsNullOrEmpty(gridPath) && ent.Comp.Generator != null && TryComp(ent.Comp.Generator, out DockingArmGeneratorComponent? generatorComp))
        {
            if (generatorComp.DockingArmGrids.Count > 0)
            {
                gridPath = _random.Pick(generatorComp.DockingArmGrids);
                Log.Info($"Selected random dock grid: {gridPath}");
            }
        }

        // Don't mark as loaded - keep the button available for reuse
        ent.Comp.Locked = false;

        // Load the docking arm grid
        SpawnDockingArmGrid(ent, args.MapUid, gridPath);
    }    /// <summary>
    /// Attempts to find a clear spawn location near the gateway.
    /// Checks multiple positions in a spiral pattern until a clear spot is found.
    /// </summary>
    private bool TryFindClearSpawnLocation(MapId mapId, Vector2 preferredPosition, float gridRadius, out Vector2 clearPosition, int maxAttempts = 12)
    {
        clearPosition = preferredPosition;

        // Check if preferred position is clear
        if (IsSpawnLocationClear(mapId, preferredPosition, gridRadius))
        {
            return true;
        }

        // Try positions in a spiral pattern around the preferred location
        var random = new Random(_random.Next());
        for (int attempt = 1; attempt < maxAttempts; attempt++)
        {
            var distance = 30 + (attempt * 15); // Increase distance with each attempt
            var angle = random.NextAngle();
            var testPosition = preferredPosition + angle.ToVec() * distance;

            if (IsSpawnLocationClear(mapId, testPosition, gridRadius))
            {
                clearPosition = testPosition;
                Log.Info($"Found clear spawn location at {testPosition} after {attempt} attempts (distance: {distance} tiles)");
                return true;
            }
        }

        Log.Warning($"Could not find clear spawn location after {maxAttempts} attempts");
        return false;
    }

    /// <summary>
    /// Checks if a potential spawn location is clear of other grids.
    /// </summary>
    private bool IsSpawnLocationClear(MapId mapId, Vector2 position, float radius)
    {
        // Create a bounding box around the spawn position
        var box = new Box2(position - new Vector2(radius, radius), position + new Vector2(radius, radius));

        // Check if any grids intersect this area
        if (_mapManager.FindGridsIntersecting(mapId, box).Any())
        {
            Log.Debug($"Spawn location at {position} blocked by intersecting grid(s)");
            return false;
        }

        return true;
    }

    private void SpawnDockingArmGrid(Entity<DockingArmDestinationComponent> ent, EntityUid? sourceGatewayUid, string gridPath)
    {
        Log.Info($"SpawnDockingArmGrid called - GridPath: {gridPath}, SourceGateway: {ToPrettyString(sourceGatewayUid ?? EntityUid.Invalid)}");

        if (string.IsNullOrEmpty(gridPath))
        {
            Log.Error($"No grid path provided for dock spawn!");
            return;
        }

        // Determine target map - use the source gateway's map if provided
        MapId targetMapId;
        Vector2 preferredPosition = Vector2.Zero;
        Angle spawnAngle = Angle.Zero;

        if (sourceGatewayUid != null && TryComp<TransformComponent>(sourceGatewayUid, out var sourceXform))
        {
            targetMapId = sourceXform.MapID;
            // Calculate initial preferred position near the gateway
            var random = new Random(_random.Next());
            var spawnDistance = random.Next(20, 40);
            spawnAngle = random.NextAngle();
            preferredPosition = sourceXform.WorldPosition + spawnAngle.ToVec() * spawnDistance;

            Log.Info($"Initial spawn position {preferredPosition} on map {targetMapId}, distance {spawnDistance} from gateway");
        }
        else
        {
            Log.Error($"No source gateway provided for docking arm spawn!");
            return;
        }

        // Find a clear spawn location (assume typical dock is roughly 30 tiles radius)
        const float estimatedDockRadius = 30f;
        if (!TryFindClearSpawnLocation(targetMapId, preferredPosition, estimatedDockRadius, out var spawnPosition))
        {
            Log.Error($"Could not find clear space to spawn dock - area around gateway is too crowded!");
            _popup.PopupEntity(Loc.GetString("gateway-docking-arm-no-space"), sourceGatewayUid.Value);
            return;
        }

        // Load the grid at the clear position
        Log.Info($"Attempting to load grid from {gridPath} at cleared position {spawnPosition}...");
        if (_loader.TryLoadGrid(targetMapId, new ResPath(gridPath), out var dockingArmGrid, offset: spawnPosition.Floored()))
        {
            // Get the generator component to access and increment the dock counter
            var dockNumber = 1;
            if (ent.Comp.Generator != null && TryComp<DockingArmGeneratorComponent>(ent.Comp.Generator, out var genComp))
            {
                dockNumber = genComp.DockCounter++;
            }

            var dockName = $"Dock {dockNumber}";
            _metadata.SetEntityName(dockingArmGrid.Value, dockName);

            // Add IFF component so the dock shows up on mass scanners
            EnsureComp<IFFComponent>(dockingArmGrid.Value);
            
            // Track this dock in the generator component
            if (ent.Comp.Generator != null && TryComp<DockingArmGeneratorComponent>(ent.Comp.Generator, out genComp))
            {
                genComp.SpawnedDocks.Add(dockingArmGrid.Value);
            }

            // Rotate the dock to face toward the gateway/station (perpendicular to spawn direction)
            // Add 90 degrees (π/2) to make it face tangentially, forming a ring pattern
            if (TryComp<TransformComponent>(dockingArmGrid.Value, out var dockXform))
            {
                var rotationToGateway = (sourceXform.WorldPosition - spawnPosition).ToAngle();
                var tangentialRotation = rotationToGateway + Angle.FromDegrees(90);
                // Subtract an additional 90 degrees to account for the dock's default orientation
                var finalRotation = tangentialRotation - Angle.FromDegrees(-90);
                _transform.SetWorldRotation(dockXform, finalRotation);

                Log.Info($"Rotated dock to {finalRotation.Degrees}° (perpendicular to gateway direction, adjusted for dock orientation)");
            }

            // Update all gateway UIs so the new dock appears as a destination
            _gateway.UpdateAllGateways();

            Log.Info($"Successfully spawned dock grid '{dockName}' (UID: {dockingArmGrid.Value}) from {gridPath} at {spawnPosition} on map {targetMapId}");
        }
        else
        {
            Log.Error($"Failed to load docking arm grid from {gridPath}!");
        }
    }
}
