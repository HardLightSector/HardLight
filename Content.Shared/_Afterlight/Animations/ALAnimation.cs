using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Animations;

[DataRecord]
[Serializable, NetSerializable]
public readonly record struct ALAnimation(TimeSpan Length, List<ALAnimationTrack> AnimationTracks);
