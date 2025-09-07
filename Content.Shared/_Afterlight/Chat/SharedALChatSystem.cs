using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._Afterlight.Chat;

// Taken from https://github.com/RMC-14/RMC-14
public abstract class SharedALChatSystem : EntitySystem
{
    public virtual void ChatMessageToOne(
        ChatChannel channel,
        string message,
        string wrappedMessage,
        EntityUid source,
        bool hideChat,
        INetChannel client,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
    }

    public void ChatMessageToOne(
        string message,
        EntityUid target,
        ChatChannel channel = ChatChannel.Local,
        bool hideChat = false,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
        if (!TryComp(target, out ActorComponent? actor))
            return;

        ChatMessageToOne(channel,
            message,
            message,
            default,
            hideChat,
            actor.PlayerSession.Channel,
            colorOverride,
            recordReplay,
            audioPath,
            audioVolume,
            author
        );
    }

    public virtual void ChatMessageToMany(
        string message,
        string wrappedMessage,
        Filter filter,
        ChatChannel channel,
        EntityUid source = default,
        bool hideChat = false,
        Color? colorOverride = null,
        bool recordReplay = false,
        string? audioPath = null,
        float audioVolume = 0,
        NetUserId? author = null)
    {
    }
}