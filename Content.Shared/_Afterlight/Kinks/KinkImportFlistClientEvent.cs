using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Kinks;

[Serializable, NetSerializable]
public sealed class KinkImportFlistClientEvent(string link) : EntityEventArgs
{
    public string Link = link;
}
