using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._NF.Implants.Components;

/// <summary>
/// Marker component for a medical implant that, upon the owner's death, will schedule a teleport to a Fulton beacon
/// after a short delay unless the owner is revived before the timer expires.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MedicalTeleportImplantComponent : Component
{
    /// <summary>
    /// Delay before teleporting after death.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("teleportDelay")]
    public TimeSpan TeleportDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Sound played when the teleport occurs.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("teleportSound")]
    public SoundSpecifier? TeleportSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    /// <summary>
    /// Sound played immediately on death; is looped and stopped upon revival or implant removal.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("flatlineSound")]
    public SoundSpecifier? FlatlineSound = new SoundPathSpecifier("/Audio/_HL/Effects/Flatline.ogg");

    /// <summary>
    /// Server-side handle to the currently playing flatline stream (if any). Not networked.
    /// </summary>
    [ViewVariables]
    public EntityUid? FlatlineStream;

    /// <summary>
    /// Minimum time between successful teleports triggered by this implant.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("teleportCooldown")]
    public TimeSpan TeleportCooldown = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Next time at which a teleport may occur. Server-only; not networked.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("nextAllowedTeleport", customTypeSerializer: typeof(Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.TimeOffsetSerializer))]
    public TimeSpan NextAllowedTeleport = TimeSpan.Zero;
}
