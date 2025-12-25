using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Coyote.ShuttleCrewStatus;

/// <summary>
/// Component that tracks shuttle crew status and manages IFF color based on active players aboard.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleCrewStatusComponent : Component
{
    /// <summary>
    /// The original IFF color before any crew status changes.
    /// Used to restore the color when active players are detected.
    /// </summary>
    [DataField]
    public Color? OriginalColor;

    /// <summary>
    /// Whether the shuttle currently has active players aboard.
    /// </summary>
    [DataField]
    public bool HasActiveCrew;

    /// <summary>
    /// The next time to check crew status.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextCheck = TimeSpan.Zero;
}
