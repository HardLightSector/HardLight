using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.Shared._Compat.Components;

// Compatibility shim for deprecated 'RadioChime' component referenced in old prototypes.
[RegisterComponent, ComponentProtoName("RadioChime")]
public sealed partial class RadioChimeCompatComponent : Component
{
    [DataField("sound")]
    public SoundSpecifier? Sound;
}
