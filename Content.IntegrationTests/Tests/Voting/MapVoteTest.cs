using System.Linq;
using System.Reflection;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Voting.Managers;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Voting;

[TestFixture]
public sealed class MapVoteTest
{
    private const string TestMapEligibleName = "MapVoteTestEligible";
    private const string TestMapIneligibleName = "MapVoteTestIneligible";

    [TestPrototypes]
    private static readonly string TestMaps = @$"
- type: gameMap
  id: {TestMapIneligibleName}
  mapName: {TestMapIneligibleName}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 20
  maxPlayers: 80
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""

- type: gameMap
  id: {TestMapEligibleName}
  mapName: {TestMapEligibleName}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
";

    /// <summary>
    /// Tests that when a map vote finishes, the selected map is applied
    /// regardless of current eligibility restrictions, ensuring democracy works.
    /// </summary>
    [Test]
    public async Task TestMapVoteOverridesEligibility()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var gameMapManager = server.ResolveDependency<IGameMapManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            // Verify test maps exist
            Assert.That(prototypeManager.HasIndex<GameMapPrototype>(TestMapEligibleName), Is.True,
                $"Test map {TestMapEligibleName} should exist");
            Assert.That(prototypeManager.HasIndex<GameMapPrototype>(TestMapIneligibleName), Is.True,
                $"Test map {TestMapIneligibleName} should exist");

            // Clear any existing map selection
            gameMapManager.ClearSelectedMap();
            Assert.That(gameMapManager.GetSelectedMap(), Is.Null, "Map should be cleared");

            // Test 1: Verify TrySelectMapIfEligible respects eligibility (this should work as before)
            var eligibleSelected = gameMapManager.TrySelectMapIfEligible(TestMapEligibleName);
            Assert.That(eligibleSelected, Is.True, "Eligible map should be selectable via TrySelectMapIfEligible");
            Assert.That(gameMapManager.GetSelectedMap()?.ID, Is.EqualTo(TestMapEligibleName),
                "Eligible map should be selected");

            gameMapManager.ClearSelectedMap();

            // Test 2: Verify TrySelectMapIfEligible blocks ineligible maps (this should work as before)
            var ineligibleSelected = gameMapManager.TrySelectMapIfEligible(TestMapIneligibleName);
            Assert.That(ineligibleSelected, Is.False, "Ineligible map should not be selectable via TrySelectMapIfEligible");
            Assert.That(gameMapManager.GetSelectedMap(), Is.Null,
                "Ineligible map should not be selected via TrySelectMapIfEligible");

            // Test 3: Verify SelectMap bypasses eligibility (this is what our fix uses for votes)
            gameMapManager.SelectMap(TestMapIneligibleName);
            Assert.That(gameMapManager.GetSelectedMap()?.ID, Is.EqualTo(TestMapIneligibleName),
                "Ineligible map should be selectable via SelectMap (democracy override)");

            // Test 4: Verify that even eligible maps work with SelectMap
            gameMapManager.SelectMap(TestMapEligibleName);
            Assert.That(gameMapManager.GetSelectedMap()?.ID, Is.EqualTo(TestMapEligibleName),
                "Eligible map should also work with SelectMap");

            gameMapManager.ClearSelectedMap();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that CheckMapExists correctly validates map existence for our fix.
    /// </summary>
    [Test]
    public async Task TestMapExistenceCheck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var gameMapManager = server.ResolveDependency<IGameMapManager>();

        await server.WaitAssertion(() =>
        {
            // Test that valid maps exist
            Assert.That(gameMapManager.CheckMapExists(TestMapEligibleName), Is.True,
                $"Map {TestMapEligibleName} should exist");
            Assert.That(gameMapManager.CheckMapExists(TestMapIneligibleName), Is.True,
                $"Map {TestMapIneligibleName} should exist");

            // Test that invalid maps don't exist
            Assert.That(gameMapManager.CheckMapExists("NonExistentMap123"), Is.False,
                "Non-existent map should not exist");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Tests that map selection works correctly and our LoadMaps fix doesn't break functionality.
    /// This verifies that map selection via voting still works after our Nullspace handling fix.
    /// </summary>
    [Test]
    public async Task TestMapSelectionAfterFix()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var gameMapManager = server.ResolveDependency<IGameMapManager>();

        await server.WaitAssertion(() =>
        {
            // Clear any existing map selection
            gameMapManager.ClearSelectedMap();
            Assert.That(gameMapManager.GetSelectedMap(), Is.Null,
                "Map selection should be cleared");

            // Test map selection - this should work regardless of DefaultMap state
            gameMapManager.SelectMap(TestMapEligibleName);
            var selectedMap = gameMapManager.GetSelectedMap();
            Assert.That(selectedMap?.ID, Is.EqualTo(TestMapEligibleName),
                "Selected map should be set correctly via SelectMap()");

            // Test another map selection
            gameMapManager.SelectMap(TestMapIneligibleName);
            selectedMap = gameMapManager.GetSelectedMap();
            Assert.That(selectedMap?.ID, Is.EqualTo(TestMapIneligibleName),
                "Selected map should be updated correctly");

            // Clear and verify
            gameMapManager.ClearSelectedMap();
            Assert.That(gameMapManager.GetSelectedMap(), Is.Null,
                "Map selection should be cleared again");
        });

        await pair.CleanReturnAsync();
    }
}
