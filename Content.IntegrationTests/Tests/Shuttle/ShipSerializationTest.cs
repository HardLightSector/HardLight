using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Save;
using Content.Tests;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
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
        const string shipName = "TestShip";

        await server.WaitAssertion(() =>
        {
            // Ensure we use the refactored path.
            cfg.SetCVar(CCVars.ShipyardUseLegacySerializer, false);

            // Create a fresh grid separate from default test map grid (remove initial grid to minimize noise).
            entManager.DeleteEntity(map.Grid);
            var gridEnt = mapManager.CreateGridEntity(map.MapId);
            var gridUid = gridEnt.Owner;
            var gridComp = gridEnt.Comp;

            entManager.RunMapInit(gridUid, entManager.GetComponent<MetaDataComponent>(gridUid));

            // Lay down tiles so spawned entities can anchor if needed.
            mapSys.SetTile(gridUid, gridComp, Vector2i.Zero, new Tile(1));
            mapSys.SetTile(gridUid, gridComp, new Vector2i(1, 0), new Tile(1));

            // Spawn a couple of simple prototypes that should serialize (avoid ones filtered like vending machines).
            var coords = new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f));
            var ent1 = entManager.SpawnEntity("AirlockShuttle", coords);
            var ent2 = entManager.SpawnEntity("ChairBrass", new EntityCoordinates(gridUid, new Vector2(1.5f, 0.5f)));

            Assert.Multiple(() =>
            {
                // Sanity: they exist and are children of the grid.
                Assert.That(entManager.EntityExists(ent1));
                Assert.That(entManager.EntityExists(ent2));
                Assert.That(entManager.GetComponent<TransformComponent>(ent1).ParentUid, Is.EqualTo(gridUid));
                Assert.That(entManager.GetComponent<TransformComponent>(ent2).ParentUid, Is.EqualTo(gridUid));
            });

            var playerId = new NetUserId(Guid.NewGuid());
            var data = shipSer.SerializeShip(gridUid, playerId, shipName);

            Assert.That(data.Grids, Has.Count.EqualTo(1), "Expected exactly one grid serialized");
            var g = data.Grids[0];

            Assert.Multiple(() =>
            {
                // Tiles: we placed exactly two non-space tiles.
                Assert.That(g.Tiles, Has.Count.EqualTo(2), "Expected two non-space tiles");

                // Entities: expect at least the two we spawned, though additional infrastructure entities (grid, etc.) may appear.
                // We only store entities with valid prototypes; ensure count >=2 and contains our prototypes.
                Assert.That(g.Entities, Has.Count.GreaterThanOrEqualTo(2), $"Expected at least 2 entities, got {g.Entities.Count}");
            });

            var protos = g.Entities.Select(e => e.Prototype).ToHashSet();
            Assert.Multiple(() =>
            {
                Assert.That(protos, Does.Contain("AirlockShuttle"), "Serialized entities missing AirlockShuttle prototype");
                Assert.That(protos, Does.Contain("ChairBrass"), "Serialized entities missing ChairBrass prototype");
            });
        });

        await pair.CleanReturnAsync();
    }
}
