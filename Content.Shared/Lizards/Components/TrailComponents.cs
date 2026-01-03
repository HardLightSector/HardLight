using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Lizards.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class TrailLeaderComponent : Component
{
    [DataField] public int BufferSize = 16;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class TrailFollowerComponent : Component
{
    [DataField] public EntityUid Leader;
    [DataField] public int Delay = 4;
    [DataField] public float Offset = 0.0f; // units behind along facing
    [DataField] public float SmoothFactor = 0.0f; // 0 = snap, 0.1..1 = interpolate
    [DataField] public float RotationOffsetDeg = -90f; // adjust sprite forward axis
    // If follower is within this distance to its target while leader is inactive, do not advance.
    [DataField] public float StopDeadzone = 0.1f;
}
