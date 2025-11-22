using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Afterlight.MobInteraction;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Afterlight.MobInteraction;

public sealed class ALMobInteractionSystem : SharedALMobInteractionSystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;

    private readonly ConcurrentDictionary<Guid, HashSet<EntProtoId<ALContentPreferenceComponent>>> _preferences = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ALMobInteractableComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<ALMobInteractableComponent, PlayerDetachedEvent>(OnDetached);

        _userDb.AddOnLoadPlayer(LoadData);
        _userDb.AddOnFinishLoad(FinishLoad);
        _userDb.AddOnPlayerDisconnect(ClientDisconnected);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _userDb.RemoveOnLoadPlayer(LoadData);
        _userDb.RemoveOnFinishLoad(FinishLoad);
        _userDb.RemoveOnPlayerDisconnect(ClientDisconnected);
    }

    private void OnAttached(Entity<ALMobInteractableComponent> ent, ref PlayerAttachedEvent args)
    {
        ent.Comp.Preferences.Clear();
        if (TryComp(ent, out ActorComponent? actor) &&
            _preferences.TryGetValue(actor.PlayerSession.UserId, out var preferences))
        {
            ent.Comp.Preferences.UnionWith(preferences);
        }

        Dirty(ent);

        var updatedEv = new ALContentPreferencesChangedEvent(ent.Comp.Preferences);
        RaiseLocalEvent(ent, updatedEv);
    }

    private void OnDetached(Entity<ALMobInteractableComponent> ent, ref PlayerDetachedEvent args)
    {
        ent.Comp.Preferences.Clear();
        Dirty(ent);

        var updatedEv = new ALContentPreferencesChangedEvent(ent.Comp.Preferences);
        RaiseLocalEvent(ent, updatedEv);
    }

    private async Task LoadData(ICommonSession player, CancellationToken cancel)
    {
        var defaultPreferences = ContentPreferencePrototypes
            .Where(p => p.Comp.DefaultValue)
            .Select(p => new EntProtoId<ALContentPreferenceComponent>(p.Entity.ID))
            .ToHashSet();

        await _db.InitContentPreferences(player.UserId, defaultPreferences, cancel);

        var preferences = await _db.GetContentPreferences(player.UserId, cancel);
        _preferences[player.UserId] = preferences;
    }

    private void FinishLoad(ICommonSession player)
    {
        SendData(player);
    }

    private void ClientDisconnected(ICommonSession player)
    {
        _preferences.Remove(player.UserId, out _);
    }

    protected override void DisablePreference(
        Entity<ALMobInteractableComponent>? ent,
        EntProtoId<ALContentPreferenceComponent> preference,
        ICommonSession? session = null)
    {
        base.DisablePreference(ent, preference, session);

        if (session == null)
        {
            if (!TryComp(ent, out ActorComponent? actor))
                return;

            session = actor.PlayerSession;
        }
        else if (TryComp(session.AttachedEntity, out ALMobInteractableComponent? interactable))
        {
            ent ??= (session.AttachedEntity.Value, interactable);
        }

        if (_preferences.TryGetValue(session.UserId, out var preferences))
            preferences.Remove(preference);

        if (ent != null)
        {
            ent.Value.Comp.Preferences.Remove(preference);
            Dirty(ent.Value);
        }

        SendData(session);
        DisablePreference(session.UserId, preference);
    }

    protected override void EnablePreference(
        Entity<ALMobInteractableComponent>? ent,
        EntProtoId<ALContentPreferenceComponent> preference,
        ICommonSession? session = null)
    {
        base.EnablePreference(ent, preference, session);

        if (session == null)
        {
            if (!TryComp(ent, out ActorComponent? actor))
                return;

            session = actor.PlayerSession;
        }
        else if (TryComp(session.AttachedEntity, out ALMobInteractableComponent? interactable))
        {
            ent ??= (session.AttachedEntity.Value, interactable);
        }

        if (_preferences.TryGetValue(session.UserId, out var preferences))
            preferences.Add(preference);

        if (ent != null)
        {
            ent.Value.Comp.Preferences.Add(preference);
            Dirty(ent.Value);
        }

        SendData(session);
        EnablePreference(session.UserId, preference);
    }

    private async void DisablePreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference)
    {
        try
        {
            await _db.DisableContentPreference(player, preference, CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.Error($"Error disabling preference {preference} for player {player}:\n{e}");
        }
    }

    private async void EnablePreference(Guid player, EntProtoId<ALContentPreferenceComponent> preference)
    {
        try
        {
            await _db.EnableContentPreference(player, preference, CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.Error($"Error enabling preference {preference} for player {player}:\n{e}");
        }
    }

    private void SendData(ICommonSession player)
    {
        var preferences = _preferences.GetValueOrDefault(player.UserId) ?? new HashSet<EntProtoId<ALContentPreferenceComponent>>();
        var ev = new ALContentPreferencesChangedEvent(preferences);

        if (player.AttachedEntity is { } ent)
            RaiseLocalEvent(ent, ev);

        RaiseNetworkEvent(ev, player);
    }
}
