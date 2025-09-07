using Content.Server.Chat.Managers;
using Content.Shared._Afterlight.Chat;
using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Afterlight.Chat;

public sealed class ALChatSystem : SharedALChatSystem
{
    [Dependency] private readonly IChatManager _chat = default!;

    public override void ChatMessageToOne(
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
        _chat.ChatMessageToOne(
            channel,
            message,
            wrappedMessage,
            source,
            hideChat,
            client,
            colorOverride,
            recordReplay,
            audioPath,
            audioVolume,
            author
        );
    }

    public override void ChatMessageToMany(
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
        _chat.ChatMessageToManyFiltered(
            filter,
            channel,
            message,
            wrappedMessage,
            source,
            hideChat,
            recordReplay,
            colorOverride,
            audioPath,
            audioVolume
        );
    }
}