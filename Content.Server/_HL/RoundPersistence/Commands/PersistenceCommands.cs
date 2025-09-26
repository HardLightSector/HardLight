using Content.Server.Administration;
using Content.Server.HL.RoundPersistence.Systems;
using Content.Server._HL.RoundPersistence.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server.HL.RoundPersistence.Commands;

/// <summary>
/// Console commands for managing the round persistence system
/// </summary>

[AdminCommand(AdminFlags.Debug)]
public sealed class SavePersistentDataCommand : IConsoleCommand
{
    public string Command => "save_persistent_data";
    public string Description => "Force save all persistent data for round continuity";
    public string Help => "save_persistent_data";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entManager = IoCManager.Resolve<IEntityManager>();
        var sysManager = IoCManager.Resolve<IEntitySystemManager>();

        var persistenceSystem = sysManager.GetEntitySystem<RoundPersistenceSystem>();
        persistenceSystem.ForceSave();

        shell.WriteLine("Forced save of persistent data completed.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class PersistentDataStatusCommand : IConsoleCommand
{
    public string Command => "persistent_data_status";
    public string Description => "Show status of the round persistence system";
    public string Help => "persistent_data_status";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entManager = IoCManager.Resolve<IEntityManager>();
        var sysManager = IoCManager.Resolve<IEntitySystemManager>();

        var persistenceSystem = sysManager.GetEntitySystem<RoundPersistenceSystem>();
        var (stationCount, shipCount, roundNumber) = persistenceSystem.GetPersistenceStatus();

        shell.WriteLine($"Round Persistence Status:");
        shell.WriteLine($"  Stations tracked: {stationCount}");
        shell.WriteLine($"  Ships tracked: {shipCount}");
        shell.WriteLine($"  Last saved round: {roundNumber}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class ClearPersistentDataCommand : IConsoleCommand
{
    public string Command => "clear_persistent_data";
    public string Description => "Clear all persistent data (use with caution!)";
    public string Help => "clear_persistent_data";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entManager = IoCManager.Resolve<IEntityManager>();
        var sysManager = IoCManager.Resolve<IEntitySystemManager>();

        // Find the persistent entity and clear its data
        var query = entManager.AllEntityQueryEnumerator<RoundPersistenceComponent>();
        while (query.MoveNext(out var uid, out var persistence))
        {
            persistence.ExpeditionData.Clear();
            persistence.ShuttleRecords.Clear();
            persistence.StationRecords.Clear();
            persistence.ShipData.Clear();
            persistence.PlayerPayments.Clear();
            persistence.RoundNumber = 0;

            shell.WriteLine("Cleared all persistent data.");
            return;
        }

        shell.WriteLine("No persistent data entity found.");
    }
}
