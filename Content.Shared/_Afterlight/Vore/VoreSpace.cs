using Content.Shared.Database._Afterlight;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Vore;

[Serializable, NetSerializable]
[DataRecord]
public partial record struct VoreSpace(
    Guid Id,
    string Name,
    string Description,
    EntProtoId<VoreOverlayComponent>? Overlay,
    VoreSpaceMode Mode,
    FixedPoint2 BurnDamage,
    FixedPoint2 BruteDamage,
    bool MuffleRadio,
    int ChanceToEscape,
    TimeSpan TimeToEscape,
    bool CanTaste,
    string? InsertionVerb,
    string? ReleaseVerb,
    // bool FancySounds,
    bool Fleshy,
    bool InternalSoundLoop,
    SoundPathSpecifier? InsertionSound,
    SoundPathSpecifier? ReleaseSound,
    Dictionary<VoreMessageType, List<string>> Messages
);
