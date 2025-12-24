using Content.Shared.Shuttles.Components;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Content.Shared.Popups; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Server.Station.Components; // Frontier
using Robust.Shared.Map.Components; // Frontier
using Robust.Shared.Physics.Components; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Robust.Shared.Physics; // Frontier
using Content.Server.Chat.Systems; // HARDLIGHT: For ChatSystem (server-side)
using Content.Shared.Salvage; // HARDLIGHT: For SalvageMissionType
using System.Threading; // HARDLIGHT: For CancellationTokenSource
using System.Numerics; // HARDLIGHT: For Vector2
using Robust.Shared.Map; // HARDLIGHT: For EntityCoordinates
using Content.Server.Shuttles.Components; // HARDLIGHT: For ShuttleComponent
using System.Linq; // HARDLIGHT: For ToList() and Take()
using Content.Shared.Shuttles.Systems; // HARDLIGHT: For FTLState
using Robust.Shared.Player; // HARDLIGHT: For Filter
using Content.Shared.Timing; // HARDLIGHT: For StartEndTime
using Robust.Shared.GameObjects; // HARDLIGHT: For SpawnTimer extension method

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    [ValidatePrototypeId<EntityPrototype>]
    public const string CoordinatesDisk = "CoordinatesDisk";

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // Frontier
    [Dependency] private readonly ChatSystem _chatSystem = default!; // HARDLIGHT

    private const float ShuttleFTLMassThreshold = 50f; // Frontier
    private const float ShuttleFTLRange = 150f; // Frontier

    /// <summary>
    /// Gets or creates expedition data for the console's shuttle/grid.
    /// Station data is intentionally ignored; consoles are fully independent.
    /// </summary>
    public SalvageExpeditionDataComponent? GetStationExpeditionData(EntityUid consoleUid)
    {
        // Resolve the console's transform and grid; only grid-local data is used.
        var xform = Transform(consoleUid);
        var gridUid = xform.GridUid;
        if (gridUid == null)
            return null;

        // Ensure and return grid-local expedition data (independent of stations).
        if (TryComp<SalvageExpeditionDataComponent>(gridUid.Value, out var gridDataExisting))
        {
            if (gridDataExisting.Missions.Count == 0 && !gridDataExisting.GeneratingMissions && !gridDataExisting.Cooldown)
            {
                GenerateMissions(gridDataExisting);
            }
            return gridDataExisting;
        }

        var gridData = EnsureComp<SalvageExpeditionDataComponent>(gridUid.Value);
        gridData.Cooldown = false;
        gridData.CanFinish = false;
        gridData.ActiveMission = 0;
        gridData.CooldownTime = TimeSpan.Zero;
        gridData.NextOffer = _timing.CurTime;
        if (gridData.Missions.Count == 0 && !gridData.GeneratingMissions)
            GenerateMissions(gridData);
        return gridData;
    }

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        var data = GetStationExpeditionData(uid);
        if (data == null)
        {
            Log.Warning($"No station expedition data found for console {ToPrettyString(uid)}");
            PlayDenySound((uid, component));
            return;
        }

        // Set this console as the active console for the mission
        component.ActiveConsole = uid;

        // Skip if already claimed
        if (data.ActiveMission != 0)
        {
            Log.Warning($"Mission claim rejected for console {ToPrettyString(uid)}: ActiveMission={data.ActiveMission} already in progress");
            PlayDenySound((uid, component));
            UpdateConsole((uid, component));
            return;
        }

        // Check if the requested mission exists
        if (!data.Missions.TryGetValue(args.Index, out var missionparams))
        {
            Log.Warning($"Mission claim rejected for console {ToPrettyString(uid)}: RequestedIndex={args.Index} not found, MissionCount={data.Missions.Count}, Available=[{string.Join(",", data.Missions.Keys)}]");
            PlayDenySound((uid, component));

            // Force update console state to ensure client is synchronized
            UpdateConsole((uid, component));
            return;
        }

        // Find the grid this console is on
        if (!TryComp<TransformComponent>(uid, out var consoleXform))
        {
            Log.Error($"Console {ToPrettyString(uid)} has no transform component");
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), uid, PopupType.MediumCaution);
            UpdateConsole((uid, component));
            return;
        }

        var ourGrid = consoleXform.GridUid;
        if (ourGrid == null || !TryComp<MapGridComponent>(ourGrid, out var gridComp))
        {
            Log.Error($"Console {ToPrettyString(uid)} grid {ourGrid} has no map grid component");
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), uid, PopupType.MediumCaution);
            UpdateConsole((uid, component));
            return;
        }

        // Store reference to console in mission params for FTL completion tracking
        component.ActiveConsole = uid;

        // Directly spawn the mission - console is completely independent
        try
        {
            Log.Info($"Spawning mission {args.Index} ({missionparams.MissionType}) for independent console {ToPrettyString(uid)} on grid {ourGrid}");
            SpawnMissionForConsole(missionparams, ourGrid.Value, uid);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to spawn mission for console {ToPrettyString(uid)}: {ex}");
            return; // Don't mark as claimed if spawning failed
        }

        // Mark as claimed and active - console handles its own state
        data.ActiveMission = args.Index;
        // Do not forcibly reset CanFinish here; preserve existing early-leave availability

        var mission = GetMission(missionparams.MissionType, _prototypeManager.Index<SalvageDifficultyPrototype>(missionparams.Difficulty), missionparams.Seed);
        // Do not modify offer timers on claim to avoid regenerating/changing offers prematurely

        UpdateConsole((uid, component));

        // Announce to all players on this grid only
        if (consoleXform.GridUid != null)
        {
            var filter = Filter.Empty().AddInGrid(consoleXform.GridUid.Value);
            var announcement = Loc.GetString("salvage-expedition-announcement-claimed");
            _chatSystem.DispatchFilteredAnnouncement(filter, announcement, uid,
                sender: "Expedition Console", colorOverride: Color.LightBlue);
        }

        Log.Info($"Mission {args.Index} successfully claimed on independent console {ToPrettyString(uid)}");
    }

    // HARDLIGHT: manual refresh handler to re-link console with station expedition data
    private void OnSalvageRefreshMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, RefreshSalvageConsoleMessage args)
    {
        Log.Info($"Manual salvage console refresh requested for {ToPrettyString(uid)}");
        UpdateConsole((uid, component));
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        var data = GetStationExpeditionData(entity);
        if (data == null || !data.CanFinish)
        {
            PlayDenySound((entity, component));
            UpdateConsole((entity, component));
            return;
        }

        // Get the console's grid
        if (!TryComp(entity, out TransformComponent? xform))
        {
            PlayDenySound((entity, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsole((entity, component));
            return;
        }

        // Find the active expedition map that was created by this console
        EntityUid? expeditionMapUid = null;
        var expeditionQuery = EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (expeditionQuery.MoveNext(out var expUid, out var expeditionComp))
        {
            if (expeditionComp.Console == entity)
            {
                expeditionMapUid = expUid;
                break;
            }
        }

        if (expeditionMapUid == null)
        {
            Log.Warning($"Could not find active expedition for console {ToPrettyString(entity)}");
            PlayDenySound((entity, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            return;
        }

        // Disable finishing to prevent multiple clicks
        data.CanFinish = false;
        UpdateConsole((entity, component));

        // Announce early finish with 20-second countdown
        const int departTime = 20;
        if (xform.GridUid != null)
        {
            var filter = Filter.Empty().AddInGrid(xform.GridUid.Value);
            var announcement = Loc.GetString("salvage-expedition-announcement-early-finish", ("departTime", departTime));
            _chatSystem.DispatchFilteredAnnouncement(filter, announcement, entity,
                sender: "Expedition Console", colorOverride: Color.Orange);
        }

        Log.Info($"Early expedition finish initiated on console {ToPrettyString(entity)}, FTL in {departTime} seconds");

        // Schedule the actual expedition completion after 20 seconds
        entity.SpawnTimer(TimeSpan.FromSeconds(departTime), () =>
        {
            // Verify the expedition still exists
            if (!Exists(expeditionMapUid.Value) || !TryComp<SalvageExpeditionComponent>(expeditionMapUid.Value, out var expComp))
            {
                Log.Warning($"Expedition {expeditionMapUid} no longer exists when trying to finish early");
                return;
            }

            // Do NOT mark as completed here; reward should only be granted
            // when mission objectives are actually complete. The runner logic
            // updates expComp.Completed based on objectives.

            // Trigger the same FTL process as normal expedition timeout
            TriggerExpeditionFTLHome(expeditionMapUid.Value, expComp);

            Log.Info($"Early expedition finish completed for {ToPrettyString(expeditionMapUid.Value)}");
        });
    }
    // End Frontier: early expedition end

    /// <summary>
    /// HARDLIGHT: Triggers the FTL home process for shuttles on an expedition map
    /// This is the same logic used in normal expedition timeout but extracted for early finish
    /// </summary>
    private void TriggerExpeditionFTLHome(EntityUid expeditionMapUid, SalvageExpeditionComponent expedition)
    {
        const float ftlTime = 20f; // 20 seconds FTL time for early finish
        var shuttleQuery = AllEntityQuery<ShuttleComponent, TransformComponent>();

        // Find shuttles on the expedition map and FTL them home
        while (shuttleQuery.MoveNext(out var shuttleUid, out var shuttle, out var shuttleXform))
        {
            if (shuttleXform.MapUid != expeditionMapUid || HasComp<FTLComponent>(shuttleUid))
                continue;

            // Find a destination on the default map
            var mapId = _gameTicker.DefaultMap;
            if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            {
                Log.Error($"Could not get DefaultMap EntityUID, shuttle {shuttleUid} may be stuck on expedition.");
                continue;
            }

            // Destination generator parameters (same as normal timeout)
            int numRetries = 20;
            float minDistance = 200f;
            float minRange = 750f;
            float maxRange = 3500f;

            // Get positions of existing grids to avoid collisions
            List<Vector2> gridCoords = new();
            var gridQuery = EntityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var _, out _, out var xform))
            {
                if (xform.MapID == mapId)
                    gridCoords.Add(_transform.GetWorldPosition(xform));
            }

            // Find a safe drop location
            Vector2 dropLocation = _random.NextVector2(minRange, maxRange);
            for (int i = 0; i < numRetries; i++)
            {
                bool positionIsValid = true;
                foreach (var station in gridCoords)
                {
                    if (Vector2.Distance(station, dropLocation) < minDistance)
                    {
                        positionIsValid = false;
                        break;
                    }
                }

                if (positionIsValid)
                    break;

                dropLocation = _random.NextVector2(minRange, maxRange);
            }

            // FTL the shuttle home
            _shuttle.FTLToCoordinates(shuttleUid, shuttle, new EntityCoordinates(mapUid.Value, dropLocation), 0f, ftlTime, TravelTime);
            Log.Info($"Early finish: FTLing shuttle {shuttleUid} home from expedition {expeditionMapUid}");
        }

        // Clean up console state and schedule expedition deletion
        CleanupExpeditionConsoleState(expeditionMapUid);

        // Delete the expedition map after shuttles have departed
        expeditionMapUid.SpawnTimer(TimeSpan.FromSeconds(ftlTime + 5f), () =>
        {
            if (Exists(expeditionMapUid))
            {
                QueueDel(expeditionMapUid);
                Log.Info($"Deleted expedition map {expeditionMapUid} after early finish");
            }
        });
    }

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(Entity<SalvageExpeditionDataComponent> component)
    {
        // HARDLIGHT: This method is obsolete with independent console system
        // Each console manages its own state independently
        Log.Debug("UpdateConsoles called but consoles are now independent - no action needed");
    }

    public void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        var consoleComp = component.Comp;
        var uid = component.Owner;

        var data = GetStationExpeditionData(uid);
        if (data == null)
        {
            // If the console isn't on a grid, present a disabled state.
            var emptyState = new SalvageExpeditionConsoleState(
                TimeSpan.Zero,
                false,
                true,
                0,
                new List<SalvageMissionParams>(),
                false,
                TimeSpan.Zero
            );
            _ui.SetUiState(uid, SalvageConsoleUiKey.Expedition, emptyState);
            return;
        }

        // Sanitize ActiveMission against current mission list to avoid UI/index errors
        if (data.ActiveMission != 0 && !data.Missions.ContainsKey(data.ActiveMission))
        {
            Log.Warning($"Console {ToPrettyString(uid)} had ActiveMission={data.ActiveMission} not in mission list; resetting.");
            data.ActiveMission = 0;
            data.CanFinish = false;
        }

        // HARDLIGHT: Only generate missions if truly needed and not already generating
        // This prevents the race condition that causes UI issues
        bool shouldGenerateMissions = data.Missions.Count == 0 &&
                                     data.ActiveMission == 0 &&
                                     !data.GeneratingMissions &&
                                     !data.Cooldown;

        if (shouldGenerateMissions)
        {
            Log.Debug($"Generating missions for console {ToPrettyString(uid)} - conditions met");
            GenerateMissions(data);
        }

        var state = new SalvageExpeditionConsoleState(
            data.NextOffer,
            data.Claimed,
            data.Cooldown,
            data.ActiveMission,
            data.Missions.Values.ToList(),
            data.CanFinish,
            data.CooldownTime
        );

        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
        Log.Debug($"Updated console {ToPrettyString(uid)} with {state.Missions.Count} missions (Active: {data.ActiveMission}, Cooldown: {data.Cooldown})");
    }

    // HARDLIGHT: Direct mission spawning for console-specific expeditions
    private void SpawnMissionForConsole(SalvageMissionParams missionParams, EntityUid shuttleGrid, EntityUid consoleUid)
    {
        // HARDLIGHT: Fully independent console system - no station dependencies
        Log.Info($"Spawning independent mission for console {consoleUid} on shuttle {shuttleGrid}");

        // Directly spawn the mission using the existing job system
        // HARDLIGHT: For independent console system, use shuttle as station and pass console reference
        var missionStation = shuttleGrid; // Always use shuttle grid for independent consoles
        var cancelToken = new CancellationTokenSource();
        var job = new SpawnSalvageMissionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            _station,
            _shuttle,
            this,
            missionStation,
            consoleUid, // HARDLIGHT: Pass console reference for FTL targeting
            null, // No coordinates disk for console missions
            missionParams,
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    // Frontier: deny sound
    private void PlayDenySound(Entity<SalvageExpeditionConsoleComponent> ent)
    {
        _audio.PlayPvs(_audio.ResolveSound(ent.Comp.ErrorSound), ent);
    }
    // End Frontier
}
