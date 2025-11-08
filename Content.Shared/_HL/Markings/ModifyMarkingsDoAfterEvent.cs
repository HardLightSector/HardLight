using Content.Shared.DoAfter;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared._HL.Markings;

[Serializable, NetSerializable]
public sealed partial class ModifyMarkingsDoAfterEvent : SimpleDoAfterEvent
{
    [DataField(required: true)]
    public Marking Marking;

    [DataField(required: true)]
    public string MarkingPrototypeName;

    [DataField(required: true)]
    public bool IsVisible;

    public ModifyMarkingsDoAfterEvent(Marking marking, string markingPrototypeName, bool isVisible)
    {
        Marking = marking;
        MarkingPrototypeName = markingPrototypeName;
        IsVisible = isVisible;
    }
}
