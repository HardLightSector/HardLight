using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Vore;

[Serializable, NetSerializable]
public enum VoreMessageType
{
    DigestOwner,
    DigestPrey,
    AbsorbOwner,
    AbsorbPrey,
    UnabsorbOwner,
    UnabsorbPrey,
    StruggleOutside,
    StruggleInside,
    AbsorbedStruggleOutside,
    AbsorbedStruggleInside,
    EscapeAttemptOwner,
    EscapeAttemptPrey,
    EscapeOwner,
    EscapePrey,
    EscapeOutside,
    EscapeFailOwner,
    EscapeFailPrey,
}
