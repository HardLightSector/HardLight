using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Afterlight.Vore;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Afterlight.Vore;

public sealed class VoreSystem : SharedVoreSystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly UserDbDataManager _userDb = default!;

    private readonly ConcurrentDictionary<(NetUserId Player, Guid Id), VoreSpace> _erroredSpaces = new();
    private readonly ConcurrentDictionary<NetUserId, List<VoreSpace>> _userSpaces = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoreRetrySavingEvent>(OnRetrySaving);

        _userDb.AddOnLoadPlayer(LoadData);
        _userDb.AddOnPlayerDisconnect(ClientDisconnected);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _userDb.RemoveOnLoadPlayer(LoadData);
        _userDb.RemoveOnPlayerDisconnect(ClientDisconnected);
    }

    private void OnRetrySaving(VoreRetrySavingEvent msg, EntitySessionEventArgs args)
    {
        if (!_erroredSpaces.Remove((args.SenderSession.UserId, msg.Id), out var space))
            return;

        UpdateSpaceDatabase(args.SenderSession, space);
    }

    private async Task LoadData(ICommonSession player, CancellationToken cancel)
    {
        _userSpaces[player.UserId] = await _db.GetVoreSpaces(player.UserId, cancel);
    }

    private void ClientDisconnected(ICommonSession player)
    {
        _userSpaces.Remove(player.UserId, out _);
    }

    protected override void UpdateSpaceDatabase(EntityUid player, VoreSpace space)
    {
        if (!TryComp(player, out ActorComponent? actor))
            return;

        UpdateSpaceDatabase(actor.PlayerSession, space);

        var spaces = _userSpaces[actor.PlayerSession.UserId];
        for (var i = 0; i < spaces.Count; i++)
        {
            var existingSpace = spaces[i];
            if (space.Id != existingSpace.Id)
                continue;

            spaces[i] = space;
            break;
        }
    }

    protected override void RemoveSpaceDatabase(EntityUid player, Guid space)
    {
        if (!TryComp(player, out ActorComponent? actor))
            return;

        if (_userSpaces.TryGetValue(actor.PlayerSession.UserId, out var spaces))
            spaces.RemoveAll(s => s.Id == space);

        RemoveSpaceDatabase(actor.PlayerSession.UserId, space);
    }

    protected override List<VoreSpace> GetDbSpaces(Entity<VorePredatorComponent> predator)
    {
        if (!TryComp(predator, out ActorComponent? actor))
            return new List<VoreSpace>();

        return _userSpaces.GetValueOrDefault(actor.PlayerSession.UserId)?.ToList() ?? new List<VoreSpace>();
    }

    private async void UpdateSpaceDatabase(ICommonSession player, VoreSpace space)
    {
        try
        {
            await _db.UpdateVoreSpace(player.UserId, space, CancellationToken.None);
        }
        catch (Exception e)
        {
            try
            {
                Log.Error($"Error saving vore space to database with id {space} from player {player}:\n{e}");

                _erroredSpaces[(player.UserId, space.Id)] = space;
                RaiseNetworkEvent(new VoreErrorSavingEvent(space.Id), player);
            }
            catch
            {
                // At this point even Log.Error might have errored, so abort mission so that we don't get an uncaught
                // error in an async void method
            }
        }
    }

    private async void RemoveSpaceDatabase(Guid player, Guid space)
    {
        try
        {
            await _db.DeleteVoreSpace(player, space, CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.Error($"Error deleting space {space} for player {player}:\n{e}");
        }
    }
}
