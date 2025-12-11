using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Server.Chat.Managers;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._NF.GameTicking.Events;
using Content.Server._NF.PublicTransit;
using Content.Server._NF.PublicTransit.Components;
using Content.Server._NF.PublicTransit.Prototypes;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Systems;
using Content.Server.Parallax;
using Content.Server.Screens.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Shuttles.Events;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Salvage;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tiles;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// If enabled spawns players on a separate arrivals station before they can transfer to the main station.
/// </summary>
public sealed class ArrivalsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BiomeSystem _biomes = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly PublicTransitSystem _publicTransit = default!;

    private readonly List<ProtoId<BiomeTemplatePrototype>> _arrivalsBiomeOptions = new()
    {
        "Grasslands"
    };

    public bool Enabled { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        Enabled = _cfgManager.GetCVar(CCVars.ArrivalsShuttles);
        _cfgManager.OnValueChanged(CCVars.ArrivalsShuttles, SetArrivals);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
    }

    private void SetArrivals(bool enabled)
    {
        Enabled = enabled;
        if (Enabled)
            SetupArrivalsStation();
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (Enabled)
            SetupArrivalsStation();
    }

    private void SetupArrivalsStation()
    {
        EntityUid arrivalsUid;
        if (TryGetArrivals(out arrivalsUid))
        {
            // Arrivals already exists, just finalize setup.
            FinalizeArrivalsSetup(arrivalsUid);
            return;
        }

        // 1. Create the planet map
        var planetMapPath = _cfgManager.GetCVar(CCVars.ArrivalsMap);
        _mapSystem.CreateMap(out var planetMapId, runMapInit: false);
        var planetMapUid = _mapSystem.GetMap(planetMapId);

        // 2. Optionally, add a biome/planet
        if (_cfgManager.GetCVar(CCVars.ArrivalsPlanet))
        {
            var template = _random.Pick(_arrivalsBiomeOptions);
            _biomes.EnsurePlanet(planetMapUid, _protoManager.Index(template));
        }

        // 3. Load the terminal grid onto the planet map
        var terminalGridPath = "/Maps/Misc/terminal.yml";
        if (!_loader.TryLoadGrid(planetMapId, new ResPath(terminalGridPath), out var terminalGrid))
            return;

        _metaData.SetEntityName(terminalGrid.Value, "Arrivals Terminal");
        EnsureComp<ArrivalsSourceComponent>(terminalGrid.Value);
        EnsureComp<StationTransitComponent>(terminalGrid.Value);

        arrivalsUid = terminalGrid.Value;
        _mapSystem.InitializeMap(planetMapId);

        FinalizeArrivalsSetup(arrivalsUid);
    }

    private void FinalizeArrivalsSetup(EntityUid terminalGrid)
    {
        // Add the arrivals terminal as a stop to the route
        const int stopIndex = 10;
        _publicTransit.AddStopToRoute(terminalGrid, "SpawnPoints", stopIndex);

        // --- Add the main station grid as a stop to the route ---
        var mainStation = _station.GetStations().FirstOrDefault();
        if (mainStation != EntityUid.Invalid && TryComp<StationDataComponent>(mainStation, out var stationData))
        {
            if (_station.GetLargestGrid(stationData) is { } mainStationGrid)
            {
                _publicTransit.AddStopToRoute(terminalGrid, "SpawnPoints", 10); // Arrivals
                _publicTransit.AddStopToRoute(mainStationGrid, "SpawnPoints", 20); // Use a different index
            }
        }

        // Notify other systems that arrivals is ready
        RaiseLocalEvent(new StationsGeneratedEvent());
        RaiseLocalEvent(new ArrivalsReadyEvent());
    }

    private void SetupShuttle(EntityUid arrivalsGrid)
    {
        var shuttlePath = "/Maps/_NF/Shuttles/Bus/cheetah.yml"; // Path to your bus YAML

        // Spawn the bus grid on a dummy map
        _mapSystem.CreateMap(out var dummyMapId);

        if (_loader.TryLoadGrid(dummyMapId, new ResPath(shuttlePath), out var busGrid))
        {
            EnsureComp<TransitShuttleComponent>(busGrid.Value);
            _publicTransit.RegisterBus(busGrid.Value, "SpawnPoints", arrivalsGrid, "DockTransit");
        }
    }

    public bool TryGetArrivals(out EntityUid uid)
    {
        var arrivalsQuery = EntityQueryEnumerator<ArrivalsSourceComponent>();
        while (arrivalsQuery.MoveNext(out uid, out _))
            return true;
        uid = default;
        return false;
    }

    private void OnStationsGenerated(StationsGeneratedEvent ev)
    {
        // Optional: logic here
    }
}
