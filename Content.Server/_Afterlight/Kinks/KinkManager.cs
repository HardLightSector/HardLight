using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Afterlight.Kinks;
using Content.Shared.Database._Afterlight;
using Content.Shared.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Afterlight.Kinks;

public sealed class KinkManager : IPostInjectInit
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly UserDbDataManager _userDb = null!;

    public event KinksUpdated? OnKinksUpdated;

    private ISawmill _sawmill = null!;
    private ImmutableHashSet<EntProtoId<KinkDefinitionComponent>> _kinks = ImmutableHashSet<EntProtoId<KinkDefinitionComponent>>.Empty;
    private readonly Dictionary<NetUserId, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>> _connected = new();
    private readonly List<EntProtoId<KinkDefinitionComponent>> _toRemove = new();

    private async Task LoadData(ICommonSession player, CancellationToken cancel)
    {
        var dbKinks = await _db.GetKinks(player.UserId, cancel);
        cancel.ThrowIfCancellationRequested();

        var kinks = new Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>();
        foreach (var kink in dbKinks)
        {
            kinks[kink.KinkId] = kink.Preference;
        }

        _connected[player.UserId] = kinks;
    }

    private void FinishLoad(ICommonSession player)
    {
        SendKinks(player);
    }

    private void ClientDisconnected(ICommonSession player)
    {
        _connected.Remove(player.UserId);
    }

    private void SendKinks(ICommonSession player)
    {
        var connected = _connected.GetValueOrDefault(player.UserId) ?? new();
        var ev = new KinksUpdatedEvent(player.UserId, new(connected));
        OnKinksUpdated?.Invoke(ev);
    }

    public Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference>? GetKinks(NetUserId player)
    {
        return _connected.GetValueOrDefault(player);
    }

    public async void SetKink(NetUserId player, EntProtoId<KinkDefinitionComponent> kink, KinkPreference? preference)
    {
        try
        {
            await (preference == null
                ? _db.RemoveKink(player, kink, CancellationToken.None)
                : _db.SetKink(player, kink, preference.Value, CancellationToken.None));

            if (!_connected.TryGetValue(player, out var playerKinks))
                return;

            if (preference == null)
                playerKinks.Remove(kink);
            else
                playerKinks[kink] = preference.Value;

            OnPlayerUpdated(player, playerKinks);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error setting kink {kink} to {preference} for player {player}:\n{e}");
        }
    }

    /// <summary>
    ///     Sets the specified kinks to the specified preferences, but leaves any not present on the dictionary unchanged.
    /// </summary>
    public async Task UpdateKinksAsync(NetUserId player, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks)
    {
        _toRemove.Clear();
        foreach (var id in kinks.Keys)
        {
            if (!_kinks.Contains(id))
                _toRemove.Add(id);
        }

        foreach (var remove in _toRemove)
        {
            kinks.Remove(remove);
        }

        await _db.UpdateKinks(player, kinks.ToDictionary(), CancellationToken.None);

        if (!_connected.TryGetValue(player, out var playerKinks))
            return;

        foreach (var (kinkId, preference) in kinks)
        {
            playerKinks[kinkId] = preference;
        }

        OnPlayerUpdated(player, playerKinks);
    }

    /// <summary>
    ///     Sets the specified kinks to the specified preferences, but leaves any not present on the dictionary unchanged.
    /// </summary>
    public async void UpdateKinks(NetUserId player, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> kinks)
    {
        try
        {
            await UpdateKinksAsync(player, kinks);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error setting {kinks.Count.ToString() ?? "no"} kinks for player {player}:\n{e}");
        }
    }

    /// <summary>
    ///     Sets the specified kinks to the specified preferences, but leaves any not present on the dictionary unchanged.
    /// </summary>
    public async void UpdateKinks(NetUserId player, List<EntProtoId<KinkDefinitionComponent>> kinks, KinkPreference preference)
    {
        try
        {
            for (var i = kinks.Count - 1; i >= 0; i--)
            {
                var id = kinks[i];
                if (!_kinks.Contains(id))
                    kinks.RemoveAt(i);
            }

            await _db.UpdateKinks(player, kinks, preference, CancellationToken.None);

            if (!_connected.TryGetValue(player, out var playerKinks))
                return;

            foreach (var kinkId in kinks)
            {
                playerKinks[kinkId] = preference;
            }

            OnPlayerUpdated(player, playerKinks);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error setting {kinks.Count} kinks for player {player}:\n{e}");
        }
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            ReloadPrototypes();
    }

    public void ReloadPrototypes()
    {
        var builder = new HashSet<EntProtoId<KinkDefinitionComponent>>();
        foreach (var prototype in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.HasComponent<KinkDefinitionComponent>(_compFactory))
                continue;

            builder.Add(prototype.ID);
        }

        _kinks = builder.ToImmutableHashSet();
    }

    private void OnPlayerUpdated(NetUserId player, Dictionary<EntProtoId<KinkDefinitionComponent>, KinkPreference> playerKinks)
    {
        if (!_player.TryGetSessionById(player, out var session))
            return;

        var ev = new KinksUpdatedEvent(session.UserId, new(playerKinks));
        OnKinksUpdated?.Invoke(ev);
    }

    public void PostInject()
    {
        _sawmill = _log.GetSawmill(nameof(KinkManager));
        _userDb.AddOnLoadPlayer(LoadData);
        _userDb.AddOnFinishLoad(FinishLoad);
        _userDb.AddOnPlayerDisconnect(ClientDisconnected);
        _prototype.PrototypesReloaded += OnPrototypesReloaded;
    }

    public delegate void KinksUpdated(KinksUpdatedEvent ev);
}
