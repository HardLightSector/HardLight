using Robust.Shared.Configuration;

namespace Content.Shared._Afterlight.CCVar;

[CVarDefs]
public sealed partial class ALCVars
{
    public static readonly CVarDef<int> ALVoreSpacesLimit =
        CVarDef.Create("al.vore_spaces_limit", 10, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreNameCharacterLimit =
        CVarDef.Create("al.vore_name_character_limit", 50, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreDescriptionCharacterLimit =
        CVarDef.Create("al.vore_description_character_limit", 1000, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBurnMin =
        CVarDef.Create("al.vore_damage_burn_min", 0f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBurnMax =
        CVarDef.Create("al.vore_damage_burn_max", 2.5f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBurnDefault =
        CVarDef.Create("al.vore_damage_burn_default", 0f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBruteMin =
        CVarDef.Create("al.vore_damage_brute_min", 0f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBruteMax =
        CVarDef.Create("al.vore_damage_brute_max", 2.5f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<float> ALVoreDamageBruteDefault =
        CVarDef.Create("al.vore_damage_brute_default", 0f, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALVoreMuffleRadioDefault =
        CVarDef.Create("al.vore_muffle_radio_default", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreChanceToEscapeDefault =
        CVarDef.Create("al.vore_chance_to_escape_default", 100, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreTimeToEscapeMin =
        CVarDef.Create("al.vore_time_to_escape_min", 2, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreTimeToEscapeMax =
        CVarDef.Create("al.vore_time_to_escape_max", 60, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreTimeToEscapeDefault =
        CVarDef.Create("al.vore_time_to_escape_default", 15, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALVoreCanTasteDefault =
        CVarDef.Create("al.vore_can_taste_default", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<string> ALVoreInsertionVerbDefault =
        CVarDef.Create("al.vore_insertion_verb_default", "ingest", CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreInsertionVerbCharacterLimit =
        CVarDef.Create("al.vore_insertion_verb_character_limit", 20, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<string> ALVoreReleaseVerbDefault =
        CVarDef.Create("al.vore_release_verb_default", "expels", CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreReleaseVerbCharacterLimit =
        CVarDef.Create("al.vore_release_verb_character_limit", 20, CVar.REPLICATED | CVar.SERVER);

    // public static readonly CVarDef<bool> ALVoreFancySoundsDefault =
    //     CVarDef.Create("al.vore_fancy_sounds_default", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALVoreFleshySpaceDefault =
        CVarDef.Create("al.vore_fleshy_space_default", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<bool> ALVoreInternalSoundLoopDefault =
        CVarDef.Create("al.vore_internal_sound_loop_default", true, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<string> ALVoreInsertionSoundDefault =
        CVarDef.Create("al.vore_insertion_sound_default", "Gulp", CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<string> ALVoreReleaseSoundDefault =
        CVarDef.Create("al.vore_release_sound_default", "Splatter", CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreMessagesCountLimit =
        CVarDef.Create("al.vore_messages_count_limit", 10, CVar.REPLICATED | CVar.SERVER);

    public static readonly CVarDef<int> ALVoreMessagesCharacterLimit =
        CVarDef.Create("al.vore_messages_character_limit", 300, CVar.REPLICATED | CVar.SERVER);
}
