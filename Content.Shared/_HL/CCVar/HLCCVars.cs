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
        CVarDef.Create("hardlight.round_persistence.enabled", false, CVar.SERVERONLY);

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
}
