using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Animations;

[DataRecord]
[Serializable, NetSerializable]
public record struct ALAnimationId(string Id) : ISelfSerialize
{
    public void Deserialize(string value)
    {
        Id = value;
    }

    public string Serialize()
    {
        return Id;
    }
}
