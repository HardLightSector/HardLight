using Content.Server.Database;
using Content.Shared._Afterlight.Database;

namespace Content.Server._Afterlight.Database;

public sealed class ALUserDbDataSystem : SharedALUserDbDataSystem
{
    [Dependency] private readonly UserDbDataManager _userDb = default!;

    public override void AddOnLoadPlayer(OnLoadPlayer action)
    {
        _userDb.AddOnLoadPlayer((player, cancel) => action(player, cancel));
    }

    public override void AddOnFinishLoad(OnFinishLoad action)
    {
        _userDb.AddOnFinishLoad(player => action(player));
    }

    public override void AddOnPlayerDisconnect(OnPlayerDisconnect action)
    {
        _userDb.AddOnPlayerDisconnect(player => action(player));
    }
}
