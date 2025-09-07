using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Dialog;

// Taken from https://github.com/RMC-14/RMC-14
[Serializable, NetSerializable]
public enum DialogUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class DialogOptionBuiMsg(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class DialogInputBuiMsg(string input) : BoundUserInterfaceMessage
{
    public readonly string Input = input;
}

[Serializable, NetSerializable]
public sealed class DialogConfirmBuiMsg : BoundUserInterfaceMessage;
