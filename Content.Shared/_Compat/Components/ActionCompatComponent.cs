using Robust.Shared.GameObjects;

namespace Content.Shared._Compat.Components;

// Compatibility shim for deprecated 'Action' component referenced in old prototypes.
[RegisterComponent, ComponentProtoName("Action")]
public sealed partial class ActionCompatComponent : Component
{
}
