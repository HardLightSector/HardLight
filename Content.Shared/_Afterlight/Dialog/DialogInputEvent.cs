using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Dialog;

// Taken from https://github.com/RMC-14/RMC-14
[Serializable, NetSerializable]
public abstract record DialogInputEvent(string Message);
