using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Vore;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedVoreSystem))]
public sealed partial class CanBeVorePredatorComponent : Component;
