namespace Content.Shared._Afterlight.Dialog;

// Taken from https://github.com/RMC-14/RMC-14
[ByRefEvent]
public readonly record struct DialogChosenEvent(EntityUid Actor, int Index);
