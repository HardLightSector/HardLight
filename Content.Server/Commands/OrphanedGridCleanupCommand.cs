using Content.Server.Administration;
using Content.Server.GridSplit;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class OrphanedGridCleanupCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override string Command => "orphanedgridcleanup";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var system = _entityManager.System<OrphanedGridCleanupSystem>();

        if (args.Length == 0)
        {
            shell.WriteLine("Usage: orphanedgridcleanup <subcommand> [value]");
            shell.WriteLine("  enable - Enables automatic cleanup of orphaned grids from splits");
            shell.WriteLine("  disable - Disables automatic cleanup of orphaned grids from splits");
            shell.WriteLine("  settiles <count> - Sets the minimum tile count threshold");
            shell.WriteLine("  cleanup <gridId> - Manually cleans up a specific grid if it's orphaned");
            shell.WriteLine("  enableempty - Enables periodic cleanup of empty/nameless grids");
            shell.WriteLine("  disableempty - Disables periodic cleanup of empty/nameless grids");
            shell.WriteLine("  force - Forces an immediate empty grid cleanup check");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "enable":
                system.SetEnabled(true);
                shell.WriteLine("Orphaned grid cleanup enabled.");
                break;

            case "disable":
                system.SetEnabled(false);
                shell.WriteLine("Orphaned grid cleanup disabled.");
                break;

            case "enableempty":
                system.SetEmptyGridCleanupEnabled(true);
                shell.WriteLine("Empty grid periodic cleanup enabled.");
                break;

            case "disableempty":
                system.SetEmptyGridCleanupEnabled(false);
                shell.WriteLine("Empty grid periodic cleanup disabled.");
                break;

            case "force":
                var deleted = system.ForceEmptyGridCleanup();
                shell.WriteLine($"Forced empty grid cleanup. Deleted {deleted} grid(s).");
                break;

            case "settiles":
                if (args.Length < 2 || !int.TryParse(args[1], out var tileCount))
                {
                    shell.WriteError("Invalid tile count. Usage: orphanedgridcleanup settiles <count>");
                    return;
                }
                system.SetMinimumTileCount(tileCount);
                shell.WriteLine($"Minimum tile count set to {tileCount}.");
                break;

            case "cleanup":
                if (args.Length < 2 || !EntityUid.TryParse(args[1], out var gridUid))
                {
                    shell.WriteError("Invalid grid ID. Usage: orphanedgridcleanup cleanup <gridId>");
                    return;
                }

                if (!_entityManager.HasComponent<MapGridComponent>(gridUid))
                {
                    shell.WriteError($"Entity {gridUid} is not a grid.");
                    return;
                }

                if (system.TryCleanupGrid(gridUid))
                {
                    shell.WriteLine($"Grid {gridUid} has been queued for deletion.");
                }
                else
                {
                    shell.WriteLine($"Grid {gridUid} does not meet orphan criteria and was not deleted.");
                }
                break;

            default:
                shell.WriteError($"Unknown subcommand: {args[0]}");
                shell.WriteLine("Valid subcommands: enable, disable, enableempty, disableempty, force, settiles, cleanup");
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                new[] { "enable", "disable", "enableempty", "disableempty", "force", "settiles", "cleanup" },
                "<subcommand>");
        }

        return CompletionResult.Empty;
    }
}
