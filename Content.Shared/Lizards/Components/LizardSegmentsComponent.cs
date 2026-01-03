using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Lizards.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class LizardSegmentsComponent : Component
{
    [DataField] public string BodyPrototype = string.Empty;
    [DataField] public string Body2Prototype = string.Empty;
    [DataField] public string TailPrototype = string.Empty;

    // Optional uniform scales for each spawned segment. Defaults to 1.0 (no scaling).
    [DataField] public float BodyScale = 1.0f;
    [DataField] public float Body2Scale = 1.0f;
    [DataField] public float TailScale = 1.0f;
}
