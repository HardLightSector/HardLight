using Robust.Shared.Serialization;

namespace Content.Shared._Afterlight.Vore;

[Serializable, NetSerializable]
public sealed class VorePromptDeclineEvent(Guid prompt) : EntityEventArgs
{
    public readonly Guid Prompt = prompt;
}
