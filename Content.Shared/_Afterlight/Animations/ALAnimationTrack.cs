using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Animations;

[DataRecord]
[Serializable, NetSerializable]
public readonly record struct ALAnimationTrack(object? LayerKey, List<ALKeyFrame> KeyFrames);
