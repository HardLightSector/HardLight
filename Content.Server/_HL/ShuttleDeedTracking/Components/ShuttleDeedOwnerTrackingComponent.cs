using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._HL.ShuttleDeedTracking.Components;

/// <summary>
/// Component that tracks the shuttle deed owner's session status.
/// After a configurable number of consecutive inactive checks, the grid will be deleted.
/// </summary>
[RegisterComponent]
public sealed partial class ShuttleDeedOwnerTrackingComponent : Component
{
    /// <summary>
    /// The number of consecutive checks where the deed owner was inactive/offline.
    /// </summary>
    [DataField]
    public int InactiveCheckCount;

    /// <summary>
    /// The maximum number of consecutive inactive checks before the grid is deleted.
    /// </summary>
    [DataField]
    public int MaxInactiveChecks = 6;

    /// <summary>
    /// The next time to check the owner's session status.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextCheck = TimeSpan.Zero;
}
