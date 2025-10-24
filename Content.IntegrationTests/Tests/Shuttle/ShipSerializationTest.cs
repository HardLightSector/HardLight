using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Shuttles.Save;
using Content.Tests;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Maths;
using Robust.Shared.IoC;
using Robust.Shared.Network; // Needed for NetUserId

namespace Content.IntegrationTests.Tests.Shuttle;

/// <summary>
/// Regression test: ensure the refactored ShipSerializationSystem actually serializes entities
/// (previously only tiles were saved due to incorrect YAML parsing).
/// </summary>
public sealed class ShipSerializationTest : ContentUnitTest
{
    [Test]
    public async Task RefactoredSerializer_SerializesEntities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var shipSer = entManager.System<ShipSerializationSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapSys = entManager.System<SharedMapSystem>();
        var xformSys = entManager.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            // Ensure we use the refactored path.
            cfg.SetCVar(CCVars.ShipyardUseLegacySerializer, false);

            // Create a fresh grid separate from default test map grid (remove initial grid to minimize noise).
            entManager.DeleteEntity(map.Grid);
            var gridEnt = mapManager.CreateGridEntity(map.MapId);
            var gridUid = gridEnt.Owner;
            var gridComp = gridEnt.Comp;

            // Lay down a single solid tile so spawned entities can anchor if needed.
            mapSys.SetTile(gridUid, gridComp, Vector2i.Zero, new Tile(1));

            // Spawn a couple of simple prototypes that should serialize (avoid ones filtered like vending machines).
            var coords = new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f));
            var ent1 = entManager.SpawnEntity("AirlockShuttle", coords); // has Transform + is a clear prototype
            var ent2 = entManager.SpawnEntity("ChairOfficeLight", new EntityCoordinates(gridUid, new Vector2(1.5f, 0.5f))); // HL - Change to a prototype that actually exists

            // Sanity: they exist and are children of the grid.
            Assert.That(entManager.EntityExists(ent1));
            Assert.That(entManager.EntityExists(ent2));
            Assert.That(entManager.GetComponent<TransformComponent>(ent1).ParentUid, Is.EqualTo(gridUid));
            Assert.That(entManager.GetComponent<TransformComponent>(ent2).ParentUid, Is.EqualTo(gridUid));

            var playerId = new NetUserId(Guid.NewGuid());
            var shipName = "TestShip";
            var data = shipSer.SerializeShip(gridUid, playerId, shipName);

            Assert.That(data.Grids.Count, Is.EqualTo(1), "Expected exactly one grid serialized");
            var g = data.Grids[0];

            // Tiles: we placed exactly one non-space tile.
            Assert.That(g.Tiles.Count, Is.EqualTo(1), "Expected one non-space tile");

            // Entities: expect at least the two we spawned, though additional infrastructure entities (grid, etc.) may appear.
            // We only store entities with valid prototypes; ensure count >=2 and contains our prototypes.
            Assert.That(g.Entities.Count >= 2, $"Expected at least 2 entities, got {g.Entities.Count}");
            var protos = g.Entities.Select(e => e.Prototype).ToHashSet();
            Assert.That(protos.Contains("AirlockShuttle"), "Serialized entities missing AirlockShuttle prototype");
            Assert.That(protos.Contains("ChairOfficeLight"), "Serialized entities missing ChairOffice prototype"); // HL - Change to a prototype that actually exists
        });

        await pair.CleanReturnAsync();
    }
}
