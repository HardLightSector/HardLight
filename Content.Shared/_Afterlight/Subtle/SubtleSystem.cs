using Content.Shared._Afterlight.CCVar;
using Content.Shared._Afterlight.Chat;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._Afterlight.Subtle;

public sealed class SubtleSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedALChatSystem _alChat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetConfigurationManager _netConfiguration = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<GhostComponent> _ghostQuery;

    private readonly SoundSpecifier _sound =
        new SoundPathSpecifier("/Audio/_Afterlight/Effects/Achievement/glockenspiel_ping.ogg");
    private float _range;

    public override void Initialize()
    {
        _ghostQuery = GetEntityQuery<GhostComponent>();

        SubscribeNetworkEvent<SubtleClientEvent>(OnSubtleClient);

        Subs.CVar(_config, ALCVars.ALSubtleRange, v => _range = v, true);
    }

    private void OnSubtleClient(SubtleClientEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } ent)
            return;

        if (!CanSubtle(ent))
            return;

        _adminLog.Add(LogType.ALSubtle, $"{ToPrettyString(ent)} sent subtle emote:\n{msg.Emote}");

        var wrappedMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", Identity.Name(ent, EntityManager)),
            ("entity", ent),
            ("message", FormattedMessage.RemoveMarkupOrThrow(msg.Emote)));
        var coords = _transform.GetMapCoordinates(ent);
        var userId = args.SenderSession.UserId;
        var chatFilter = Filter.Empty()
            .AddInRange(coords, _range, _player, EntityManager)
            .RemoveWhereAttachedEntity(e => _ghostQuery.HasComp(e));

        var audioFilter = chatFilter
            .Clone()
            .RemoveWhere(s =>
                s == args.SenderSession ||
                !_netConfiguration.GetClientCVar(s.Channel, ALCVars.ALSubtlePlaySound)
            );
        _audio.PlayGlobal(_sound, audioFilter, false);

        if (!msg.AntiGhost)
        {
            chatFilter.AddWhere(s =>
                _ghostQuery.HasComp(s.AttachedEntity) &&
                _netConfiguration.GetClientCVar(s.Channel, ALCVars.ALGhostSeeAllEmotes)
            );
        }

        _alChat.ChatMessageToMany(msg.Emote, wrappedMessage, chatFilter, ChatChannel.Emotes, ent, recordReplay: true, author: userId);
    }

    public bool CanSubtle(EntityUid ent)
    {
        if (_mobState.IsIncapacitated(ent))
            return false;

        if (HasComp<GhostComponent>(ent) && !HasComp<BypassInteractionChecksComponent>(ent))
            return false;

        return true;
    }
}