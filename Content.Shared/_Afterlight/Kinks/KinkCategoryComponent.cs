using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Kinks;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedKinkSystem))]
public sealed partial class KinkCategoryComponent : Component;
