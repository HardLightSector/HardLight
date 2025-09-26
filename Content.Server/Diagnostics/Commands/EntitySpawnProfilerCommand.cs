using Content.Shared.HL.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server.Diagnostics.Commands;

/// <summary>
/// Console command to toggle / configure the entity spawn profiler without editing config files.
/// Usage:
///   spawnprof on
///   spawnprof off
///   spawnprof interval 5
///   spawnprof top 25
/// </summary>
public sealed class EntitySpawnProfilerCommand : IConsoleCommand
{
    public string Command => "spawnprof";
    public string Description => "Toggle or configure the entity spawn profiler (diagnoses unexpected background spawns).";
    public string Help => "spawnprof on|off | interval <seconds> | top <count>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var cfg = IoCManager.Resolve<IConfigurationManager>();

        if (args.Length == 0)
        {
            shell.WriteLine("Usage: " + Help);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                cfg.SetCVar(HLProfilerCCVars.EntitySpawnProfilerEnabled, true);
                shell.WriteLine("Entity spawn profiler enabled.");
                break;
            case "off":
                cfg.SetCVar(HLProfilerCCVars.EntitySpawnProfilerEnabled, false);
                shell.WriteLine("Entity spawn profiler disabled.");
                break;
            case "interval":
                if (args.Length < 2 || !float.TryParse(args[1], out var seconds))
                {
                    shell.WriteError("Expected: spawnprof interval <seconds>");
                    return;
                }
                cfg.SetCVar(HLProfilerCCVars.EntitySpawnProfilerInterval, MathF.Max(0.5f, seconds));
                shell.WriteLine($"Set profiler interval to {seconds:F2}s");
                break;
            case "top":
                if (args.Length < 2 || !int.TryParse(args[1], out var top))
                {
                    shell.WriteError("Expected: spawnprof top <count>");
                    return;
                }
                cfg.SetCVar(HLProfilerCCVars.EntitySpawnProfilerTop, Math.Max(1, top));
                shell.WriteLine($"Set profiler top count to {top}");
                break;
            default:
                shell.WriteError("Unrecognized subcommand. " + Help);
                break;
        }
    }
}
