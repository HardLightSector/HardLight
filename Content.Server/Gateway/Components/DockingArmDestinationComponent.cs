namespace Content.Server.Gateway.Components;

/// <summary>
/// Destination created by <see cref="DockingArmGeneratorComponent"/>
/// Marks a map that contains a docking arm structure.
/// </summary>
[RegisterComponent]
public sealed partial class DockingArmDestinationComponent : Component
{
    /// <summary>
    /// Generator that created this docking arm destination.
    /// Can be null if this is a manually-created destination.
    /// </summary>
    [DataField]
    public EntityUid? Generator;

    /// <summary>
    /// Is the map locked from being used still or unlocked.
    /// Used in conjunction with the attached generator's NextUnlock.
    /// For manual destinations without a generator, this should be false.
    /// </summary>
    [DataField]
    public bool Locked = false;

    /// <summary>
    /// Has the docking arm grid been spawned yet.
    /// </summary>
    [DataField]
    public bool Loaded;

    /// <summary>
    /// Name of this docking arm destination.
    /// </summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Path to the grid YAML file for this docking arm.
    /// </summary>
    [DataField]
    public string GridPath = string.Empty;

    /// <summary>
    /// The spawned docking arm grid entity.
    /// </summary>
    [DataField]
    public EntityUid? DockingArmGrid;
}
