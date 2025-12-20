using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Vore;

[Serializable, NetSerializable]
public enum VoreUi
{
    Key,
}

[Serializable, NetSerializable]
public sealed class VoreAddSpaceBuiMsg : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class VoreErrorSavingEvent(Guid id) : EntityEventArgs
{
    public readonly Guid Id = id;
}

[Serializable, NetSerializable]
public sealed class VoreRetrySavingEvent(Guid id) : EntityEventArgs
{
    public readonly Guid Id = id;
}

[Serializable, NetSerializable]
public sealed class VoreChangeMessageBuiMsg(VoreMessageType type, int index, string text) : BoundUserInterfaceMessage
{
    public readonly VoreMessageType Type = type;
    public readonly int Index = index;
    public readonly string Text = text;
}

[Serializable, NetSerializable]
public sealed class VoreSetSpaceSettingsBuiMsg(int index, VoreSpace space) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly VoreSpace Space = space;
}

[Serializable, NetSerializable]
public sealed class VoreDeleteSpaceBuiMsg(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class VoreSetActiveSpaceBuiMsg(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class VoreSetOverlayBuiMsg(int index, EntProtoId<VoreOverlayComponent>? overlay) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly EntProtoId<VoreOverlayComponent>? Overlay = overlay;
}

[Serializable, NetSerializable]
public sealed class VoreSetOverlayColorBuiMsg(int index, Color color) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly Color Color = color;
}
