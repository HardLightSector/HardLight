using Content.Server.Administration.Logs;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Players;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server.Traits;

public sealed class TraitSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTimeTracking = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly AdminSystem _adminSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SharedHandsSystem _sharedHandsSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    // When the player is spawned in, add all trait components selected during character creation
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var pointsTotal = _configuration.GetCVar(CCVars.GameTraitsDefaultPoints);
        var traitSelections = _configuration.GetCVar(CCVars.GameTraitsMax);

        if (args.JobId is not null && _prototype.TryIndex<JobPrototype>(args.JobId, out var jobPrototype)
            && jobPrototype is not null && !jobPrototype.ApplyTraits)
            return;

        var sortedTraits = new List<TraitPrototype>();
        foreach (var traitId in args.Profile.TraitPreferences)
        {
            if (_prototype.TryIndex<TraitPrototype>(traitId, out var traitPrototype))
            {
                sortedTraits.Add(traitPrototype);
            }
            else
            {
                DebugTools.Assert($"No trait found with ID {traitId}!");
                return;
            }
        }

        sortedTraits.Sort();

        foreach (var traitPrototype in sortedTraits)
        {
            // Check requirements if they exist
            if (traitPrototype.Requirements.Count > 0)
            {
                var job = _prototype.Index<JobPrototype>(args.JobId ?? _prototype.EnumeratePrototypes<JobPrototype>().First().ID);
                var playTimes = _playTimeTracking.GetTrackerTimes(args.Player);
                var whitelisted = args.Player.ContentData()?.Whitelisted ?? false;

                var requirementsMet = true;
                foreach (var requirement in traitPrototype.Requirements)
                {
                    if (!requirement.Check(EntityManager, _prototype, args.Profile, playTimes, out _))
                    {
                        requirementsMet = false;
                        break;
                    }
                }

                if (!requirementsMet)
                    continue;
            }

            // To check for cheaters
            pointsTotal -= traitPrototype.Cost;
            --traitSelections;

            AddTrait(args.Mob, traitPrototype);
        }

        if (pointsTotal < 0 || traitSelections < 0)
            PunishCheater(args.Mob);
    }

    /// <summary>
    ///     Adds a single Trait Prototype to an Entity.
    /// </summary>
    public void AddTrait(EntityUid uid, TraitPrototype traitPrototype)
    {
        // Check whitelist/blacklist
        if (_whitelistSystem.IsWhitelistFail(traitPrototype.Whitelist, uid) ||
            _whitelistSystem.IsBlacklistPass(traitPrototype.Blacklist, uid))
            return;

        // Add all components required by the prototype
        EntityManager.AddComponents(uid, traitPrototype.Components, traitPrototype.ReplaceComponents); // Hardlight, ReplaceComponents change

        // Add item required by the trait
        if (traitPrototype.TraitGear != null && TryComp(uid, out HandsComponent? handsComponent))
        {
            var coords = Transform(uid).Coordinates;
            var inhandEntity = EntityManager.SpawnEntity(traitPrototype.TraitGear, coords);
            _sharedHandsSystem.TryPickup(uid,
                inhandEntity,
                checkActionBlocker: false,
                handsComp: handsComponent);
        }
    }

    /// <summary>
    ///     On a non-cheating client, it is not possible to save a character with a negative number of traits. This can however
    ///     trigger incorrectly if a character was saved, and then at a later point in time an admin changes the traits Cvars to reduce the points.
    ///     Or if the points costs of traits is increased.
    /// </summary>
    private void PunishCheater(EntityUid uid)
    {
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(uid):entity} attempted to spawn with an invalid trait list. This might be a mistake, or they might be cheating");

        if (!_configuration.GetCVar(CCVars.TraitsPunishCheaters)
            || !_playerManager.TryGetSessionByEntity(uid, out var targetPlayer))
            return;

        // For maximum comedic effect, this is plenty of time for the cheater to get on station and start interacting with people.
        var timeToDestroy = _random.NextFloat(120, 360);

        Timer.Spawn(TimeSpan.FromSeconds(timeToDestroy), () => VaporizeCheater(targetPlayer));
    }

    /// <summary>
    ///     https://www.youtube.com/watch?v=X2QMN0a_TrA
    /// </summary>
    private void VaporizeCheater(ICommonSession targetPlayer)
    {
        _adminSystem.Erase(targetPlayer.UserId);

        var feedbackMessage = "[font size=24][color=#ff0000]You have spawned in with an illegal trait point total. If this was a result of cheats, then your nonexistence is a skill issue. Otherwise, feel free to click 'Return To Lobby', and fix your trait selections.[/color][/font]";
        _chatManager.ChatMessageToOne(
            ChatChannel.Server,
            feedbackMessage,
            feedbackMessage,
            EntityUid.Invalid,
            false,
            targetPlayer.Channel);
    }
}
