using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Afterlight.CCVar;

[CVarDefs]
public sealed partial class ALCVars : CVars
{
    // Taken from https://github.com/RMC-14/RMC-14
    public static readonly CVarDef<float> VolumeGainCassettes =
        CVarDef.Create("al.volume_gain_cassettes", 0.33f, CVar.REPLICATED | CVar.CLIENT | CVar.ARCHIVE);

    // Taken from https://github.com/RMC-14/RMC-14
    public static readonly CVarDef<float> ALMovementPenCapSubtract =
        CVarDef.Create("al.movement_pen_cap_subtract", 0.8f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALMaxPickupDifference =
        CVarDef.Create("al.max_pickup_difference", 1.05f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALLobbyStartPaused =
        CVarDef.Create("al.lobby_start_paused", false, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALUseLoadoutRequirements =
        CVarDef.Create("al.use_loadout_requirements", true, CVar.REPLICATED | CVar.SERVER);

    // Taken from https://github.com/RMC-14/RMC-14
    public static readonly CVarDef<bool> ALActiveInputMoverEnabled =
        CVarDef.Create("al.active_input_mover_enabled", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALSubtleMaxCharacters =
        CVarDef.Create("al.subtle_max_characters", 2048, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALSubtleRange =
        CVarDef.Create("al.subtle_range", 1.5f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALSubtlePlaySound =
        CVarDef.Create("al.subtle_play_sound", true, CVar.REPLICATED | CVar.CLIENT);

    public static readonly CVarDef<bool> ALGhostSeeAllEmotes =
        CVarDef.Create("al.ghost_see_all_emotes", false, CVar.REPLICATED | CVar.CLIENT);
}
