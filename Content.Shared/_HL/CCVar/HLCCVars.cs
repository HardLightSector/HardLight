using Robust.Shared.Configuration;

namespace Content.Shared.HL.CCVar;

/// <summary>
/// Configuration variables for HardLight-specific features
/// </summary>
[CVarDefs]
public sealed class HLCCVars
{
    /// <summary>
    /// Enable round persistence system to maintain ship functionality across round restarts
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceEnabled =
        CVarDef.Create("hardlight.round_persistence.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable expedition data persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceExpeditions =
        CVarDef.Create("hardlight.round_persistence.expeditions", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable shuttle records persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceShuttleRecords =
        CVarDef.Create("hardlight.round_persistence.shuttle_records", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable station records and manifest persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceStationRecords =
        CVarDef.Create("hardlight.round_persistence.station_records", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable ship IFF and association persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceShipData =
        CVarDef.Create("hardlight.round_persistence.ship_data", true, CVar.SERVERONLY);

    /// <summary>
    /// Enable player payment data persistence
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistencePlayerPayments =
        CVarDef.Create("hardlight.round_persistence.player_payments", true, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of rounds to keep persistence data for
    /// </summary>
    public static readonly CVarDef<int> RoundPersistenceMaxRounds =
        CVarDef.Create("hardlight.round_persistence.max_rounds", 10, CVar.SERVERONLY);

    /// <summary>
    /// Enable verbose logging for the persistence system
    /// </summary>
    public static readonly CVarDef<bool> RoundPersistenceDebugLogging =
        CVarDef.Create("hardlight.round_persistence.debug_logging", false, CVar.SERVERONLY);

    // Vending performance controls
    public static readonly CVarDef<bool> VendingLazyRestock =
        CVarDef.Create("hardlight.vending.lazy_restock", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> VendingRestockBatch =
        CVarDef.Create("hardlight.vending.restock_batch", 2, CVar.SERVERONLY, desc: "Max vending machines to restock per tick.");

    public static readonly CVarDef<int> VendingRestockTickMs =
        CVarDef.Create("hardlight.vending.restock_tick_ms", 250, CVar.SERVERONLY, desc: "Interval in ms between vending restock ticks.");

    // Exclude vending machines from shuttle save files to avoid load-time spikes
    public static readonly CVarDef<bool> ExcludeVendingInShipSave =
        CVarDef.Create("hardlight.vending.exclude_in_ship_save", true, CVar.SERVERONLY,
            desc: "If true, vending machines are omitted from shuttle save files to reduce load hitches.");

    // World chunk logging / behavior
    public static readonly CVarDef<bool> WorldChunkDebugLogs =
        CVarDef.Create("hardlight.world.debug_chunk_logs", false, CVar.SERVERONLY, desc: "Enable world chunk load/unload debug logs.");

    // Ship load performance controls
    public static readonly CVarDef<bool> ShipLoadAsync =
        CVarDef.Create("hardlight.shipload.async", false, CVar.SERVERONLY, desc: "Load ships asynchronously over multiple ticks.");

    public static readonly CVarDef<int> ShipLoadBatchNonContained =
        CVarDef.Create("hardlight.shipload.batch_noncontained", 64, CVar.SERVERONLY, desc: "Max non-contained entities to spawn per tick during ship load.");

    public static readonly CVarDef<int> ShipLoadBatchContained =
        CVarDef.Create("hardlight.shipload.batch_contained", 96, CVar.SERVERONLY, desc: "Max contained entities to spawn/insert per tick during ship load.");

    public static readonly CVarDef<int> ShipLoadTimeBudgetMs =
        CVarDef.Create("hardlight.shipload.time_budget_ms", 8, CVar.SERVERONLY, desc: "Soft time budget per tick for ship load processing (ms).");

    public static readonly CVarDef<bool> ShipLoadDecals =
        CVarDef.Create("hardlight.shipload.load_decals", false, CVar.SERVERONLY, desc: "If true, restore decals when loading ships (can be expensive).");

    public static readonly CVarDef<bool> ShipLoadLogProgress =
        CVarDef.Create("hardlight.shipload.log_progress", false, CVar.SERVERONLY, desc: "Log ship load progress each tick.");
}
