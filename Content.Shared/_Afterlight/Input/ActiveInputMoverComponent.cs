using Robust.Shared.GameStates;

namespace Content.Shared._Afterlight.Input;

// Taken from https://github.com/RMC-14/RMC-14
[RegisterComponent, NetworkedComponent]
[Access(typeof(ALInputSystem))]
public sealed partial class ActiveInputMoverComponent : Component;
