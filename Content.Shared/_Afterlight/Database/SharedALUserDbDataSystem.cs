using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Player;

namespace Content.Shared._Afterlight.Database;

public abstract class SharedALUserDbDataSystem : EntitySystem
{
    public virtual void AddOnLoadPlayer(OnLoadPlayer action)
    {
    }

    public virtual void AddOnFinishLoad(OnFinishLoad action)
    {
    }

    public virtual void AddOnPlayerDisconnect(OnPlayerDisconnect action)
    {
    }

    public delegate Task OnLoadPlayer(ICommonSession player, CancellationToken cancel);

    public delegate void OnFinishLoad(ICommonSession player);

    public delegate void OnPlayerDisconnect(ICommonSession player);
}
