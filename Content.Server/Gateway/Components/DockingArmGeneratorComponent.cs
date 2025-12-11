using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Gateway.Components;

/// <summary>
/// Generates docking arm destinations that spawn specific grid structures.
/// Similar to GatewayGeneratorComponent but spawns pre-made grids instead of procedural dungeons.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class DockingArmGeneratorComponent : Component
{
    /// <summary>
    /// Prototype to spawn on the generated map (the gateway entity).
    /// </summary>
    [DataField]
    public EntProtoId? Proto = "Gateway";

    /// <summary>
    /// Next time another docking arm unlocks.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUnlock;

    /// <summary>
    /// How long it takes to unlock another docking arm once one is selected.
    /// </summary>
    [DataField]
    public TimeSpan UnlockCooldown = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maps we've generated.
    /// </summary>
    [DataField]
    public List<EntityUid> Generated = new();

    /// <summary>
    /// List of grid YAML paths to randomly select from for docking arm structures.
    /// </summary>
    [DataField]
    public List<string> DockingArmGrids = new()
    {
        "/Maps/Structures/dock.yml",

    };

    /// <summary>
    /// Number of initial docking arm options to generate.
    /// </summary>
    [DataField]
    public int InitialCount = 0;

    /// <summary>
    /// Maximum distance from station to spawn the docking arm.
    /// </summary>
    [DataField]
    public int MaxSpawnDistance = 256;

    /// <summary>
    /// Minimum distance from station to spawn the docking arm.
    /// </summary>
    [DataField]
    public int MinSpawnDistance = 64;

    /// <summary>
    /// Counter for dock numbering. Increments each time a dock is spawned.
    /// Not serialized - resets to 1 on round restart.
    /// </summary>
    public int DockCounter = 1;

    /// <summary>
    /// Maximum number of docks that can be spawned at once.
    /// </summary>
    [DataField]
    public int MaxDocks = 10;

    /// <summary>
    /// List of currently spawned dock grid UIDs.
    /// Not serialized - resets on round restart.
    /// </summary>
    public List<EntityUid> SpawnedDocks = new();
}
