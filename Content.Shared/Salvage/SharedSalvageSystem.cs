using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Dataset;
using Content.Shared.Procedural;
using Content.Shared.Procedural.Loot;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Salvage;

public abstract partial class SharedSalvageSystem : EntitySystem
{
    [Dependency] protected readonly IConfigurationManager CfgManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    /// <summary>
    /// Main loot table for salvage expeditions.
    /// </summary>
    public static readonly ProtoId<SalvageLootPrototype> ExpeditionsLootProto = "NFSalvageLootModerate"; // Frontier: SalvageLoot<NFSalvageLootModerate

    public string GetFTLName(LocalizedDatasetPrototype dataset, int seed)
    {
        var random = new System.Random(seed);
        return $"{Loc.GetString(dataset.Values[random.Next(dataset.Values.Count)])}-{random.Next(10, 100)}-{(char) (65 + random.Next(26))}";
    }

    public SalvageMission GetMission(SalvageMissionType config, SalvageDifficultyPrototype difficulty, int seed) // Frontier: add config
    {
        // This is on shared to ensure the client display for missions and what the server generates are consistent
        var modifierBudget = difficulty.ModifierBudget;
        var rand = new System.Random(seed);

        // Run budget in order of priority
        // - Biome
        // - Lighting
        // - Atmos
        var biome = GetMod<SalvageBiomeModPrototype>(rand, ref modifierBudget);
        var light = GetBiomeMod<SalvageLightMod>(biome.ID, rand, ref modifierBudget);
        var temp = GetBiomeMod<SalvageTemperatureMod>(biome.ID, rand, ref modifierBudget);
        var air = GetBiomeMod<SalvageAirMod>(biome.ID, rand, ref modifierBudget);
        var dungeon = GetBiomeMod<SalvageDungeonModPrototype>(biome.ID, rand, ref modifierBudget);
        // Frontier: restrict factions per difficulty
        // var factionProtos = _proto.EnumeratePrototypes<SalvageFactionPrototype>().ToList();
        var factionProtos = _proto.EnumeratePrototypes<SalvageFactionPrototype>()
            .Where(x =>
                {
                    // Accept if no filter is set
                    if (!x.Configs.TryGetValue("Difficulties", out var difficulties) || string.IsNullOrWhiteSpace(difficulties))
                        return true;

                    // Robust parsing: trim items, ignore empty entries, and compare case-insensitively
                    var target = difficulty.ID.ToString();
                    var tokens = difficulties.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                              .Select(t => t.Trim());
                    return tokens.Any(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));
                }
            ).ToList();
        // Fallback: if no factions match the difficulty filter, allow any faction to avoid crashes
        if (factionProtos.Count == 0)
            factionProtos = _proto.EnumeratePrototypes<SalvageFactionPrototype>().ToList();
        // End Frontier: difficulties per faction
        factionProtos.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        var faction = factionProtos[rand.Next(factionProtos.Count)];

        var mods = new List<string>();

        if (air.Description != string.Empty)
        {
            mods.Add(Loc.GetString(air.Description));
        }

        // only show the description if there is an atmosphere since wont matter otherwise
        if (temp.Description != string.Empty && !air.Space)
        {
            mods.Add(Loc.GetString(temp.Description));
        }

        if (light.Description != string.Empty)
        {
            mods.Add(Loc.GetString(light.Description));
        }

        var duration = TimeSpan.FromSeconds(CfgManager.GetCVar(CCVars.SalvageExpeditionDuration));

        return new SalvageMission(seed, dungeon.ID, faction.ID, biome.ID, air.ID, temp.Temperature, light.Color, duration, mods, difficulty.ID, config); // Frontier: add difficulty.ID, config
    }

    public T GetBiomeMod<T>(string biome, System.Random rand, ref float rating) where T : class, IPrototype, IBiomeSpecificMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating || (mod.Biomes != null && !mod.Biomes.Contains(biome)))
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    public T GetMod<T>(System.Random rand, ref float rating) where T : class, IPrototype, ISalvageMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating)
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    private List<string> GetRewards(int difficulty, System.Random rand)
    {
        var rewards = new List<string>(3);
        var ids = RewardsForDifficulty(difficulty);
        foreach (var id in ids)
        {
            var weights = _proto.Index<WeightedRandomEntityPrototype>(id);
            rewards.Add(weights.Pick(rand));
        }
        return rewards;
    }

    private string[] RewardsForDifficulty(int rating)
    {
        var t1 = "ExpeditionRewardT1";
        var t2 = "ExpeditionRewardT2";
        var t3 = "ExpeditionRewardT3";
        var t4 = "ExpeditionRewardT4";
        var t5 = "ExpeditionRewardT5";
        switch (rating)
        {
            case 0: return new[] { t1 };
            case 1: return new[] { t2 };
            case 2: return new[] { t3 };
            case 3: return new[] { t4 };
            case 4: return new[] { t5 };
            default: throw new NotImplementedException();
        }
    }
}

[Serializable, NetSerializable]
public enum SalvageMissionType : byte
{
    /// <summary>
    /// Destroy the specified structures in a dungeon.
    /// </summary>
    Destruction = 0,

    /// <summary>
    /// Kill a large creature in a dungeon.
    /// </summary>
    Elimination = 1,

    /// <summary>
    /// Maximum value for random generation, should not be used directly.
    /// </summary>
    Max = Elimination,
}
// End Frontier
