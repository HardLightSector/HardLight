using Robust.Shared.Configuration;

namespace Content.Shared.HL.CCVar;

/// <summary>
/// CVars for HardLight profiling / diagnostics utilities.
/// </summary>
/// <summary>
/// Marked with CVarDefs so Robust's CVar loader registers these definitions.
/// </summary>
[CVarDefs]
public sealed class HLProfilerCCVars
{
    /// <summary>
    /// Enables the entity spawn profiler system when true.
    /// Server only; safe to hot-toggle at runtime.
    /// </summary>
    public static readonly CVarDef<bool> EntitySpawnProfilerEnabled =
        CVarDef.Create("hl.profiler.entity_spawns.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE,
            "Enable periodic logging of entity spawn counts by prototype.");

    /// <summary>
    /// How often (in seconds) the profiler reports when enabled.
    /// </summary>
    public static readonly CVarDef<float> EntitySpawnProfilerInterval =
        CVarDef.Create("hl.profiler.entity_spawns.interval", 10f, CVar.SERVERONLY | CVar.ARCHIVE,
            "Reporting interval in seconds for the entity spawn profiler.");

    /// <summary>
    /// Maximum number of prototype lines to include per report.
    /// </summary>
    public static readonly CVarDef<int> EntitySpawnProfilerTop =
        CVarDef.Create("hl.profiler.entity_spawns.top", 15, CVar.SERVERONLY | CVar.ARCHIVE,
            "Maximum prototype rows to list in each entity spawn profiler report.");
}
