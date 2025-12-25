using System.Linq;
using System.Threading;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // For MapGridComponent
using Content.Server._NF.Salvage.Expeditions; // Frontier
using Content.Server.Station.Components; // Frontier
using Content.Shared.Procedural; // Frontier
using Content.Shared.Salvage; // Frontier
using Robust.Shared.Prototypes; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Shared.Shuttles.Components; // Frontier
using Robust.Shared.Configuration;
using Content.Shared.Ghost;
using System.Numerics; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles setup / teardown of salvage expeditions.
     */

    private const int MissionLimit = 6; // Frontier: 3<5

    private readonly JobQueue _salvageQueue = new();
    private readonly List<(SpawnSalvageMissionJob Job, CancellationTokenSource CancelToken)> _salvageJobs = new();
    private const double SalvageJobTime = 0.002;
    private readonly List<(ProtoId<SalvageDifficultyPrototype> id, int value)> _missionDifficulties = [("NFEasy", 0),("NFModerate", 1), ("NFHazardous", 2),("NFExtreme", 3), ("NFNightmare", 4)]; // Frontier: mission difficulties with order

    [Dependency] private readonly IConfigurationManager _cfgManager = default!; // Frontier

    private float _cooldown;
    private float _failedCooldown; // Frontier
    public float TravelTime { get; private set; } // Frontier
    public bool ProximityCheck { get; private set; } // Frontier

    private void InitializeExpeditions()
    {
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ComponentInit>(OnSalvageConsoleInit);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, EntParentChangedMessage>(OnSalvageConsoleParent);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ClaimSalvageMessage>(OnSalvageClaimMessage);
        SubscribeLocalEvent<SalvageExpeditionDataComponent, ExpeditionSpawnCompleteEvent>(OnExpeditionSpawnComplete); // Frontier: more gracefully handle expedition generation failures
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ExpeditionSpawnCompleteEvent>(OnExpeditionSpawnCompleteConsole); // HARDLIGHT: Handle event on consoles for round persistence
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, FinishSalvageMessage>(OnSalvageFinishMessage); // Frontier: For early finish
    SubscribeLocalEvent<SalvageExpeditionConsoleComponent, RefreshSalvageConsoleMessage>(OnSalvageRefreshMessage); // HARDLIGHT: manual refresh

        SubscribeLocalEvent<SalvageExpeditionComponent, MapInitEvent>(OnExpeditionMapInit);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentGetState>(OnExpeditionGetState);
        SubscribeLocalEvent<SalvageExpeditionComponent, EntityTerminatingEvent>(OnMapTerminating); // Frontier

        SubscribeLocalEvent<SalvageStructureComponent, ExaminedEvent>(OnStructureExamine);

        _cooldown = _cfgManager.GetCVar(CCVars.SalvageExpeditionCooldown);
        Subs.CVar(_cfgManager, CCVars.SalvageExpeditionCooldown, SetCooldownChange);
        _failedCooldown = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionFailedCooldown); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionFailedCooldown, SetFailedCooldownChange); // Frontier
        TravelTime = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionTravelTime); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionTravelTime, SetTravelTime); // Frontier
        ProximityCheck = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionProximityCheck); // Frontier
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionProximityCheck, SetProximityCheck); // Frontier
    }

    private void OnExpeditionGetState(EntityUid uid, SalvageExpeditionComponent component, ref ComponentGetState args)
    {
        args.State = new SalvageExpeditionComponentState()
        {
            Stage = component.Stage,
            SelectedSong = component.SelectedSong // Frontier: note, not dirtied on map init (not needed)
        };
    }

    private void SetCooldownChange(float obj)
    {
        // Update the active cooldowns if we change it.
        var diff = obj - _cooldown;

        var query = AllEntityQuery<SalvageExpeditionDataComponent>();

        while (query.MoveNext(out var comp))
        {
            comp.NextOffer += TimeSpan.FromSeconds(diff);
        }

        _cooldown = obj;
    }

    // Frontier: failed cooldowns
    private void SetFailedCooldownChange(float obj)
    {
        // Note: we don't know whether or not players have failed missions, so let's not punish/reward them if this gets changed.
        _failedCooldown = obj;
    }

    private void SetTravelTime(float obj)
    {
        TravelTime = obj;
    }

    private void SetProximityCheck(bool obj)
    {
        ProximityCheck = obj;
    }
    // End Frontier

    private void OnExpeditionMapInit(EntityUid uid, SalvageExpeditionComponent component, MapInitEvent args)
    {
        component.SelectedSong = _audio.ResolveSound(component.Sound);
    }

    private void OnExpeditionShutdown(EntityUid uid, SalvageExpeditionComponent component, ComponentShutdown args)
    {
        // component.Stream = _audio.Stop(component.Stream); // Frontier: moved to client

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            if (job.Station == component.Station)
            {
                cancelToken.Cancel();
                _salvageJobs.Remove((job, cancelToken));
            }
        }

        // HARDLIGHT: Handle round persistence - station might be deleted during round transitions
        if (Deleted(component.Station))
        {
            Log.Info($"Expedition shutdown: Station {component.Station} was deleted (likely during round persistence)");
            return;
        }

        // Finish mission - but handle the case where expedition data might be on a different entity
        // due to round persistence transferring data between entities
        if (TryComp(component.Station, out SalvageExpeditionDataComponent? data))
        {
            FinishExpedition((component.Station, data), component, uid); // Frontier: add component
        }
        else
        {
            // HARDLIGHT: For round persistence, expedition data might have been moved to a console's LocalExpeditionData
            // This is a graceful fallback for when station entities are recreated
            Log.Info($"Expedition shutdown: No expedition data found on station {component.Station}, expedition likely handled by local console data");
        }
    }

    private void UpdateExpeditions()
    {
        var currentTime = _timing.CurTime;
        _salvageQueue.Process();

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _salvageJobs.Remove((job, cancelToken));
                    break;
            }
        }

        var query = EntityQueryEnumerator<SalvageExpeditionDataComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // Update offers
            if (comp.NextOffer > currentTime || comp.Claimed)
                continue;

            // Frontier: disable cooldown when still in FTL
            // HARDLIGHT: Modified for round persistence - work with or without StationDataComponent
            EntityUid? largestGrid = null;

            if (TryComp(uid, out StationDataComponent? stationData))
            {
                // Normal case: station has StationDataComponent
                largestGrid = _station.GetLargestGrid(stationData);
            }
            else if (TryComp(uid, out MapGridComponent? _))
            {
                // Round persistence case: uid might be a grid itself (shuttle serving as station)
                largestGrid = uid;
            }

            // Check if the grid (whether from station or direct grid) has FTL component
            if (largestGrid == null || !TryComp(largestGrid.Value, out FTLComponent? _))
            {
                comp.Cooldown = false;
            }
            // End Frontier: disable cooldown when still in FTL
            // comp.NextOffer += TimeSpan.FromSeconds(_cooldown); // Frontier
            comp.NextOffer = currentTime + TimeSpan.FromSeconds(_cooldown); // Frontier
            comp.CooldownTime = TimeSpan.FromSeconds(_cooldown); // Frontier

            // HARDLIGHT: Only generate missions if not already generating and no active mission
            if (!comp.GeneratingMissions && comp.ActiveMission == 0)
            {
                GenerateMissions(comp);
            }
            UpdateConsoles((uid, comp));
        }
    }

    private void FinishExpedition(Entity<SalvageExpeditionDataComponent> expedition, SalvageExpeditionComponent expeditionComp, EntityUid uid)
    {
        var component = expedition.Comp;
        // Frontier: separate timeout/announcement for success/failures
        if (expeditionComp.Completed)
        {
            component.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
            component.CooldownTime = TimeSpan.FromSeconds(_cooldown);
            Announce(uid, Loc.GetString("salvage-expedition-mission-completed"));
            // HARDLIGHT: Spawn expedition reward at the originating console based on difficulty
            TrySpawnExpeditionRewardAtConsole(expeditionComp);
        }
        else
        {
            component.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_failedCooldown);
            component.CooldownTime = TimeSpan.FromSeconds(_failedCooldown);
            Announce(uid, Loc.GetString("salvage-expedition-mission-failed"));
        }
        // End Frontier: separate timeout/announcement for success/failures
        component.ActiveMission = 0;
        component.Cooldown = true;

        // HARDLIGHT: Clear missions when expedition finishes to prevent UI confusion
        component.Missions.Clear();

        UpdateConsoles(expedition);
    }
     // HARDLIGHT: Spawn appropriate briefcase reward at the console that started the expedition
    private void TrySpawnExpeditionRewardAtConsole(SalvageExpeditionComponent expeditionComp)
    {
        if (expeditionComp.Console == null || !Exists(expeditionComp.Console.Value))
        {
            Log.Warning("Expedition completed but console reference missing; cannot spawn reward.");
            return;
        }

        if (!TryComp(expeditionComp.Console.Value, out TransformComponent? consoleXform))
        {
            Log.Warning($"Expedition completed but console {ToPrettyString(expeditionComp.Console.Value)} has no transform; cannot spawn reward.");
            return;
        }

        // Map difficulty to reward tier entity
        var diffId = expeditionComp.MissionParams.Difficulty.ToString();
        string rewardProto = diffId switch
        {
            "NFEasy" => "SpaceCashExpeditionT1",
            "NFModerate" => "SpaceCashExpeditionT2",
            "NFHazardous" => "SpaceCashExpeditionT3",
            "NFExtreme" => "SpaceCashExpeditionT4",
            "NFNightmare" => "SpaceCashExpeditionT5",
            _ => "SpaceCashExpeditionT1"
        };

        try
        {
            EntityManager.SpawnEntity(rewardProto, consoleXform.Coordinates);
            Log.Info($"Spawned expedition reward {rewardProto} at console {ToPrettyString(expeditionComp.Console.Value)} for difficulty {diffId}.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to spawn expedition reward {rewardProto} at console {ToPrettyString(expeditionComp.Console.Value)}: {ex}");
        }
    }
    private void GenerateMissions(SalvageExpeditionDataComponent component)
    {
        // HARDLIGHT: Prevent duplicate mission generation
        if (component.GeneratingMissions)
        {
            Log.Debug("Skipping mission generation - already in progress");
            return;
        }

        component.GeneratingMissions = true;

        try
        {
            component.Missions.Clear();

            // Frontier: generate missions from an arbitrary set of difficulties
            if (_missionDifficulties.Count <= 0)
            {
                Log.Error("No expedition mission difficulties to pick from!");
                return;
            }

            // this doesn't support having more missions than types of ratings
            // but the previous system didn't do that either.
            var allDifficulties = _missionDifficulties; // Frontier: Enum.GetValues<DifficultyRating>() < _missionDifficulties
            _random.Shuffle(allDifficulties);
            var difficulties = allDifficulties.Take(MissionLimit).ToList();

            // If we support more missions than there are accepted types, pick more until you're up to MissionLimit
            while (difficulties.Count < MissionLimit)
            {
                var difficultyIndex = _random.Next(_missionDifficulties.Count);
                difficulties.Add(_missionDifficulties[difficultyIndex]);
            }
            difficulties.Sort((x, y) => { return Comparer<int>.Default.Compare(x.value, y.value); });

            // HARDLIGHT: Always start mission indices from 0 for consistency
            var missionIndex = 0;
            for (var i = 0; i < MissionLimit; i++)
            {
                var mission = new SalvageMissionParams
                {
                    Index = (ushort)missionIndex,
                    // Pick a valid mission type; Max is a sentinel and must be excluded.
                    MissionType = (SalvageMissionType)_random.NextByte((byte)SalvageMissionType.Max),
                    Seed = _random.Next(),
                    Difficulty = difficulties[i].id,
                };

                component.Missions[(ushort)missionIndex] = mission;
                missionIndex++;
            }

            // Update NextIndex to continue from where we left off if more missions are generated later
            component.NextIndex = (ushort)missionIndex;
            // End Frontier: generate missions from an arbitrary set of difficulties

            Log.Debug($"Generated {component.Missions.Count} new missions");
        }
        finally
        {
            // HARDLIGHT: Always clear the flag, even if generation fails
            component.GeneratingMissions = false;
        }
    }

    // HARDLIGHT: Public method for round persistence system to properly regenerate missions
    public void ForceGenerateMissions(SalvageExpeditionDataComponent component)
    {
        GenerateMissions(component);
    }

    private SalvageExpeditionConsoleState GetState(SalvageExpeditionDataComponent component)
    {
        var missions = component.Missions.Values.ToList();
        return new SalvageExpeditionConsoleState(component.NextOffer, component.Claimed, component.Cooldown, component.ActiveMission, missions, component.CanFinish, component.CooldownTime); // Frontier: add CanFinish, CooldownTime
    }

    private void SpawnMission(SalvageMissionParams missionParams, EntityUid station, EntityUid? console, EntityUid? coordinatesDisk)
    {
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
            _station, // Frontier
            _shuttle, // Frontier
            this, // Frontier
            station,
            console,
            coordinatesDisk,
            missionParams,
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    private void OnStructureExamine(EntityUid uid, SalvageStructureComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("salvage-expedition-structure-examine"));
    }

    // Frontier: exped job handling, ghost reparenting
    // Handle exped spawn job failures gracefully - reset the console
    private void OnExpeditionSpawnComplete(EntityUid uid, SalvageExpeditionDataComponent component, ExpeditionSpawnCompleteEvent ev)
    {
        // HARDLIGHT: Enhanced handling for round persistence
        if (component.ActiveMission == ev.MissionIndex && !ev.Success)
        {
            component.ActiveMission = 0;
            component.Cooldown = false;
            // HARDLIGHT: Don't regenerate missions here, let UpdateConsole handle it
            // This prevents the UI swapping issue
            UpdateConsoles((uid, component));

            Log.Info($"Expedition mission {ev.MissionIndex} failed for entity {uid}, reset console state");
        }
        else if (component.ActiveMission == ev.MissionIndex && ev.Success)
        {
            Log.Debug($"Expedition mission {ev.MissionIndex} completed successfully for entity {uid}");
        }
    }

    // HARDLIGHT: Also handle the event on expedition consoles for round persistence
    private void OnExpeditionSpawnCompleteConsole(EntityUid uid, SalvageExpeditionConsoleComponent component, ExpeditionSpawnCompleteEvent ev)
    {
        // Handle expedition completion events sent to consoles via station data
        var stationData = GetStationExpeditionData(uid);
        if (stationData != null &&
            stationData.ActiveMission == ev.MissionIndex &&
            !ev.Success)
        {
            stationData.ActiveMission = 0;
            stationData.Cooldown = false;

            // Update the console UI
            UpdateConsole((uid, component));

            Log.Info($"Expedition console {uid} handled failed mission {ev.MissionIndex} via station expedition data");
        }
        // HARDLIGHT: Also handle successful missions to ensure proper state management
        else if (stationData != null &&
                 stationData.ActiveMission == ev.MissionIndex &&
                 ev.Success)
        {
            Log.Debug($"Expedition console {uid} handled successful mission {ev.MissionIndex}");
            // Don't change state for successful missions - let the normal completion flow handle it
        }
    }

    // Send all ghosts (relevant for admins) back to the default map so they don't lose their stuff.
    private void OnMapTerminating(EntityUid uid, SalvageExpeditionComponent component, EntityTerminatingEvent ev)
    {
        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        var newCoords = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);
        while (ghosts.MoveNext(out var ghostUid, out _, out var xform))
        {
            if (xform.MapUid == uid)
                _transform.SetMapCoordinates(ghostUid, newCoords);
        }
    }
    // End Frontier
}
